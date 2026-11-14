using Microsoft.Data.Sqlite;

namespace TiaProject;

public sealed class VciTable
{
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public long RowCount { get; set; }
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public bool Truncated { get; set; }
}

public sealed class VciData
{
    public string FilePath { get; set; } = "";
    public List<VciTable> Tables { get; set; } = [];
    public Dictionary<string, long> RowCounts { get; set; } = [];
}

public static class VciReader
{
    public static VciData Read(string dbPath, int maxRowsPerTable = 5000)
    {
        var temp = FileUtil.CopyToTemp(dbPath, Path.Combine(Path.GetTempPath(), "tia-project", "vci"));
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = temp,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            var data = new VciData { FilePath = Path.GetFullPath(dbPath) };

            var tables = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read()) tables.Add(r.GetString(0));
            }

            foreach (var table in tables)
            {
                var t = new VciTable { Name = table };
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) t.Columns.Add(r.GetString(1));
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                    t.RowCount = Convert.ToInt64(cmd.ExecuteScalar());
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM \"{table}\" LIMIT {maxRowsPerTable}";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                        for (int i = 0; i < r.FieldCount; i++)
                        {
                            object? value = r.IsDBNull(i) ? null : r.GetValue(i);
                            if (value is byte[] bytes)
                                value = TextUtil.AsciiOrHex(bytes) is { } text && text.All(c => c is >= ' ' or '\n' or '\r' or '\t')
                                    ? text
                                    : Convert.ToHexString(bytes).ToLowerInvariant();
                            row[r.GetName(i)] = value;
                        }
                        t.Rows.Add(row);
                    }
                }
                t.Truncated = t.RowCount > maxRowsPerTable;
                data.Tables.Add(t);
                data.RowCounts[table] = t.RowCount;
            }
            return data;
        }
        finally
        {
            FileUtil.TryDelete(temp);
            FileUtil.TryDeleteDirectory(Path.GetDirectoryName(temp)!);
        }
    }
}
