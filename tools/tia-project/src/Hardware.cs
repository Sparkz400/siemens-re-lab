using System.Text;

namespace TiaProject;

public sealed class HardwareObject
{
    public string ClassName { get; set; } = "";
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public string? Name { get; set; }
    public string? Subtype { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = [];
    public List<string> Links { get; set; } = [];
}

public sealed class HardwareDevice
{
    public string Name { get; set; } = "";
    public string? Subtype { get; set; }
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public HardwareObject Device { get; set; } = new();
    public List<HardwareObject> Items { get; set; } = [];
    public List<HardwareObject> Nodes { get; set; } = [];
    public List<HardwareObject> AddressManagers { get; set; } = [];
}

public sealed class HardwareResult
{
    public string ExportPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public List<HardwareDevice> Devices { get; set; } = [];
    public List<HardwareObject> Nodes { get; set; } = [];
    public List<HardwareObject> AddressManagers { get; set; } = [];
    public List<HardwareObject> Other { get; set; } = [];
    public string? DotFile { get; set; }
    public string? MarkdownFile { get; set; }
}

public static class HardwareExporter
{
    private static readonly string[] DeviceClasses =
    [
        "Siemens.Simatic.HwConfiguration.Model.DeviceData",
        "Siemens.Simatic.HwConfiguration.Hmi.Model.HmiApplicationData",
        "Siemens.Simatic.Hmi.Utah.Device.HmiDeviceData"
    ];

    private static readonly string[] ItemClasses =
    [
        "Siemens.Simatic.HwConfiguration.Model.DeviceItemData"
    ];

    private static readonly string[] NodeClasses =
    [
        "Siemens.Simatic.HwConfiguration.Model.NodeData"
    ];

    private static readonly string[] AddressClasses =
    [
        "Siemens.Simatic.HwConfiguration.Model.AddressManagerNGData",
        "Siemens.Simatic.HwConfiguration.Model.AddressManagerV2Data"
    ];

    public static HardwareResult Export(ObjectExport export, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var result = new HardwareResult { ExportPath = export.FilePath, OutPath = Path.GetFullPath(outDir) };
        var devices = export.Objects.Where(o => DeviceClasses.Any(c => o.ClassName.EndsWith(c, StringComparison.Ordinal))).ToList();
        var items = export.Objects.Where(o => ItemClasses.Any(c => o.ClassName.EndsWith(c, StringComparison.Ordinal))).ToList();
        var nodes = export.Objects.Where(o => NodeClasses.Any(c => o.ClassName.EndsWith(c, StringComparison.Ordinal))).ToList();
        var addrs = export.Objects.Where(o => AddressClasses.Any(c => o.ClassName.EndsWith(c, StringComparison.Ordinal))).ToList();

        foreach (var device in devices)
        {
            var hd = new HardwareDevice
            {
                Name = device.Name ?? device.ClassName,
                Subtype = device.Subtype,
                InstanceId = device.InstanceId,
                Guid = device.Guid,
                Device = ToObject(device)
            };
            var controllerIds = device.Links.Where(l => l.TypeId == 1052700).Select(l => l.InstanceId).ToHashSet();
            hd.Items = items.Where(i => i.Links.Any(l =>
                    (l.TypeId == 1052686 && l.InstanceId == device.InstanceId) ||
                    (l.TypeId == 1052689 && l.InstanceId == device.InstanceId)))
                .Select(ToObject).ToList();
            hd.Nodes = nodes.Where(n => n.Links.Any(l =>
                    (l.TypeId == 1052700 && controllerIds.Contains(l.InstanceId)) ||
                    (l.TypeId == 1052686 && l.InstanceId == device.InstanceId)))
                .Select(ToObject).ToList();
            hd.AddressManagers = addrs.Where(a => a.Links.Any(l =>
                    controllerIds.Contains(l.InstanceId) || l.InstanceId == device.InstanceId))
                .Select(ToObject).ToList();
            result.Devices.Add(hd);
        }
        result.Nodes = nodes.Select(ToObject).ToList();
        result.AddressManagers = addrs.Select(ToObject).ToList();
        result.Other = export.Objects
            .Where(o => o.ClassName.Contains("LoadableHWConfiguration", StringComparison.Ordinal)
                        || o.ClassName.Contains("S7ControllerTarget", StringComparison.Ordinal))
            .Select(ToObject).ToList();

        result.DotFile = Path.Combine(outDir, "hardware.dot");
        File.WriteAllText(result.DotFile, BuildDot(result), new UTF8Encoding(false));
        result.MarkdownFile = Path.Combine(outDir, "hardware.md");
        File.WriteAllText(result.MarkdownFile, BuildMarkdown(result), new UTF8Encoding(false));
        return result;
    }

    private static HardwareObject ToObject(ExportedObjectInfo o) => new()
    {
        ClassName = o.ClassName,
        InstanceId = o.InstanceId,
        Guid = o.Guid,
        Name = o.Name,
        Subtype = o.Subtype,
        Attributes = o.Attributes,
        Links = o.Links.Select(l => $"{l.TypeId}-{l.InstanceId}").ToList()
    };

    private static string BuildDot(HardwareResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph Hardware {");
        sb.AppendLine("  rankdir=LR; node [shape=box, fontname=\"Consolas\"];");
        foreach (var d in result.Devices)
        {
            sb.AppendLine($"  d{d.InstanceId} [label=\"{Escape(d.Name)}\\n{Escape(d.Subtype ?? "")}\", style=filled, fillcolor=\"#DDE7F3\"];");
            foreach (var i in d.Items)
                sb.AppendLine($"  i{i.InstanceId} [label=\"{Escape(i.Name ?? "")}\\n{Escape(i.Subtype ?? "")}\"]; d{d.InstanceId} -> i{i.InstanceId};");
            foreach (var n in d.Nodes)
                sb.AppendLine($"  n{n.InstanceId} [label=\"{Escape(n.Name ?? "")}\\nnode\", shape=ellipse]; d{d.InstanceId} -> n{n.InstanceId};");
            foreach (var a in d.AddressManagers)
                sb.AppendLine($"  a{a.InstanceId} [label=\"{Escape(a.Name ?? "")}\\naddress\", shape=note]; d{d.InstanceId} -> a{a.InstanceId};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildMarkdown(HardwareResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Hardware inventory");
        sb.AppendLine();
        foreach (var d in result.Devices)
        {
            sb.AppendLine($"## {d.Name} ({d.Subtype})");
            sb.AppendLine();
            sb.AppendLine("| Item | Type | Order number | Position |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var i in d.Items)
            {
                var order = Attr(i, "OrderNumber");
                var pos = Attr(i, "PositionNumber");
                sb.AppendLine($"| {i.Name} | {i.Subtype} | {order} | {pos} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string? Attr(HardwareObject o, string name)
    {
        foreach (var kv in o.Attributes)
            if (kv.Key.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
