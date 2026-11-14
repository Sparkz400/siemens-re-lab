using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace TiaProject;

public sealed class HmiRenderScreen
{
    public string Name { get; set; } = "";
    public long InstanceId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Background { get; set; } = "#FFFFFF";
    public int ItemCount { get; set; }
    public string SvgFile { get; set; } = "";
}

public sealed class HmiRenderResult
{
    public string ExportPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public List<HmiRenderScreen> Screens { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public static class HmiSvgRenderer
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    public static HmiRenderResult Render(ObjectExport export, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var result = new HmiRenderResult { ExportPath = export.FilePath, OutPath = Path.GetFullPath(outDir) };
        var screens = export.Objects.Where(o => o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal)).ToList();
        var items = export.Objects.Where(o => o.ClassName.EndsWith("HmiScreenItemData", StringComparison.Ordinal)).ToList();
        var gallery = new StringBuilder();
        gallery.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>HMI screens</title>");
        gallery.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#1b1b1b;color:#eee;margin:24px}h2{font-weight:400}img{background:#fff;border:1px solid #444;max-width:100%}</style></head><body><h1>HMI screens</h1>");

        foreach (var screen in screens)
        {
            int width = ParseInt(Attr(screen, "Width"), 1024);
            int height = ParseInt(Attr(screen, "Height"), 768);
            var bg = Color(Attr(screen, "BackColor"), "#FFFFFF");
            var screenItems = items.Where(i => i.Links.Any(l => l.TypeId == 13307909 && l.InstanceId == screen.InstanceId)).ToList();
            var svg = new XElement(Svg + "svg",
                new XAttribute("width", width),
                new XAttribute("height", height),
                new XAttribute("viewBox", $"0 0 {width} {height}"),
                new XElement(Svg + "rect", new XAttribute("x", 0), new XAttribute("y", 0),
                    new XAttribute("width", width), new XAttribute("height", height),
                    new XAttribute("fill", bg)),
                new XElement(Svg + "text", new XAttribute("x", 12), new XAttribute("y", 24),
                    new XAttribute("font-family", "Segoe UI, Arial"), new XAttribute("font-size", 16),
                    new XAttribute("fill", "#222"), screen.Name ?? ""));

            foreach (var item in screenItems.OrderBy(i => ParseInt(Attr(i, "Layer"), 0)))
            {
                int x = ParseInt(Attr(item, "Left"), 0);
                int y = ParseInt(Attr(item, "Top"), 0);
                int w = ParseInt(Attr(item, "Width"), 80);
                int h = ParseInt(Attr(item, "Height"), 24);
                var subtype = item.Subtype ?? "Object";
                var name = item.Name ?? subtype;
                var fill = FillFor(subtype);
                var stroke = "#3A6EA5";
                XElement shape;
                if (subtype.Contains("Circle", StringComparison.OrdinalIgnoreCase) || subtype.Contains("Ellipse", StringComparison.OrdinalIgnoreCase))
                {
                    shape = new XElement(Svg + "ellipse",
                        new XAttribute("cx", x + w / 2), new XAttribute("cy", y + h / 2),
                        new XAttribute("rx", Math.Max(1, w / 2)), new XAttribute("ry", Math.Max(1, h / 2)),
                        new XAttribute("fill", fill), new XAttribute("stroke", stroke));
                }
                else if (subtype.Contains("Line", StringComparison.OrdinalIgnoreCase))
                {
                    shape = new XElement(Svg + "line",
                        new XAttribute("x1", x), new XAttribute("y1", y),
                        new XAttribute("x2", x + w), new XAttribute("y2", y + h),
                        new XAttribute("stroke", stroke), new XAttribute("stroke-width", 2));
                }
                else if (subtype.Contains("TubeArc", StringComparison.OrdinalIgnoreCase))
                {
                    shape = new XElement(Svg + "path",
                        new XAttribute("d", $"M {x} {y + h} A {w / 2} {h / 2} 0 0 1 {x + w} {y + h}"),
                        new XAttribute("fill", "none"), new XAttribute("stroke", "#B05A2A"),
                        new XAttribute("stroke-width", 6));
                }
                else
                {
                    shape = new XElement(Svg + "rect",
                        new XAttribute("x", x), new XAttribute("y", y),
                        new XAttribute("width", w), new XAttribute("height", h),
                        new XAttribute("rx", subtype.Contains("Button", StringComparison.OrdinalIgnoreCase) ? 6 : 2),
                        new XAttribute("fill", fill), new XAttribute("stroke", stroke),
                        new XAttribute("stroke-dasharray", subtype.Contains("Graphic", StringComparison.OrdinalIgnoreCase) ? "4 3" : "0"));
                }
                svg.Add(shape);
                svg.Add(new XElement(Svg + "text",
                    new XAttribute("x", x + 4), new XAttribute("y", y + Math.Min(h - 4, 16)),
                    new XAttribute("font-family", "Segoe UI, Arial"),
                    new XAttribute("font-size", 11), new XAttribute("fill", "#222"),
                    name));
            }

            var svgFile = Path.Combine(outDir, SafeFileName(screen.Name ?? $"Screen_{screen.InstanceId}") + ".svg");
            new XDocument(new XDeclaration("1.0", "utf-8", null), svg).Save(svgFile);
            result.Screens.Add(new HmiRenderScreen
            {
                Name = screen.Name ?? $"Screen_{screen.InstanceId}",
                InstanceId = screen.InstanceId,
                Width = width,
                Height = height,
                Background = bg,
                ItemCount = screenItems.Count,
                SvgFile = svgFile
            });
            gallery.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(screen.Name)} <small>{width}×{height}, {screenItems.Count} items</small></h2>");
            gallery.AppendLine($"<img src=\"{System.Net.WebUtility.HtmlEncode(Path.GetFileName(svgFile))}\" />");
        }
        gallery.AppendLine("</body></html>");
        File.WriteAllText(Path.Combine(outDir, "screens.html"), gallery.ToString(), new UTF8Encoding(false));
        return result;
    }

    private static string? Attr(ExportedObjectInfo o, string suffix)
    {
        foreach (var kv in o.Attributes)
            if (kv.Key.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static string Color(string? value, string fallback)
    {
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return fallback;
        uint argb = unchecked((uint)v);
        byte r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string FillFor(string subtype)
    {
        if (subtype.Contains("Button", StringComparison.OrdinalIgnoreCase)) return "#DDE7F3";
        if (subtype.Contains("TextField", StringComparison.OrdinalIgnoreCase) || subtype.Contains("IOField", StringComparison.OrdinalIgnoreCase)) return "#F7F7F7";
        if (subtype.Contains("Alarm", StringComparison.OrdinalIgnoreCase)) return "#F3E1E1";
        if (subtype.Contains("Trend", StringComparison.OrdinalIgnoreCase)) return "#E1F0E1";
        return "#EEF2F7";
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}
