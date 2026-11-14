using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaProject;

public sealed class PlfHeader
{
    public int FileType { get; set; }
    public string FileTypeName { get; set; } = "";
    public int ObsoleteStoreId { get; set; }
    public Guid FileId { get; set; }
    public string ProductVersion { get; set; } = "";
    public string FileFormatVersion { get; set; } = "";
    public byte Node { get; set; }
    public string HeaderHash { get; set; } = "";
    public bool HeaderHashValid { get; set; }
    public bool TerminatorValid { get; set; }
    public int HeaderSize { get; set; } = 98;
}

public sealed class PayloadString
{
    public int Offset { get; set; }
    public string Text { get; set; } = "";
}

public sealed class PayloadXml
{
    public int Offset { get; set; }
    public string Root { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class PlfObject
{
    public int RecordIndex { get; set; }
    public long Offset { get; set; }
    public int Size { get; set; }
    public int TypeId { get; set; }
    public string TypeHex => $"0x{TypeId:X}";
    public long InstanceId { get; set; }
    public int ClusterId { get; set; }
    public uint InternalObjectInfo { get; set; }
    public short States { get; set; }
    public byte OffsetCount { get; set; }
    public byte StorageMetaId { get; set; }
    public Guid Guid { get; set; }
    public string Hash { get; set; } = "";
    public bool HashValid { get; set; }
    public int PayloadLength => Size - 44;

    public bool IsDeleted => (States & 4) != 0;
    public bool IsDestroyed => (States & 8) != 0;
    public bool IsTRef => (States & 0x20) != 0;
    public bool IsSegmentHolder => (InternalObjectInfo & 0x4) != 0;
    public bool SupportsSegmentInfo => (InternalObjectInfo & 0x2) != 0;
    public bool HasLowFilledRelations => (InternalObjectInfo & 0x1) != 0;

    public bool Live { get; set; }
    public byte IndexFlags { get; set; }
    public string? ClassName { get; set; }
    public string? Name { get; set; }
    public string? Subtype { get; set; }
    public int? Number { get; set; }
    public int? BlockType { get; set; }
    public int? ProgrammingLanguage { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
    public List<PayloadString>? Strings { get; set; }
    public List<PayloadXml>? Xml { get; set; }

    public string ObjectId => $"{TypeId}/{InstanceId}";
}

public sealed class PlfIndexEntry
{
    public int Index { get; set; }
    public int TypeId { get; set; }
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public byte Node { get; set; }
    public int Position { get; set; }
    public int Cluster { get; set; }
    public byte Flags { get; set; }
    public bool IsDeleted => (Flags & 4) != 0;
    public bool IsDestroyed => (Flags & 8) != 0;
    public bool IsTRef => (Flags & 0x20) != 0;
    public bool IsSegmentHolder => (Flags & 0x40) != 0;
    public bool IsCoreTextHolder => (Flags & 0x80) != 0;
    public string ObjectId => $"{TypeId}/{InstanceId}";
}

public sealed class PlfCommitPoint
{
    public int Index { get; set; }
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public byte Node { get; set; }
    public int Position { get; set; }
    public int Cluster { get; set; }
    public int Length { get; set; }
}

public sealed class PlfIndex
{
    public PlfHeader Header { get; set; } = new();
    public List<PlfIndexEntry> Entries { get; set; } = [];
    public List<PlfCommitPoint> CommitPoints { get; set; } = [];
    public long FileLength { get; set; }
    public long TrailingBytes { get; set; }
}

public sealed class PlfFile
{
    public PlfHeader Header { get; set; } = new();
    public long FileLength { get; set; }
    public List<PlfObject> Objects { get; set; } = [];
    public long TrailingBytes { get; set; }
    public int? InvalidRecordOffset { get; set; }
    public PlfIndex? Index { get; set; }
    public List<PlfObject> LiveObjects { get; set; } = [];
    public List<PlfIndexEntry> MissingIndexEntries { get; set; } = [];
    public int IndexGuidMismatches { get; set; }
    public int HashFailures => Objects.Count(o => !o.HashValid);
    public Dictionary<string, string> TypeNames { get; set; } = [];
    public Dictionary<string, int> TypeCounts { get; set; } = [];
    public int LiveCount => LiveObjects.Count;
    public int HistoryCount => Objects.Count - LiveObjects.Count;

    public Dictionary<string, PlfObject> ByObjectId()
    {
        var d = new Dictionary<string, PlfObject>(StringComparer.Ordinal);
        foreach (var o in Objects) d[o.ObjectId] = o;
        return d;
    }

    public PlfObject? FindLive(int typeId, long instanceId) =>
        LiveObjects.FirstOrDefault(o => o.TypeId == typeId && o.InstanceId == instanceId)
        ?? Objects.LastOrDefault(o => o.TypeId == typeId && o.InstanceId == instanceId);

    public void EnrichWith(IReadOnlyDictionary<Guid, ExportedObjectInfo> byGuid)
    {
        var names = new Dictionary<int, Dictionary<string, int>>();
        foreach (var o in Objects)
        {
            if (!byGuid.TryGetValue(o.Guid, out var e)) continue;
            o.ClassName = e.ClassName;
            o.Name = e.Name;
            o.Subtype = e.Subtype;
            o.Number = e.Number;
            o.BlockType = e.BlockType;
            o.ProgrammingLanguage = e.ProgrammingLanguage;
            if (e.Attributes.Count > 0) o.Attributes = e.Attributes;

            if (!names.TryGetValue(o.TypeId, out var counts))
                names[o.TypeId] = counts = new Dictionary<string, int>(StringComparer.Ordinal);
            counts[e.ClassName] = counts.GetValueOrDefault(e.ClassName) + 1;
        }
        TypeNames = names.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First().Key);
        TypeCounts = Objects.GroupBy(o => o.TypeId.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
    }
}

public static class PlfReader
{
    public const int HeaderSize = 98;
    public const int ObjectHeaderSize = 44;
    public const int HashSize = 32;
    public const int CommitPointTypeId = 458772; // StoreObjectId.SystemObjectTypeMinValue + 20

    private static readonly Dictionary<int, string> SystemTypeNames = new()
    {
        [458752] = "System.SystemMeta",
        [458753] = "System.PackageConstellationMap",
        [458754] = "System.ExpandoMappingTable",
        [458755] = "System.InstanceAlloctionTable",
        [458756] = "System.ClusterAllocationTable",
        [458757] = "System.ClusterMappingTable",
        [458758] = "System.ClusterCollection",
        [458759] = "System.StorageInformation",
        [458760] = "System.PropertySetMappingTable",
        [458761] = "System.RootObjectCollection",
        [458762] = "System.StorageVersion",
        [458763] = "System.FeatureConfiguration",
        [458764] = "System.ProjectAnnotationTable",
        [458765] = "System.StorageStatistics",
        [458766] = "System.LocalizatonTable",
        [458767] = "System.ProjectAnnotation",
        [458768] = "System.PackageConstellation",
        [458769] = "System.PropertySet",
        [458770] = "System.Expando",
        [458771] = "System.Segment",
        [458772] = "System.CommitPoint",
        [458773] = "System.ClosePoint",
        [458774] = "System.TrustAnchor",
        [458775] = "System.SystemMetaList"
    };

    public static PlfFile Read(string path, bool verifyHashes = true)
    {
        var bytes = FileUtil.ReadAllBytesShared(path);
        var file = new PlfFile { FileLength = bytes.Length, Header = ReadHeader(bytes) };

        int offset = HeaderSize;
        int recordIndex = 0;
        while (offset + ObjectHeaderSize <= bytes.Length)
        {
            int size = BitConverter.ToInt32(bytes, offset);
            if (size < ObjectHeaderSize || offset + size + HashSize > bytes.Length)
            {
                file.InvalidRecordOffset = offset;
                break;
            }

            var obj = new PlfObject
            {
                RecordIndex = recordIndex++,
                Offset = offset,
                Size = size,
                TypeId = BitConverter.ToInt32(bytes, offset + 4),
                InstanceId = BitConverter.ToInt64(bytes, offset + 8),
                ClusterId = BitConverter.ToInt32(bytes, offset + 16),
                InternalObjectInfo = BitConverter.ToUInt32(bytes, offset + 20),
                States = BitConverter.ToInt16(bytes, offset + 24),
                OffsetCount = bytes[offset + 26],
                StorageMetaId = bytes[offset + 27],
                Guid = new Guid(bytes.AsSpan(offset + 28, 16))
            };

            var storedHash = bytes.AsSpan(offset + size, HashSize).ToArray();
            obj.Hash = Convert.ToHexString(storedHash).ToLowerInvariant();
            if (verifyHashes)
            {
                var computed = SHA256.HashData(bytes.AsSpan(offset, size));
                obj.HashValid = computed.AsSpan().SequenceEqual(storedHash);
            }

            file.Objects.Add(obj);
            offset += size + HashSize;
        }

        file.TrailingBytes = bytes.Length - offset;
        return file;
    }

    public static PlfIndex ReadIndex(string path)
    {
        var bytes = FileUtil.ReadAllBytesShared(path);
        var index = new PlfIndex { FileLength = bytes.Length, Header = ReadHeader(bytes) };
        int offset = HeaderSize;
        int entryIndex = 0;
        while (offset + 38 <= bytes.Length)
        {
            int typeId = BitConverter.ToInt32(bytes, offset);
            long instanceId = BitConverter.ToInt64(bytes, offset + 4);
            var guid = new Guid(bytes.AsSpan(offset + 12, 16));
            byte node = bytes[offset + 28];
            int position = BitConverter.ToInt32(bytes, offset + 29);
            int cluster = BitConverter.ToInt32(bytes, offset + 33);
            byte flags = bytes[offset + 37];
            offset += 38;

            if (typeId == CommitPointTypeId)
            {
                int length = BitConverter.ToInt32(bytes, offset);
                index.CommitPoints.Add(new PlfCommitPoint
                {
                    Index = entryIndex++,
                    InstanceId = instanceId,
                    Guid = guid,
                    Node = node,
                    Position = position,
                    Cluster = cluster,
                    Length = length
                });
                if (length < 4 || offset + length > bytes.Length)
                {
                    index.TrailingBytes = bytes.Length - offset;
                    break;
                }
                offset += length;
                continue;
            }

            index.Entries.Add(new PlfIndexEntry
            {
                Index = entryIndex++,
                TypeId = typeId,
                InstanceId = instanceId,
                Guid = guid,
                Node = node,
                Position = position,
                Cluster = cluster,
                Flags = flags
            });
        }

        index.TrailingBytes = bytes.Length - offset;
        return index;
    }

    public static PlfHeader ReadHeader(byte[] bytes)
    {
        if (bytes.Length < HeaderSize) throw new CliException("file is smaller than the 98-byte FileHeader");
        var header = new PlfHeader
        {
            FileType = BitConverter.ToInt32(bytes, 0),
            ObsoleteStoreId = BitConverter.ToInt32(bytes, 4),
            FileId = new Guid(bytes.AsSpan(8, 16)),
            ProductVersion = ReadVersion(bytes, 24),
            FileFormatVersion = ReadVersion(bytes, 40),
            Node = bytes[56],
            HeaderHash = Convert.ToHexString(bytes.AsSpan(65, 32)).ToLowerInvariant(),
            TerminatorValid = bytes[97] == 0xFF
        };
        header.FileTypeName = header.FileType switch
        {
            0 => "None",
            1 => "Storage",
            2 => "Index",
            4 => "Session",
            8 => "Linked",
            0x10 => "Unsupported",
            0x20 => "Trace",
            0x40 => "Composite",
            0x80 => "CompositeIndex",
            _ => $"0x{header.FileType:X}"
        };
        var computed = SHA256.HashData(bytes.AsSpan(0, 65));
        header.HeaderHashValid = computed.AsSpan().SequenceEqual(bytes.AsSpan(65, 32));
        return header;
    }

    private static string ReadVersion(byte[] bytes, int offset) =>
        $"{BitConverter.ToInt32(bytes, offset)}.{BitConverter.ToInt32(bytes, offset + 4)}.{BitConverter.ToInt32(bytes, offset + 8)}.{BitConverter.ToInt32(bytes, offset + 12)}";

    public static string SystemTypeName(int typeId) =>
        SystemTypeNames.TryGetValue(typeId, out var name) ? name : $"0x{typeId:X}";

    public static void AttachIndex(PlfFile plf, PlfIndex index)
    {
        plf.Index = index;
        var byPosition = plf.Objects.ToDictionary(o => (int)o.Offset);
        var live = new Dictionary<string, PlfIndexEntry>(StringComparer.Ordinal);
        foreach (var entry in index.Entries)
        {
            // Last entry for an object id wins; deleted/destroyed entries have position -1.
            if (!entry.IsDeleted && !entry.IsDestroyed && entry.Position >= 0)
                live[entry.ObjectId] = entry;
            else
                live.Remove(entry.ObjectId);
        }

        foreach (var entry in live.Values)
        {
            if (!byPosition.TryGetValue(entry.Position, out var obj))
            {
                plf.MissingIndexEntries.Add(entry);
                continue;
            }
            obj.Live = true;
            obj.IndexFlags = entry.Flags;
            if (obj.Guid != entry.Guid) plf.IndexGuidMismatches++;
        }

        plf.LiveObjects = plf.Objects.Where(o => o.Live).OrderBy(o => o.Offset).ToList();
    }
}

public static partial class PayloadInspector
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_ .\-]{1,60}$", RegexOptions.Compiled)]
    private static partial Regex NameCandidate();

    private static readonly HashSet<string> NameStopWords = new(StringComparer.Ordinal)
    {
        "System.String", "System.Int32", "System.Boolean", "System.Byte", "System.Object",
        "NamingConvention", "AllowedCharacterSets", "RegularExpression", "MaxLength", "MinLength",
        "IsShell", "IsKnowHowProtected", "CreationTime", "ModifiedTime", "Protection", "Subtype",
        "Visible", "Comment", "Culture", "Password", "Version", "Name", "TargetFamily",
        "CompilerSettings", "ProgrammingLanguage", "BlockLanguage", "BlockType", "Number"
    };

    public static List<PayloadString> ExtractStrings(byte[] data, int max = 250)
    {
        var results = new List<PayloadString>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < data.Length - 2 && results.Count < max; i++)
        {
            int len = data[i];
            if (len is < 3 or > 120 || i + 1 + len > data.Length) continue;
            if (!IsPrintable(data, i + 1, len)) continue;
            var text = Encoding.ASCII.GetString(data, i + 1, len).Trim();
            if (text.Length < 3 || !seen.Add(text)) continue;
            results.Add(new PayloadString { Offset = i, Text = text });
            i += len;
        }
        return results;
    }

    public static string? GuessName(IEnumerable<PayloadString> strings)
    {
        var candidates = strings
            .Select(s => s.Text.Trim())
            .Where(s => NameCandidate().IsMatch(s))
            .Where(s => !NameStopWords.Contains(s))
            .Where(s => !s.StartsWith("Siemens.", StringComparison.Ordinal))
            .Where(s => !s.StartsWith("System.", StringComparison.Ordinal))
            .Where(s => !s.Contains("NamingConvention", StringComparison.Ordinal))
            .Where(s => !s.Contains("AllowedCharacter", StringComparison.Ordinal))
            .ToList();

        return candidates.FirstOrDefault(s => !s.Contains('.') && !s.Contains(' '))
            ?? candidates.FirstOrDefault();
    }

    public static List<PayloadXml> ExtractXml(byte[] data, int max = 10)
    {
        var results = new List<PayloadXml>();
        var text = Encoding.UTF8.GetString(data);
        int search = 0;
        while (results.Count < max)
        {
            int start = text.IndexOf("<?xml", search, StringComparison.Ordinal);
            if (start < 0) break;
            int declEnd = text.IndexOf("?>", start, StringComparison.Ordinal);
            if (declEnd < 0) break;
            int rootStart = text.IndexOf('<', declEnd + 2);
            if (rootStart < 0) break;
            int nameEnd = rootStart + 1;
            while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] is '_' or '-' or '.' or ':')) nameEnd++;
            var root = text[(rootStart + 1)..nameEnd];
            if (root.Length == 0) { search = declEnd + 2; continue; }
            int close = text.IndexOf($"</{root}>", nameEnd, StringComparison.Ordinal);
            if (close < 0) { search = declEnd + 2; continue; }
            int end = close + root.Length + 3;
            var xml = text[start..end];
            try
            {
                System.Xml.Linq.XDocument.Parse(xml);
                results.Add(new PayloadXml { Offset = start, Root = root, Text = xml });
                search = end;
            }
            catch
            {
                search = declEnd + 2;
            }
        }
        return results;
    }

    private static bool IsPrintable(byte[] data, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte b = data[offset + i];
            if (b is < 0x20 or >= 0x7F) return false;
        }
        return true;
    }
}
