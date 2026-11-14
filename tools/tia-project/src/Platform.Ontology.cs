using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TiaProject;

public sealed class OntologyBuildResult
{
    public string DbPath { get; set; } = "";
    public int Nodes { get; set; }
    public int Edges { get; set; }
    public int Savepoints { get; set; }
    public Dictionary<string, int> Kinds { get; set; } = [];
}

public static class OntologyBuilder
{
    public static OntologyBuildResult Build(ProjectPaths paths, string exportPath, string dbPath)
    {
        var export = ObjectExport.Read(exportPath);
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        PlfReader.AttachIndex(plf, PlfReader.ReadIndex(paths.Idx));
        plf.EnrichWith(export.ByGuid);
        var xref = XrefReader.Read(paths.XrefDb, plf);
        var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
            Path.Combine(Path.GetTempPath(), "tia-project", "ontology-history"), export, false);

        var full = Path.GetFullPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full)) File.Delete(full);
        var cs = new SqliteConnectionStringBuilder { DataSource = full }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE nodes(id TEXT PRIMARY KEY, kind TEXT, name TEXT, class TEXT, instance_id INTEGER, guid TEXT, props TEXT);
                CREATE TABLE edges(id INTEGER PRIMARY KEY AUTOINCREMENT, src TEXT, dst TEXT, kind TEXT, provenance TEXT, confidence REAL, props TEXT);
                CREATE TABLE savepoints(idx INTEGER PRIMARY KEY, commit_guid TEXT, live INTEGER, new_count INTEGER, changed INTEGER, deleted INTEGER);
                CREATE INDEX idx_edges_src ON edges(src);
                CREATE INDEX idx_edges_dst ON edges(dst);
                CREATE INDEX idx_nodes_kind ON nodes(kind);
                """;
            cmd.ExecuteNonQuery();
        }

        var nodes = new Dictionary<string, (string Kind, string Name, string Class, long Inst, string Guid, string Props)>(StringComparer.Ordinal);
        void AddNode(string id, string kind, string? name, string cls, long inst, Guid guid, object props)
        {
            nodes[id] = (kind, name ?? id, cls, inst, guid.ToString(), JsonSerializer.Serialize(props, JsonUtil.Options));
        }

        foreach (var o in export.Objects)
        {
            if (o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal))
                AddNode("block:" + (o.Name ?? o.InstanceId.ToString()), "block", o.Name, o.ClassName, o.InstanceId, o.Guid,
                    new { o.Subtype, o.Number, o.ProgrammingLanguage, sections = InterfaceSections(o, export) });
            else if (o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
                AddNode("tag:" + (o.Name ?? o.InstanceId.ToString()), "tag", o.Name, o.ClassName, o.InstanceId, o.Guid,
                    new { address = PlatformIo.Attr(o, "LogicalAddress"), type = PlatformIo.Attr(o, "DisplayTypeName") });
            else if (o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal))
                AddNode("screen:" + (o.Name ?? o.InstanceId.ToString()), "screen", o.Name, o.ClassName, o.InstanceId, o.Guid,
                    new { width = PlatformIo.Attr(o, "Width"), height = PlatformIo.Attr(o, "Height"), o.Subtype });
            else if (o.ClassName.EndsWith("HmiScreenItemData", StringComparison.Ordinal))
                AddNode("screenitem:" + (o.Name ?? o.InstanceId.ToString()), "screenitem", o.Name, o.ClassName, o.InstanceId, o.Guid,
                    new { left = PlatformIo.Attr(o, "Left"), top = PlatformIo.Attr(o, "Top"), width = PlatformIo.Attr(o, "Width"), height = PlatformIo.Attr(o, "Height"), o.Subtype });
            else if (o.ClassName.Contains("DeviceData", StringComparison.Ordinal) || o.ClassName.Contains("HmiApplicationData", StringComparison.Ordinal))
                AddNode("device:" + (o.Name ?? o.InstanceId.ToString()), "device", o.Name, o.ClassName, o.InstanceId, o.Guid,
                    new { o.Subtype, o.Name });
            else if (o.ClassName.EndsWith("DeviceItemData", StringComparison.Ordinal))
                AddNode("deviceitem:" + (o.Name ?? o.InstanceId.ToString()), "deviceitem", o.Name, o.ClassName, o.InstanceId, o.Guid,
                    new { o.Subtype, order = PlatformIo.Attr(o, "OrderNumber"), position = PlatformIo.Attr(o, "PositionNumber") });
        }

        var edges = new List<(string Src, string Dst, string Kind, string Prov, double Conf, string Props)>();
        void AddEdge(string src, string dst, string kind, string prov, double conf, object? props = null)
        {
            if (!nodes.ContainsKey(src) || !nodes.ContainsKey(dst)) return;
            edges.Add((src, dst, kind, prov, conf, JsonSerializer.Serialize(props ?? new { }, JsonUtil.Options)));
        }

        foreach (var o in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            var src = "block:" + (o.Name ?? o.InstanceId.ToString());
            var body = PlatformIo.Body(o, export);
            if (body is null) continue;
            foreach (var sym in body.Descendants().Where(e => e.Name.LocalName == "SymVa"))
            {
                var name = sym.Attribute("ODN")?.Value;
                if (!string.IsNullOrEmpty(name)) AddEdge(src, "tag:" + name, "references", "scl-ast", 0.8);
            }
            foreach (var comp in body.Descendants().Where(e => e.Name.LocalName == "Component"))
            {
                var name = comp.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(name)) AddEdge(src, "tag:" + name, "references", "scl-ast", 0.8);
            }
        }

        foreach (var item in export.Objects.Where(o => o.ClassName.EndsWith("HmiScreenItemData", StringComparison.Ordinal)))
        {
            var screenLink = item.Links.FirstOrDefault(l => l.TypeId == 13307909);
            if (screenLink.InstanceId == 0) continue;
            var screen = export.Objects.FirstOrDefault(o => o.InstanceId == screenLink.InstanceId);
            if (screen is null) continue;
            AddEdge("screen:" + (screen.Name ?? screen.InstanceId.ToString()),
                "screenitem:" + (item.Name ?? item.InstanceId.ToString()), "contains", "hmi-object", 0.95);
        }

        foreach (var rel in xref.Relations)
        {
            var left = xref.Objects.FirstOrDefault(o => o.Id == rel.Left);
            var right = xref.Objects.FirstOrDefault(o => o.Id == rel.Right);
            if (left is null || right is null) continue;
            var lp = xref.Part(left.PrimaryPartId);
            var rp = xref.Part(right.PrimaryPartId);
            if (lp is null || rp is null) continue;
            var ls = NodeForPart(lp);
            var rs = NodeForPart(rp);
            if (ls is null || rs is null) continue;
            AddEdge(ls, rs, rel.RelationKind ?? "relation", "xref", 0.9, new { rel.LeftType, rel.RightType, rel.RelationName });
        }

        using (var tx = conn.BeginTransaction())
        {
            foreach (var kv in nodes)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO nodes(id,kind,name,class,instance_id,guid,props) VALUES($id,$kind,$name,$class,$inst,$guid,$props)";
                cmd.Parameters.AddWithValue("$id", kv.Key);
                cmd.Parameters.AddWithValue("$kind", kv.Value.Kind);
                cmd.Parameters.AddWithValue("$name", kv.Value.Name);
                cmd.Parameters.AddWithValue("$class", kv.Value.Class);
                cmd.Parameters.AddWithValue("$inst", kv.Value.Inst);
                cmd.Parameters.AddWithValue("$guid", kv.Value.Guid);
                cmd.Parameters.AddWithValue("$props", kv.Value.Props);
                cmd.ExecuteNonQuery();
            }
            foreach (var e in edges)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO edges(src,dst,kind,provenance,confidence,props) VALUES($src,$dst,$kind,$prov,$conf,$props)";
                cmd.Parameters.AddWithValue("$src", e.Src);
                cmd.Parameters.AddWithValue("$dst", e.Dst);
                cmd.Parameters.AddWithValue("$kind", e.Kind);
                cmd.Parameters.AddWithValue("$prov", e.Prov);
                cmd.Parameters.AddWithValue("$conf", e.Conf);
                cmd.Parameters.AddWithValue("$props", e.Props);
                cmd.ExecuteNonQuery();
            }
            foreach (var sp in history.Savepoints)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO savepoints(idx,commit_guid,live,new_count,changed,deleted) VALUES($idx,$guid,$live,$new,$changed,$deleted)";
                cmd.Parameters.AddWithValue("$idx", sp.Index);
                cmd.Parameters.AddWithValue("$guid", (object?)sp.CommitGuid?.ToString() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$live", sp.Live);
                cmd.Parameters.AddWithValue("$new", sp.New);
                cmd.Parameters.AddWithValue("$changed", sp.Changed);
                cmd.Parameters.AddWithValue("$deleted", sp.Deleted);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        return new OntologyBuildResult
        {
            DbPath = full,
            Nodes = nodes.Count,
            Edges = edges.Count,
            Savepoints = history.Savepoints.Count,
            Kinds = nodes.Values.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private static string? NodeForPart(XrefPart p)
    {
        var name = p.Name ?? p.InstanceId?.ToString();
        if (string.IsNullOrEmpty(name)) return null;
        if (p.ClassName?.EndsWith(".CodeBlockData", StringComparison.Ordinal) == true) return "block:" + name;
        if (p.ClassName?.EndsWith("EAMTZTagData", StringComparison.Ordinal) == true) return "tag:" + name;
        if (p.ClassName?.EndsWith("HmiScreenData", StringComparison.Ordinal) == true) return "screen:" + name;
        if (p.ClassName?.Contains("DeviceData", StringComparison.Ordinal) == true) return "device:" + name;
        return null;
    }

    private static List<string> InterfaceSections(ExportedObjectInfo block, ObjectExport export)
    {
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2233868);
        if (link.InstanceId == 0) return [];
        var root = export.Objects.FirstOrDefault(o => o.InstanceId == link.InstanceId);
        if (root is null || !root.CoreBlobs.TryGetValue("Payload", out var hex)) return [];
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return [];
        try
        {
            return System.Xml.Linq.XDocument.Parse(text).Root?.Elements()
                .Where(e => e.Name.LocalName == "Member")
                .Select(e => e.Attribute("Name")?.Value)
                .Where(n => !string.IsNullOrEmpty(n)).Cast<string>().ToList() ?? [];
        }
        catch { return []; }
    }
}

public sealed class OntologyQueryResult
{
    public string Sql { get; set; } = "";
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
}

public static class OntologyQuery
{
    public static OntologyQueryResult Query(string dbPath, string sql)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var result = new OntologyQueryResult { Sql = sql };
        for (int i = 0; i < r.FieldCount; i++) result.Columns.Add(r.GetName(i));
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++)
                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            result.Rows.Add(row);
        }
        return result;
    }
}
