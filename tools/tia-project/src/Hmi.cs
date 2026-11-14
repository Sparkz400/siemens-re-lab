using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace TiaProject;

public sealed class HmiColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int MaxLength { get; set; }
    public bool Nullable { get; set; }
}

public sealed class HmiTableInfo
{
    public string Name { get; set; } = "";
    public long RowCount { get; set; }
    public List<HmiColumn> Columns { get; set; } = [];
    public string? JsonFile { get; set; }
}

public sealed class HmiSummary
{
    public long VariablesTotal { get; set; }
    public long VariablesUser { get; set; }
    public long Channels { get; set; }
    public long Connections { get; set; }
    public long Blocks { get; set; }
    public long Messages { get; set; }
    public JsonNode? BlockList { get; set; }
    public JsonNode? UserTags { get; set; }
    public JsonNode? ConnectionsList { get; set; }
}

public sealed class HmiExportResult
{
    public string BackupPath { get; set; } = "";
    public long BackupLength { get; set; }
    public string Database { get; set; } = "";
    public string OutPath { get; set; } = "";
    public string Instance { get; set; } = "";
    public bool Kept { get; set; }
    public string? DataPath { get; set; }
    public List<HmiTableInfo> Tables { get; set; } = [];
    public HmiSummary Summary { get; set; } = new();
}

public static class HmiExporter
{
    public static HmiExportResult Export(ProjectPaths paths, string outDir, string instance = @".\WINCC",
        bool keep = false, int maxRowsPerTable = 20000, int timeoutSeconds = 300)
    {
        if (paths.HmiBackup is null || !File.Exists(paths.HmiBackup))
            throw new CliException("no HMI .bak found under IM/HMI");
        var outFull = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outFull);

        var backup = paths.HmiBackup;
        var work = Path.Combine(Path.GetTempPath(), "tia-project", "hmi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var localBak = Path.Combine(work, "hmi.bak");
        using (var src = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var dst = File.Create(localBak))
            src.CopyTo(dst);

        TryGrantServiceAccess(work);
        TryGrantServiceAccess(localBak);

        var db = "TiaProjectHmi_" + Environment.ProcessId + "_" + Random.Shared.Next(1000, 9999);
        var result = new HmiExportResult
        {
            BackupPath = Path.GetFullPath(backup),
            BackupLength = new FileInfo(localBak).Length,
            Database = db,
            OutPath = outFull,
            Instance = instance,
            Kept = keep
        };

        try
        {
            result.DataPath = GetDataPath(instance);
            var (mdfLogical, ldfLogical) = GetLogicalNames(instance, localBak);
            var mdf = Path.Combine(result.DataPath, db + ".mdf");
            var ldf = Path.Combine(result.DataPath, db + ".ldf");

            Execute(instance, null, $"IF DB_ID(N'{Lit(db)}') IS NOT NULL DROP DATABASE [{db}];", timeoutSeconds);
            var restore = $"""
                RESTORE DATABASE [{db}] FROM DISK=N'{Lit(localBak)}'
                  WITH MOVE N'{Lit(mdfLogical)}' TO N'{Lit(mdf)}',
                       MOVE N'{Lit(ldfLogical)}' TO N'{Lit(ldf)}',
                       REPLACE, RECOVERY;
                """;
            Execute(instance, null, restore, timeoutSeconds);

            var tables = GetTables(instance, db);
            foreach (var t in tables)
            {
                t.Columns = GetColumns(instance, db, t.Name);
                if (t.RowCount <= 0 || t.RowCount > maxRowsPerTable) continue;
                var rows = Query(instance, db, $"SELECT * FROM {Ident(t.Name)}");
                var file = Path.Combine(outFull, "tables", SafeFileName(t.Name) + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(rows, TableJsonOptions), new UTF8Encoding(false));
                t.JsonFile = file;
            }
            result.Tables = tables;
            result.Summary = GetSummary(instance, db);
            WriteResult(result, outFull);
            return result;
        }
        finally
        {
            if (!keep)
            {
                try
                {
                    SqlConnection.ClearAllPools();
                    Execute(instance, null, $"IF DB_ID(N'{Lit(db)}') IS NOT NULL DROP DATABASE [{db}];", timeoutSeconds);
                }
                catch { }
                FileUtil.TryDeleteDirectory(work);
            }
            else
            {
                Console.Error.WriteLine($"kept database [{db}] and working directory {work}");
            }
        }
    }

    private static readonly JsonSerializerOptions TableJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void WriteResult(HmiExportResult result, string outDir)
    {
        var schema = new
        {
            result.BackupPath,
            result.BackupLength,
            result.Database,
            result.Instance,
            result.DataPath,
            tables = result.Tables.Select(t => new { t.Name, t.RowCount, t.Columns, t.JsonFile })
        };
        File.WriteAllText(Path.Combine(outDir, "schema.json"), JsonUtil.Serialize(schema), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonUtil.Serialize(result.Summary), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "export.json"), JsonUtil.Serialize(result), new UTF8Encoding(false));
    }

    private static SqlConnection Open(string instance, string? db)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instance,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            Encrypt = false,
            InitialCatalog = string.IsNullOrWhiteSpace(db) ? "master" : db,
            ConnectTimeout = 15,
            Pooling = false,
            ApplicationName = "tia-project"
        };
        var conn = new SqlConnection(builder.ConnectionString);
        conn.Open();
        return conn;
    }

    private static void Execute(string instance, string? db, string sql, int timeoutSeconds)
    {
        using var conn = Open(instance, db);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        cmd.ExecuteNonQuery();
    }

    private static List<Dictionary<string, object?>> Query(string instance, string db, string sql, int timeoutSeconds = 300)
    {
        using var conn = Open(instance, db);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        using var reader = cmd.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (value is byte[] bytes)
                    value = Convert.ToHexString(bytes).ToLowerInvariant();
                row[reader.GetName(i)] = value;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static object? Scalar(string instance, string db, string sql)
    {
        using var conn = Open(instance, db);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        return cmd.ExecuteScalar();
    }

    private static List<HmiTableInfo> GetTables(string instance, string db)
    {
        var rows = Query(instance, db, """
            SELECT t.name AS Name, SUM(p.rows) AS Rows
            FROM sys.tables t
            JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
            GROUP BY t.name
            ORDER BY t.name;
            """);
        return rows.Select(r => new HmiTableInfo
        {
            Name = r["Name"]?.ToString() ?? "",
            RowCount = r["Rows"] is null ? 0 : Convert.ToInt64(r["Rows"])
        }).Where(t => t.Name.Length > 0).ToList();
    }

    private static List<HmiColumn> GetColumns(string instance, string db, string table)
    {
        var rows = Query(instance, db, $"""
            SELECT c.name AS Name, ty.name AS Type, c.max_length AS MaxLength, c.is_nullable AS Nullable
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'{Lit(table)}')
            ORDER BY c.column_id;
            """);
        return rows.Select(r => new HmiColumn
        {
            Name = r["Name"]?.ToString() ?? "",
            Type = r["Type"]?.ToString() ?? "",
            MaxLength = r["MaxLength"] is null ? 0 : Convert.ToInt32(r["MaxLength"]),
            Nullable = r["Nullable"] is bool b ? b : Convert.ToInt32(r["Nullable"] ?? 0) != 0
        }).ToList();
    }

    private static HmiSummary GetSummary(string instance, string db)
    {
        var summary = new HmiSummary();
        var vars = Query(instance, db, """
            SELECT COUNT(*) AS total,
                   SUM(CASE WHEN VARIABLENAME NOT LIKE '@%' THEN 1 ELSE 0 END) AS userTags
            FROM MCPTVARIABLEDESC;
            """);
        if (vars.Count > 0)
        {
            summary.VariablesTotal = Convert.ToInt64(vars[0]["total"] ?? 0);
            summary.VariablesUser = Convert.ToInt64(vars[0]["userTags"] ?? 0);
        }

        summary.UserTags = JsonSerializer.SerializeToNode(Query(instance, db, """
            SELECT TOP 100 VARIABLENAME, VARIABLETYPEID, ADDRESSPARAMETER, CONNECTIONID
            FROM MCPTVARIABLEDESC WHERE VARIABLENAME NOT LIKE '@%'
            ORDER BY VARIABLENAME;
            """), TableJsonOptions);

        summary.Channels = Count(instance, db, "MCPTCHANNEL");
        summary.Connections = Count(instance, db, "MCPTCONNECTION");
        summary.Blocks = Count(instance, db, "MSBlock");
        summary.Messages = Count(instance, db, "MSMsgs");

        summary.BlockList = JsonSerializer.SerializeToNode(Query(instance, db, """
            SELECT TOP 100 b.BLOCK_TYP, b.BLOCK_NUM, b.BLOCK_NAME, t.L1033 AS NAME
            FROM MSBlock b LEFT JOIN TXTTable t ON t.TEXTID = b.BLOCK_NAME
            ORDER BY b.BLOCK_TYP, b.BLOCK_NUM;
            """), TableJsonOptions);
        summary.ConnectionsList = JsonSerializer.SerializeToNode(Query(instance, db, """
            SELECT CONNECTIONID, CONNECTIONNAME, PARAMETER, PLCNAME, CHANNELUNITID
            FROM MCPTCONNECTION;
            """), TableJsonOptions);
        return summary;
    }

    private static long Count(string instance, string db, string table)
    {
        var value = Scalar(instance, db, $"SELECT COUNT(*) FROM {Ident(table)};");
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static string GetDataPath(string instance)
    {
        var value = Scalar(instance, "master", "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000));")?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) return value;
        return @"C:\Program Files\Microsoft SQL Server\MSSQL16.WINCC\MSSQL\DATA";
    }

    private static (string Mdf, string Ldf) GetLogicalNames(string instance, string bak)
    {
        var rows = Query(instance, "master", $"RESTORE FILELISTONLY FROM DISK=N'{Lit(bak)}';");
        string? mdf = null, ldf = null;
        foreach (var row in rows)
        {
            var logical = row.TryGetValue("LogicalName", out var ln) ? ln?.ToString() : null;
            var type = row.TryGetValue("Type", out var ty) ? ty?.ToString() : null;
            if (string.IsNullOrEmpty(logical)) continue;
            if (type?.StartsWith("L", StringComparison.OrdinalIgnoreCase) == true) ldf ??= logical;
            else mdf ??= logical;
        }
        return (mdf ?? "WinCC_SQL.mdf", ldf ?? "WinCC_SQL.ldf");
    }

    private static void TryGrantServiceAccess(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "icacls",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("/grant");
            psi.ArgumentList.Add(@"NT SERVICE\MSSQL$WINCC:(OI)(CI)F");
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch { }
    }

    private static string Lit(string value) => value.Replace("'", "''");
    private static string Ident(string value) => "[" + value.Replace("]", "]]") + "]";

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
