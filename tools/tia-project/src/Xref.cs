using System.Text;
using Microsoft.Data.Sqlite;

namespace TiaProject;

public sealed class XrefInfo
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string? SavepointId { get; set; }
}

public sealed class XrefPart
{
    public int Id { get; set; }
    public string RawHex { get; set; } = "";
    public bool IsNull { get; set; }
    public int? TypeId { get; set; }
    public long? InstanceId { get; set; }
    public string? ClassName { get; set; }
    public string? Name { get; set; }
    public long? PlfOffset { get; set; }
    public int? PlfSize { get; set; }
    public bool? Live { get; set; }
    public string? ObjectId => TypeId is null ? null : $"{TypeId}/{InstanceId}";
}

public sealed class XrefObject
{
    public int Id { get; set; }
    public int ScopePartId { get; set; }
    public int PrimaryPartId { get; set; }
    public string SubObjectHex { get; set; } = "";
}

public sealed class XrefRelation
{
    public int LeftType { get; set; }
    public int Left { get; set; }
    public int BindingObject { get; set; }
    public string? LeftFilter { get; set; }
    public string? Location { get; set; }
    public int RightType { get; set; }
    public int Right { get; set; }
    public string? RightFilter { get; set; }
    public string? RelationInfoHex { get; set; }
    public string? RelationInfoAscii { get; set; }
    public string? LeftClassName { get; set; }
    public string? RightClassName { get; set; }
    public string? RelationName { get; set; }
    public string? RelationKind { get; set; }
}

public static class RelationSemantics
{
    public static (string Name, string Kind) Describe(string? leftClass, string? rightClass, int leftType, int rightType)
    {
        var l = leftClass ?? "";
        var r = rightClass ?? "";

        if (l.Contains("CodeBlockData", StringComparison.Ordinal) && r.Contains("EAMTZTagData", StringComparison.Ordinal))
            return ("block uses tag", "uses");
        if (l.Contains("S7ControllerTargetData", StringComparison.Ordinal) && r.Contains("CodeBlockData", StringComparison.Ordinal))
            return ("controller contains block", "contains");
        if (l.Contains("S7ControllerTargetData", StringComparison.Ordinal) && r.Contains("EAMTZTagData", StringComparison.Ordinal))
            return ("controller contains tag", "contains");
        if (l.Contains("S7ControllerTargetData", StringComparison.Ordinal) && r.Contains("DataTypeItemData", StringComparison.Ordinal))
            return ("controller contains type", "contains");
        if (l.Contains("HmiRuntimeSettingsData", StringComparison.Ordinal) && r.Contains("HmiScreenData", StringComparison.Ordinal))
            return ("HMI runtime uses screen", "uses");
        if (l.Contains("HmiRuntimeSettingsData", StringComparison.Ordinal) && r.Contains("HmiThemeData", StringComparison.Ordinal))
            return ("HMI runtime uses theme", "uses");
        if (l.Contains("HmiScreenData", StringComparison.Ordinal) && r.Contains("HmiCycleData", StringComparison.Ordinal))
            return ("screen uses cycle", "uses");
        if (l.Contains("HmiRTGroupData", StringComparison.Ordinal) && r.Contains("HmiRTAuthorizationData", StringComparison.Ordinal))
            return ("group grants authorization", "contains");
        if (l.Contains("HmiRTUserData", StringComparison.Ordinal) && r.Contains("HmiRTGroupData", StringComparison.Ordinal))
            return ("user belongs to group", "belongs");
        if (l.Contains("HmiApplicationData", StringComparison.Ordinal) && r.Contains("HmiScreenData", StringComparison.Ordinal))
            return ("HMI application uses screen", "uses");

        // XRefRelationType is a flags enum; the low bits carry the base relation.
        var low = rightType & 0x7F;
        var baseName = low switch
        {
            1 => "uses",
            2 => "is instance of",
            4 => "assigns",
            8 => "scope",
            0x10 => "overlaps",
            0x20 => "defines",
            0x40 => "belongs",
            0x80 => "mapping",
            _ => null
        };
        if (baseName is not null)
            return ($"{baseName} ({leftType}->{rightType})", baseName);
        return ($"relation {leftType}->{rightType}", "relation");
    }
}

public sealed class XrefAddress
{
    public int Obj { get; set; }
    public string? Filter { get; set; }
    public int Type { get; set; }
    public string? Range { get; set; }
    public int FirstBit { get; set; }
    public int LastBit { get; set; }
}

public sealed class XrefSummary
{
    public int PartCount { get; set; }
    public int NullParts { get; set; }
    public int DecodedParts { get; set; }
    public int UnmatchedParts { get; set; }
    public int ObjectCount { get; set; }
    public int RelationCount { get; set; }
    public int AddressCount { get; set; }
    public Dictionary<string, int> RelationTypes { get; set; } = [];
}

public sealed class XrefData
{
    public XrefInfo? Info { get; set; }
    public List<XrefPart> Parts { get; set; } = [];
    public List<XrefObject> Objects { get; set; } = [];
    public List<XrefRelation> Relations { get; set; } = [];
    public List<XrefAddress> Addresses { get; set; } = [];
    public XrefSummary Summary { get; set; } = new();

    public XrefPart? Part(int id) => Parts.FirstOrDefault(p => p.Id == id);
    public XrefObject? Object(int id) => Objects.FirstOrDefault(o => o.Id == id);

    public string ToDot()
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph XRef {");
        sb.AppendLine("  rankdir=LR;");
        sb.AppendLine("  node [shape=box, fontname=\"Consolas\"];");
        foreach (var p in Parts.Where(p => !p.IsNull))
        {
            var label = $"{p.TypeId}/{p.InstanceId}\\n{p.ClassName ?? "?"}";
            if (!string.IsNullOrEmpty(p.Name)) label += $"\\n{p.Name}";
            sb.AppendLine($"  p{p.Id} [label=\"{Escape(label)}\"];");
        }
        foreach (var r in Relations)
        {
            var l = Object(r.Left);
            var rr = Object(r.Right);
            if (l is null || rr is null) continue;
            if (Part(l.PrimaryPartId)?.IsNull != false || Part(rr.PrimaryPartId)?.IsNull != false) continue;
            sb.AppendLine($"  p{l.PrimaryPartId} -> p{rr.PrimaryPartId} [label=\"{r.RelationName ?? $"{r.LeftType}->{r.RightType}"}\"];");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public ImpactResult Impact(string target, int maxDepth = 12)
    {
        var startObjs = new HashSet<int>();
        XrefPart? startPart = null;
        var t = target.Trim();
        if (t.StartsWith("part:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(t[5..], out var pid)) startPart = Part(pid);
        }
        else if (t.StartsWith("obj:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(t[4..], out var oid)) startObjs.Add(oid);
        }
        else if (t.Contains('/'))
        {
            var parts = t.Split('/', 2);
            if (int.TryParse(parts[0], out var tid) && long.TryParse(parts[1], out var iid))
                startPart = Parts.FirstOrDefault(p => p.TypeId == tid && p.InstanceId == iid);
        }
        else if (t.StartsWith('{') && t.EndsWith('}') && t.Contains('-'))
        {
            var inner = t[1..^1];
            var parts = inner.Split('-', 2);
            if (int.TryParse(parts[0], out var tid) && long.TryParse(parts[1], out var iid))
                startPart = Parts.FirstOrDefault(p => p.TypeId == tid && p.InstanceId == iid);
        }
        else if (int.TryParse(t, out var pid2))
        {
            startPart = Part(pid2);
        }

        if (startPart is not null)
        {
            foreach (var o in Objects.Where(o => o.ScopePartId == startPart.Id || o.PrimaryPartId == startPart.Id))
                startObjs.Add(o.Id);
        }

        if (startObjs.Count == 0)
            throw new CliException($"target not found or has no objects: {target}");

        var visited = new Dictionary<int, int>();
        var queue = new Queue<(int Obj, int Depth)>();
        foreach (var o in startObjs) { visited[o] = 0; queue.Enqueue((o, 0)); }
        var edges = new List<ImpactEdge>();
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var (objId, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;
            foreach (var r in Relations.Where(r => r.Left == objId || r.Right == objId))
            {
                var other = r.Left == objId ? r.Right : r.Left;
                var direction = r.Left == objId ? "out" : "in";
                var key = $"{r.LeftType}/{r.Left}->{r.RightType}/{r.Right}";
                if (seenEdges.Add(key))
                    edges.Add(new ImpactEdge { LeftType = r.LeftType, Left = r.Left, RightType = r.RightType, Right = r.Right, Direction = direction, Depth = depth });
                if (!visited.ContainsKey(other))
                {
                    visited[other] = depth + 1;
                    queue.Enqueue((other, depth + 1));
                }
            }
        }

        var nodes = visited.Select(kv =>
        {
            var obj = Object(kv.Key);
            var part = obj is null ? null : Part(obj.PrimaryPartId);
            return new ImpactNode
            {
                Depth = kv.Value,
                ObjectId = kv.Key,
                PartId = obj?.PrimaryPartId,
                TypeId = part?.TypeId,
                InstanceId = part?.InstanceId,
                ClassName = part?.ClassName,
                Name = part?.Name
            };
        }).OrderBy(n => n.Depth).ThenBy(n => n.ObjectId).ToList();

        return new ImpactResult { Target = target, Nodes = nodes, Edges = edges };
    }
}

public sealed class ImpactNode
{
    public int Depth { get; set; }
    public int ObjectId { get; set; }
    public int? PartId { get; set; }
    public int? TypeId { get; set; }
    public long? InstanceId { get; set; }
    public string? ClassName { get; set; }
    public string? Name { get; set; }
}

public sealed class ImpactEdge
{
    public int LeftType { get; set; }
    public int Left { get; set; }
    public int RightType { get; set; }
    public int Right { get; set; }
    public string Direction { get; set; } = "";
    public int Depth { get; set; }
}

public sealed class ImpactResult
{
    public string Target { get; set; } = "";
    public List<ImpactNode> Nodes { get; set; } = [];
    public List<ImpactEdge> Edges { get; set; } = [];
}

public static class XrefReader
{
    public static XrefData Read(string dbPath, PlfFile? plf = null)
    {
        var temp = FileUtil.CopyToTemp(dbPath, Path.Combine(Path.GetTempPath(), "tia-project", "xref"));
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
            var data = new XrefData();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, version, savepointID FROM info";
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    data.Info = new XrefInfo
                    {
                        Id = r.GetInt32(0),
                        Version = r.GetInt32(1),
                        SavepointId = r.IsDBNull(2) ? null : r.GetString(2)
                    };
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, binary FROM parts ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var raw = r.GetValue(1) switch
                    {
                        byte[] b => b,
                        string s => Encoding.UTF8.GetBytes(s),
                        long l => BitConverter.GetBytes(l),
                        int i => BitConverter.GetBytes(i),
                        _ => []
                    };
                    var part = new XrefPart
                    {
                        Id = r.GetInt32(0),
                        RawHex = Convert.ToHexString(raw).ToLowerInvariant()
                    };
                    if (raw.Length == 1 && raw[0] == 0)
                    {
                        part.IsNull = true;
                    }
                    else if (raw.Length == 14 && raw[0] == 0x4F && raw[1] == 0x47)
                    {
                        part.TypeId = BitConverter.ToInt32(raw, 2);
                        part.InstanceId = BitConverter.ToInt64(raw, 6);
                        if (plf is not null)
                        {
                            var obj = plf.FindLive(part.TypeId.Value, part.InstanceId.Value);
                            if (obj is not null)
                            {
                                part.PlfOffset = obj.Offset;
                                part.PlfSize = obj.Size;
                                part.Live = obj.Live;
                                part.ClassName = obj.ClassName;
                                part.Name = obj.Name;
                            }
                        }
                    }
                    data.Parts.Add(part);
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, scopeObject, primaryObject, subObject FROM objs ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var sub = r.IsDBNull(3) ? "" : r.GetValue(3) switch
                    {
                        byte[] b => Convert.ToHexString(b).ToLowerInvariant(),
                        long l => l.ToString("x"),
                        int i => i.ToString("x"),
                        _ => r.GetValue(3)?.ToString() ?? ""
                    };
                    data.Objects.Add(new XrefObject
                    {
                        Id = r.GetInt32(0),
                        ScopePartId = r.GetInt32(1),
                        PrimaryPartId = r.GetInt32(2),
                        SubObjectHex = sub
                    });
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT leftType, left, bindingObject, leftFilter, location, rightType, right, rightFilter, relationInfo FROM rels ORDER BY leftType, left, rightType, right";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var rawInfo = r.IsDBNull(8) ? null : r.GetValue(8);
                    var info = rawInfo switch
                    {
                        byte[] b => b,
                        long l => BitConverter.GetBytes(l),
                        int i => BitConverter.GetBytes(i),
                        string s => Encoding.UTF8.GetBytes(s),
                        _ => null
                    };
                    data.Relations.Add(new XrefRelation
                    {
                        LeftType = r.GetInt32(0),
                        Left = r.GetInt32(1),
                        BindingObject = r.GetInt32(2),
                        LeftFilter = r.IsDBNull(3) ? null : r.GetString(3),
                        Location = r.IsDBNull(4) ? null : r.GetString(4),
                        RightType = r.GetInt32(5),
                        Right = r.GetInt32(6),
                        RightFilter = r.IsDBNull(7) ? null : r.GetString(7),
                        RelationInfoHex = info is null ? null : Convert.ToHexString(info).ToLowerInvariant(),
                        RelationInfoAscii = rawInfo is string s2 ? s2 : info is null ? null : TextUtil.AsciiOrHex(info)
                    });
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT obj, filter, type, range, firstBit, lastBit FROM addrs ORDER BY obj";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    data.Addresses.Add(new XrefAddress
                    {
                        Obj = r.GetInt32(0),
                        Filter = r.IsDBNull(1) ? null : r.GetString(1),
                        Type = r.GetInt32(2),
                        Range = r.IsDBNull(3) ? null : r.GetString(3),
                        FirstBit = r.GetInt32(4),
                        LastBit = r.GetInt32(5)
                    });
                }
            }

            var objById = data.Objects.ToDictionary(o => o.Id);
            foreach (var rel in data.Relations)
            {
                if (objById.TryGetValue(rel.Left, out var leftObj))
                    rel.LeftClassName = data.Part(leftObj.PrimaryPartId)?.ClassName;
                if (objById.TryGetValue(rel.Right, out var rightObj))
                    rel.RightClassName = data.Part(rightObj.PrimaryPartId)?.ClassName;
                var sem = RelationSemantics.Describe(rel.LeftClassName, rel.RightClassName, rel.LeftType, rel.RightType);
                rel.RelationName = sem.Name;
                rel.RelationKind = sem.Kind;
            }

            data.Summary = new XrefSummary
            {
                PartCount = data.Parts.Count,
                NullParts = data.Parts.Count(p => p.IsNull),
                DecodedParts = data.Parts.Count(p => p.TypeId is not null),
                UnmatchedParts = data.Parts.Count(p => p.TypeId is not null && p.PlfOffset is null),
                ObjectCount = data.Objects.Count,
                RelationCount = data.Relations.Count,
                AddressCount = data.Addresses.Count,
                RelationTypes = data.Relations
                    .GroupBy(r => $"{r.LeftType}->{r.RightType}")
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count())
            };
            return data;
        }
        finally
        {
            FileUtil.TryDelete(temp);
            FileUtil.TryDeleteDirectory(Path.GetDirectoryName(temp)!);
        }
    }
}
