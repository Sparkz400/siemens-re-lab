using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TiaProject;

public sealed class ApiCallResult
{
    public string Verb { get; set; } = "";
    public Dictionary<string, string> Arguments { get; set; } = [];
    public int Count { get; set; }
    public object? Result { get; set; }
}

public static class PlatformApi
{
    public static ApiCallResult Call(string dbPath, string verb, Dictionary<string, string> args)
    {
        var result = new ApiCallResult { Verb = verb, Arguments = args };
        switch (verb.ToLowerInvariant())
        {
            case "resolve":
                result.Result = Rows(dbPath,
                    "SELECT id,kind,name,class,instance_id,guid,props FROM nodes WHERE id=$id OR name=$name LIMIT 20",
                    ("$id", args.GetValueOrDefault("id", args.GetValueOrDefault("node", ""))),
                    ("$name", args.GetValueOrDefault("name", "")));
                break;
            case "callers":
                result.Result = Rows(dbPath,
                    "SELECT e.src,e.dst,e.kind,e.provenance,e.confidence,n.name AS src_name FROM edges e LEFT JOIN nodes n ON n.id=e.src WHERE e.dst=$id AND e.kind IN ('references','calls','uses')",
                    ("$id", args.GetValueOrDefault("id", args.GetValueOrDefault("block", ""))));
                break;
            case "writers":
                result.Result = Rows(dbPath,
                    "SELECT e.src,e.dst,e.kind,e.provenance,e.confidence,n.name AS src_name FROM edges e LEFT JOIN nodes n ON n.id=e.src WHERE e.dst=$id AND e.kind IN ('references','uses','writes')",
                    ("$id", args.GetValueOrDefault("id", args.GetValueOrDefault("tag", ""))));
                break;
            case "screens_using_tag":
                result.Result = Rows(dbPath,
                    "SELECT s.id AS screen, si.id AS item, si.name AS item_name FROM nodes s JOIN edges e ON e.src=s.id AND e.kind='contains' JOIN nodes si ON si.id=e.dst WHERE s.kind='screen' AND (si.props LIKE $tag OR si.name LIKE $tag)",
                    ("$tag", "%" + args.GetValueOrDefault("tag", "") + "%"));
                break;
            case "impact":
                result.Result = Impact(dbPath, args.GetValueOrDefault("id", args.GetValueOrDefault("node", "")), 4);
                break;
            case "diff":
                result.Result = Rows(dbPath,
                    "SELECT * FROM savepoints WHERE idx BETWEEN $a AND $b ORDER BY idx",
                    ("$a", args.GetValueOrDefault("a", "0")),
                    ("$b", args.GetValueOrDefault("b", "9999")));
                break;
            case "stats":
                result.Result = Rows(dbPath,
                    "SELECT kind, COUNT(*) AS n FROM nodes GROUP BY kind ORDER BY n DESC",
                    Array.Empty<(string, object?)>());
                break;
            case "query":
                result.Result = Rows(dbPath, args.GetValueOrDefault("sql", "SELECT 1"), Array.Empty<(string, object?)>());
                break;
            default:
                throw new CliException($"unknown API verb: {verb}");
        }
        result.Count = result.Result is List<Dictionary<string, object?>> list ? list.Count : 1;
        return result;
    }

    private static List<Dictionary<string, object?>> Impact(string dbPath, string start, int depth)
    {
        var visited = new Dictionary<string, int> { [start] = 0 };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (id, d) = queue.Dequeue();
            if (d >= depth) continue;
            foreach (var row in Rows(dbPath,
                         "SELECT src,dst,kind,provenance,confidence FROM edges WHERE src=$id OR dst=$id",
                         ("$id", id)))
            {
                var other = row["src"]?.ToString() == id ? row["dst"]?.ToString() : row["src"]?.ToString();
                if (string.IsNullOrEmpty(other) || visited.ContainsKey(other)) continue;
                visited[other] = d + 1;
                queue.Enqueue((other, d + 1));
            }
        }
        return visited.Select(kv => new Dictionary<string, object?>
        {
            ["id"] = kv.Key,
            ["depth"] = kv.Value,
            ["node"] = Rows(dbPath, "SELECT kind,name,class FROM nodes WHERE id=$id", ("$id", kv.Key)).FirstOrDefault()
        }).ToList();
    }

    private static List<Dictionary<string, object?>> Rows(string dbPath, string sql, params (string Name, object? Value)[] parameters)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value ?? "");
        using var r = cmd.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++)
                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}

public sealed class ApiBenchmarkTask
{
    public string Verb { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = [];
    public int ExpectedMin { get; set; }
    public int Actual { get; set; }
    public bool Pass { get; set; }
}

public sealed class ApiBenchmarkResult
{
    public string DbPath { get; set; } = "";
    public int Tasks { get; set; }
    public int Passed { get; set; }
    public double Score { get; set; }
    public List<ApiBenchmarkTask> Results { get; set; } = [];
}

public static class ApiBenchmark
{
    public static ApiBenchmarkResult Run(string dbPath)
    {
        var nodes = PlatformApi.Call(dbPath, "query", new() { ["sql"] = "SELECT id,kind,name FROM nodes" });
        var list = (List<Dictionary<string, object?>>)nodes.Result!;
        var tasks = new List<ApiBenchmarkTask>();
        foreach (var block in list.Where(n => n["kind"]?.ToString() == "block").Take(5))
        {
            tasks.Add(new ApiBenchmarkTask
            {
                Verb = "resolve",
                Args = new() { ["id"] = block["id"]?.ToString() ?? "" },
                ExpectedMin = 1
            });
        }
        foreach (var tag in list.Where(n => n["kind"]?.ToString() == "tag").Take(5))
        {
            tasks.Add(new ApiBenchmarkTask
            {
                Verb = "writers",
                Args = new() { ["id"] = tag["id"]?.ToString() ?? "" },
                ExpectedMin = 0
            });
        }
        tasks.Add(new ApiBenchmarkTask { Verb = "stats", Args = new(), ExpectedMin = 1 });
        foreach (var t in tasks)
        {
            var r = PlatformApi.Call(dbPath, t.Verb, t.Args);
            t.Actual = r.Count;
            t.Pass = r.Count >= t.ExpectedMin;
        }
        return new ApiBenchmarkResult
        {
            DbPath = dbPath,
            Tasks = tasks.Count,
            Passed = tasks.Count(t => t.Pass),
            Score = tasks.Count == 0 ? 0 : (double)tasks.Count(t => t.Pass) / tasks.Count,
            Results = tasks
        };
    }
}
