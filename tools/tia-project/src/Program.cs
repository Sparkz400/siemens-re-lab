using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaProject;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Cli.Run(args);
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex);
            return 1;
        }
    }
}

internal sealed class Args
{
    public List<string> Positional { get; } = [];
    public Dictionary<string, string?> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Args(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                var key = a[2..];
                string? value = null;
                var eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    value = key[(eq + 1)..];
                    key = key[..eq];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                Options[key] = value ?? "true";
            }
            else
            {
                Positional.Add(a);
            }
        }
    }

    public bool Has(string key) => Options.ContainsKey(key);
    public string? Get(string key) => Options.TryGetValue(key, out var v) ? v : null;
    public string Require(string key) => Get(key) ?? throw new CliException($"missing --{key}");
    public int GetInt(string key, int fallback) => int.TryParse(Get(key), out var v) ? v : fallback;
}

public static class Cli
{
    public static int Run(string[] argv)
    {
        var args = new Args(argv);
        if (args.Positional.Count == 0 || args.Has("help") || args.Has("h"))
        {
            PrintHelp();
            return 0;
        }

        var cmd = args.Positional[0].ToLowerInvariant();
        return cmd switch
        {
            "info" => Info(args),
            "plf" => Plf(args),
            "xref" => Xref(args),
            "vci" => Vci(args),
            "opns" => Opns(args),
            "mc7" => Mc7(args),
            "hardware" => Hardware(args),
            "history" => History(args),
            "forget" => Forget(args),
            "safety" => Domain(args, "safety"),
            "umac" => Domain(args, "umac"),
            "drives" => Domain(args, "drives"),
            "platform" => Platform(args),
            "deploy" => Deploy(args),
            "warehouse" => Warehouse(args),
            "customs" => Customs(args),
            "queue" => Queue(args),
            "compiler" => Compiler(args),
            "evolve" => Evolve(args),
            "thymus" => ThymusCmd(args),
            "replay" => Replay(args),
            "contracts" => Contracts(args),
            "midden" => MiddenCmd(args),
            "fixture" => Fixture(args),
            "drift" => Drift(args),
            "corpus" => Corpus(args),
            "fleet" => Fleet(args),
            "certify" => Certify(args),
            "registry" => Registry(args),
            "market" => Market(args),
            "ontology" => Ontology(args),
            "api" => Api(args),
            "ir" => Ir(args),
            "ci" => Ci(args),
            "gate" => Gate(args),
            "merge" => Merge(args),
            "forensics" => ForensicsCmd(args),
            "flightrec" => FlightRec(args),
            "prune" => Prune(args),
            "hmi" => Hmi(args),
            "all" => All(args),
            _ => throw new CliException($"unknown command '{cmd}'. Try --help.")
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            tia-project - file-level TIA Portal project toolkit

            usage:
              tia-project info   <project> [--json <file>]
              tia-project plf    <project> [--json <file>] [--fs2xml <plain.xml>] [--history] [--attrs] [--strings] [--xml <dir>] [--no-verify]
              tia-project xref   <project> [--json <file>] [--dot <file>] [--impact <part:id|obj:id|type/instance>] [--fs2xml <plain.xml>]
              tia-project vci    <project> [--json <file>] [--max-rows <n>]

              tia-project opns schemas --bin <tia-bin> --out <dir>
              tia-project opns api     --bin <tia-bin> --out <file>
              tia-project opns fs2xml  <project> --out <file> [--bin <tia-bin>] [--keep-encrypted]
              tia-project opns blocks  <plain.xml> --out <dir>
              tia-project opns simaticml <plain.xml> --out <dir>
              tia-project mc7 disasm  <plain.xml> --out <dir> [--bin <tia-bin>]
              tia-project hardware export <plain.xml> --out <dir>
              tia-project history <project> --out <dir> [--fs2xml <plain.xml>] [--extract]
              tia-project forget --project <dir> --fs2xml <xml> --out <new-project-dir> [--keep-last <n>] [--validated <json>] [--full] [--json <report>]
              tia-project safety export <plain.xml> --out <dir>
              tia-project umac export   <plain.xml> --out <dir>
              tia-project drives export <plain.xml> --out <dir>
              tia-project vci verify <project> --json <file>

              tia-project platform run --project <dir> --fs2xml <xml> --out <dir> [--key <path-base>] [--bin <tia-bin>] [--release-project <dir> --release-export <xml>] [--compiler-export <xml>]
              tia-project platform realpair --project <dir> --fs2xml <xml> --release-project <dir> --release-export <xml> --out <dir> [--bin <tia-bin>] [--fleet-spec <json>] [--compiler-export <xml>]

              tia-project deploy rollback --project <dir> --fs2xml <xml> --out <dir> [--key <private.pem>]
              tia-project deploy pod --artifact <file> --cpu-serial <sn> --rack-slot <slot> --key <private.pem> --out <json>
              tia-project deploy verify-pod --pod <json> --public <public.pem>
              tia-project deploy consolidate --changes <json> --out <json>
              tia-project warehouse add --file <file> --id <id> --out <dir>
              tia-project warehouse release --dir <dir> --id <id> --tests <csv> --umac-review <json> [--public <public.pem>]
              tia-project warehouse list --dir <dir> --out <json>
              tia-project warehouse review --artifact <file> --reviewed-by <name> --decision approved --out <json> [--key <private.pem>]
              tia-project warehouse umac --artifact <file> --out <json>
              tia-project warehouse test --artifact <file> --out <dir> [--key <path-base>]
              tia-project customs classify --fs2xml <xml> --block <name> --out <json>
              tia-project queue age --items <json> --rate <number> --out <json>

              tia-project compiler probe --fs2xml <xml> --out <json> [--bin <tia-bin>]
              tia-project compiler stats --probe <json> --out <json>
              tia-project compiler model --probe <json> --out <json>
              tia-project compiler predict --probe <json> --block <name> --out <json>
              tia-project compiler differential --probe <json> --out <json>
              tia-project compiler test --fs2xml <xml> --out <dir> [--bin <tia-bin>]
              tia-project compiler classic-test --fs2xml <xml> --out <dir> [--bin <tia-bin>]
              tia-project evolve trace --fs2xml <xml> --block <name> --out <json> [--samples <n>]
              tia-project evolve run --fs2xml <xml> --block <name> --trace <trace.json> --out <json> [--count <n>]
              tia-project evolve recorded --fs2xml <xml> --block <name> --trace <flight-trace.json> --inputs <csv> --outputs <csv> --out <dir>
              tia-project evolve recorded-test --trace <flight-trace.json> --input <tag> --output <tag> --out <dir>
              tia-project evolve test --out <dir>
              tia-project thymus train --fs2xml <xml> --out <json>
              tia-project thymus scan --fs2xml <xml> --detectors <json> --out <json>
              tia-project replay consolidate --project <dir> --fs2xml <xml> --out <json>
              tia-project contracts discover --fs2xml <xml> --min-blocks <n> --out <json>
              tia-project midden analyze --fs2xml <xml> --out <json>

              tia-project drift snapshot --project <dir> --fs2xml <xml> --out <json>
              tia-project drift compare  --a <json> --b <json> --out <json>
              tia-project corpus build   --project <dir> --fs2xml <xml> --out <dir>
              tia-project corpus verify  --corpus <dir> --project <dir> --fs2xml <xml> --out <json>
              tia-project corpus mutate  --fs2xml <xml> --out <dir> [--count <n>]
              tia-project corpus multi   --spec <json> --out <dir>
              tia-project fixture create --project <dir> --fs2xml <xml> --out <dir> --name <name> --version <ver> [--no-tag]
              tia-project fleet record   --project <dir> --fs2xml <xml> --out <file>
              tia-project fleet report   --dir <dir> --out <json>

              tia-project certify equivalence --fs2xml <xml> --a <block> --b <block> --out <json> [--bin <tia-bin>]
              tia-project certify bisim       --fs2xml <xml> --a <block> --b <block> --out <json> [--cycles <n>] [--observe-bits <csv>] [--observe-bytes <csv>] [--init-bits <csv>] [--init-bytes <csv>] [--graph]
              tia-project certify bisim-test  --fs2xml <xml> --out <json> [--bin <tia-bin>]
              tia-project certify mc7-test    --fs2xml <xml> --out <json> [--bin <tia-bin>]
              tia-project certify symbolic    --fs2xml <xml> --a <block> --b <block> --out <json> [--bin <tia-bin>]
              tia-project certify symbolic-test --fs2xml <xml> --out <json> [--bin <tia-bin>]
              tia-project certify semantic    --fs2xml <xml> --a <block> --b <block> --out <json> [--bin <tia-bin>]
              tia-project certify semantic-test --fs2xml <xml> --out <json> [--bin <tia-bin>]
              tia-project registry init --key <path>
              tia-project registry sign --key <private.pem> --artifact <file> --out <json>
              tia-project registry verify --public <public.pem> --artifact <file> --signature <json>
              tia-project registry attest --fs2xml <xml> --key <private.pem> --out <dir>
              tia-project market publish --fs2xml <xml> --block <name> --key <private.pem> --out <dir> [--contract <json>] [--project <dir> --vci <vci.db>]
              tia-project market verify --package <json> --public <public.pem> [--project <dir> --vci <vci.db>] [--fs2xml <xml>]
              tia-project market test --project <dir> --fs2xml <xml> --out <dir> [--key <path-base>]

              tia-project ontology build --project <dir> --fs2xml <xml> --out <db>
              tia-project ontology query --db <db> --sql "SELECT ..."
              tia-project api call --db <db> --verb <verb> [--id <id>] [--name <name>] [--tag <tag>] [--sql <sql>]
              tia-project api benchmark --db <db> --out <json>
              tia-project ir export --fs2xml <xml> --out <json>
              tia-project ir verify --ir <json> --out <json>
              tia-project ir lower --ir <json> --backend json|dot|simaticml --out <path> [--fs2xml <xml>]
              tia-project ir roundtrip --fs2xml <xml> --out <dir> [--schemas <dir>] [--bin <tia-bin>]
              tia-project ir passes
              tia-project ci run --project <dir> --fs2xml <xml> --out <dir>
              tia-project ci linux --project <dir> --fs2xml <xml> --out <dir> [--project-file <csproj>]
              tia-project ci init --out <dir>

              tia-project gate compare --baseline <xml> --candidate <xml> --out <dir> [--baseline-project <dir> --candidate-project <dir>]
              tia-project gate bisect  --project <dir> --fs2xml <xml> --block <name> --out <file>
              tia-project merge --base <xml> --ours <xml> --theirs <xml> --out <xml> [--report <json>]
              tia-project forensics --project <dir> --fs2xml <xml> --out <dir>
              tia-project flightrec record --fs2xml <xml> --out <trace.json>
              tia-project flightrec replay --trace <trace.json> --rules <rules.json> --out <json>
              tia-project prune --project <dir> --fs2xml <xml> --keep-last <n> --out <dir>

              tia-project hmi export  <project> --out <dir> [--instance .\WINCC] [--keep] [--max-rows <n>]
              tia-project hmi objects <plain.xml> --out <dir>
              tia-project hmi render  <plain.xml> --out <dir>
              tia-project hmi il      --bin <tia-bin> --out <file>

              tia-project all <project> --out <dir> [--bin <tia-bin>]

            notes:
              <project> may be the project folder, the .ap21, or System\PEData.plf.
              PLF/IDX/XRef/Vci are read from copies when TIA holds them open.
              HMI export restores the project's CS_*.bak into MSSQL$WINCC as a temporary
              database, exports it, and drops the database unless --keep is given.
            """);
    }

    private static ProjectPaths Paths(Args args, int index = 1) =>
        ProjectPaths.Resolve(args.Positional.Count > index ? args.Positional[index] : throw new CliException("missing <project>"));

    private static int Info(Args args)
    {
        var paths = Paths(args);
        var ap21 = Ap21Info.Read(paths.Ap21);
        var info = ProjectInfo.Read(Path.Combine(paths.Root, "ProjectInfo.txt"));
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        var idx = PlfReader.ReadIndex(paths.Idx);
        PlfReader.AttachIndex(plf, idx);
        var xref = XrefReader.Read(paths.XrefDb, plf);
        var vci = VciReader.Read(paths.VciDb, maxRowsPerTable: 0);

        var report = new
        {
            project = new
            {
                root = paths.Root,
                ap21 = new { paths.Ap21, ap21.Name, ap21.Type, ap21.BrowsableType, ap21.ProjectCompatibilityVersion, ap21.ProjectVersion, ap21.FileLength, ap21.Sha256 },
                info,
                files = new
                {
                    plf = new { path = paths.Plf, length = new FileInfo(paths.Plf).Length },
                    idx = new { path = paths.Idx, length = new FileInfo(paths.Idx).Length },
                    xref = new { path = paths.XrefDb, length = new FileInfo(paths.XrefDb).Length },
                    vci = new { path = paths.VciDb, length = new FileInfo(paths.VciDb).Length },
                    hmiBackup = paths.HmiBackup is null ? null : new { path = paths.HmiBackup, length = new FileInfo(paths.HmiBackup).Length }
                }
            },
            plf = new
            {
                header = plf.Header,
                fileLength = plf.FileLength,
                records = plf.Objects.Count,
                live = plf.LiveObjects.Count,
                history = plf.HistoryCount,
                indexEntries = idx.Entries.Count,
                commitPoints = idx.CommitPoints.Count,
                typeNames = plf.TypeNames
            },
            xref = xref.Summary,
            vci = vci.RowCounts
        };

        if (args.Get("json") is { } json) JsonUtil.Write(report, json);
        else
        {
            Console.WriteLine($"project      {ap21.Name}  ({paths.Root})");
            Console.WriteLine($"ap21         {ap21.BrowsableType}  v{ap21.ProjectVersion}");
            Console.WriteLine($"TIA          min {info.MinimumVersion}  last {info.LastUsedVersion}");
            Console.WriteLine($"PLF          {plf.FileLength:N0} bytes, {plf.Objects.Count} records, {plf.LiveObjects.Count} live, {plf.HistoryCount} history");
            Console.WriteLine($"IDX          {idx.Entries.Count} entries, {idx.CommitPoints.Count} commit points");
            Console.WriteLine($"XRef         {xref.Summary.PartCount} parts, {xref.Summary.ObjectCount} objects, {xref.Summary.RelationCount} relations");
            Console.WriteLine($"VCI          {string.Join(", ", vci.RowCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
            if (paths.HmiBackup is not null) Console.WriteLine($"HMI backup   {paths.HmiBackup} ({new FileInfo(paths.HmiBackup).Length:N0} bytes)");
        }
        return 0;
    }

    private static int Plf(Args args)
    {
        var paths = Paths(args);
        var plf = PlfReader.Read(paths.Plf, verifyHashes: !args.Has("no-verify"));
        var idx = PlfReader.ReadIndex(paths.Idx);
        PlfReader.AttachIndex(plf, idx);

        ObjectExport? export = null;
        if (args.Get("fs2xml") is { } xmlPath)
        {
            export = ObjectExport.Read(xmlPath);
            plf.EnrichWith(export.ByGuid);
        }

        bool includeStrings = args.Has("strings");
        if (includeStrings || args.Get("xml") is not null)
        {
            var bytes = FileUtil.ReadAllBytesShared(paths.Plf);
            foreach (var o in plf.Objects)
            {
                var payload = bytes.AsSpan((int)o.Offset + 44, o.PayloadLength).ToArray();
                if (includeStrings)
                {
                    o.Strings = PayloadInspector.ExtractStrings(payload);
                    if (o.Name is null) o.Name = PayloadInspector.GuessName(o.Strings);
                }
                if (args.Get("xml") is { } xmlDir)
                    o.Xml = PayloadInspector.ExtractXml(payload);
            }
            if (args.Get("xml") is { } dir)
            {
                Directory.CreateDirectory(dir);
                foreach (var o in plf.Objects.Where(o => o.Xml is { Count: > 0 }))
                {
                    foreach (var x in o.Xml!)
                    {
                        var file = Path.Combine(dir, $"obj-{o.RecordIndex:D4}-{o.TypeId}-{o.InstanceId}-{x.Root}.xml");
                        File.WriteAllText(file, x.Text, new UTF8Encoding(false));
                    }
                }
            }
        }

        bool includeHistory = args.Has("history");
        bool includeAttrs = args.Has("attrs");
        var objects = (includeHistory ? plf.Objects : plf.LiveObjects)
            .Select(o => new
            {
                o.RecordIndex, o.Offset, o.Size, o.TypeId, o.TypeHex, o.InstanceId, o.ClusterId, o.Guid,
                o.Live, o.States, o.IndexFlags, o.ClassName, o.Name, o.Subtype, o.Number, o.BlockType, o.ProgrammingLanguage,
                o.HashValid, o.PayloadLength, o.IsDeleted, o.IsTRef, o.IsSegmentHolder,
                Attributes = includeAttrs ? o.Attributes : null,
                Strings = includeStrings ? o.Strings : null
            }).ToList();

        var report = new
        {
            header = plf.Header,
            index = new { header = idx.Header, entries = idx.Entries.Count, commitPoints = idx.CommitPoints.Count, trailingBytes = idx.TrailingBytes },
            fileLength = plf.FileLength,
            records = plf.Objects.Count,
            live = plf.LiveObjects.Count,
            history = plf.HistoryCount,
            hashFailures = plf.HashFailures,
            indexGuidMismatches = plf.IndexGuidMismatches,
            missingIndexEntries = plf.MissingIndexEntries.Count,
            trailingBytes = plf.TrailingBytes,
            invalidRecordOffset = plf.InvalidRecordOffset,
            typeNames = plf.TypeNames,
            typeCounts = plf.TypeCounts,
            objects
        };

        if (args.Get("json") is { } json) JsonUtil.Write(report, json);
        else
        {
            Console.WriteLine($"PLF {plf.FileLength:N0} bytes  {plf.Objects.Count} records  live={plf.LiveObjects.Count} history={plf.HistoryCount}");
            Console.WriteLine($"header hash valid={plf.Header.HeaderHashValid}  terminator={plf.Header.TerminatorValid}  trailing={plf.TrailingBytes}");
            Console.WriteLine($"hash failures={plf.HashFailures}  index GUID mismatches={plf.IndexGuidMismatches}  missing index entries={plf.MissingIndexEntries.Count}");
            var top = plf.Objects.GroupBy(o => o.TypeId).OrderByDescending(g => g.Count()).Take(15);
            foreach (var g in top)
            {
                var name = plf.TypeNames.GetValueOrDefault(g.Key.ToString()) ?? PlfReader.SystemTypeName(g.Key);
                Console.WriteLine($"  {g.Key,8} 0x{g.Key:X6}  {g.Count(),5}  {name}");
            }
        }
        return 0;
    }

    private static int Xref(Args args)
    {
        var paths = Paths(args);
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        var idx = PlfReader.ReadIndex(paths.Idx);
        PlfReader.AttachIndex(plf, idx);
        if (args.Get("fs2xml") is { } xmlPath) plf.EnrichWith(ObjectExport.Read(xmlPath).ByGuid);

        var xref = XrefReader.Read(paths.XrefDb, plf);
        if (args.Get("impact") is { } target)
        {
            var impact = xref.Impact(target);
            if (args.Get("json") is { } ij) JsonUtil.Write(impact, ij);
            else Console.WriteLine(JsonUtil.Serialize(impact));
            return 0;
        }
        if (args.Get("dot") is { } dot)
        {
            var full = Path.GetFullPath(dot);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, xref.ToDot(), new UTF8Encoding(false));
            Console.Error.WriteLine($"wrote {full}");
        }

        if (args.Get("json") is { } json) JsonUtil.Write(xref, json);
        else
        {
            Console.WriteLine($"XRef parts={xref.Summary.PartCount} decoded={xref.Summary.DecodedParts} null={xref.Summary.NullParts} unmatched={xref.Summary.UnmatchedParts}");
            Console.WriteLine($"objects={xref.Summary.ObjectCount} relations={xref.Summary.RelationCount} addresses={xref.Summary.AddressCount}");
            foreach (var kv in xref.Summary.RelationTypes.Take(15)) Console.WriteLine($"  {kv.Key}  {kv.Value}");
        }
        return 0;
    }

    private static int Vci(Args args)
    {
        var isVerify = args.Positional.Count >= 2 && args.Positional[1].Equals("verify", StringComparison.OrdinalIgnoreCase);
        var paths = Paths(args, isVerify ? 2 : 1);
        if (isVerify)
        {
            var verify = VciVerifier.Verify(paths.Root, paths.VciDb);
            JsonUtil.Write(verify, args.Get("json"));
            Console.WriteLine($"VCI verify total={verify.Total} ok={verify.Ok} mismatch={verify.Mismatch} missing={verify.Missing} errors={verify.Errors}");
            return 0;
        }
        var vci = VciReader.Read(paths.VciDb, args.GetInt("max-rows", 5000));
        if (args.Get("json") is { } json) JsonUtil.Write(vci, json);
        else
        {
            Console.WriteLine($"VCI {vci.FilePath}");
            foreach (var t in vci.Tables) Console.WriteLine($"  {t.Name,-24} {t.RowCount,8} rows  ({t.Columns.Count} columns)");
        }
        return 0;
    }

    private static int Opns(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("opns needs a subcommand: schemas|api|fs2xml|blocks");
        var sub = args.Positional[1].ToLowerInvariant();
        return sub switch
        {
            "schemas" => OpnsSchemas(args),
            "api" => OpnsApi(args),
            "fs2xml" => OpnsFs2Xml(args),
            "blocks" => OpnsBlocks(args),
            "simaticml" => OpnsSimaticMl(args),
            _ => throw new CliException($"unknown opns subcommand '{sub}'")
        };
    }

    private static int OpnsSchemas(Args args)
    {
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin() ?? throw new CliException("TIA bin not found; pass --bin");
        var outDir = args.Require("out");
        var result = SchemaExtractor.Extract(bin, outDir);
        if (args.Get("json") is { } json) JsonUtil.Write(result, json);
        else
        {
            Console.WriteLine($"extracted {result.Schemas.Count} XSD resources to {result.OutPath}");
            foreach (var s in result.Schemas) Console.WriteLine($"  {s.FileName,-45} {s.Length,8}  {s.Assembly}");
            foreach (var m in result.MissingIncludes) Console.WriteLine($"  missing include: {m}");
        }
        return 0;
    }

    private static int OpnsApi(Args args)
    {
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin() ?? throw new CliException("TIA bin not found; pass --bin");
        var outFile = args.Require("out");
        var index = ApiIndexer.Index(bin);
        JsonUtil.Write(index, outFile);
        Console.WriteLine($"indexed {index.Assemblies.Sum(a => a.Types.Count)} types in {index.Assemblies.Count} assemblies");
        return 0;
    }

    private static int OpnsFs2Xml(Args args)
    {
        var paths = Paths(args, 2);
        var outFile = args.Require("out");
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin() ?? throw new CliException("TIA bin not found; pass --bin");
        var result = Fs2XmlBridge.ExportPlain(paths.Plf, outFile, bin, args.GetInt("timeout", 600), args.Has("keep-encrypted"));
        if (!result.Ok) throw new CliException("FS2XML export failed: " + (result.Error ?? "unknown"));
        Console.WriteLine($"wrote {result.PlainPath} ({result.PlainLength:N0} bytes)");
        if (!string.IsNullOrEmpty(result.EncryptedPath))
            Console.WriteLine($"encrypted intermediate: {result.EncryptedPath} ({result.EncryptedLength:N0} bytes)");
        return 0;
    }

    private static int OpnsBlocks(Args args)
    {
        if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
        var xml = args.Positional[2];
        var outDir = args.Require("out");
        var export = ObjectExport.Read(xml);
        export.WriteBlockSources(Path.Combine(outDir, "sources"));
        var report = new
        {
            export.FilePath,
            export.FileVersion,
            export.ObjectCount,
            blocks = export.Blocks.Select(b => new
            {
                b.Name, b.Subtype, b.Number, b.BlockType, b.ProgrammingLanguage, b.LanguageName,
                b.BlockGuid, b.BlockInstanceId, b.CompileUnitInstanceId, b.CompileUnitGuid,
                b.SourceRoot, b.SourceLength, b.SourceFile
            }),
            export.Warnings
        };
        JsonUtil.Write(report, Path.Combine(outDir, "blocks.json"));
        Console.WriteLine($"blocks={export.Blocks.Count} sources={export.Blocks.Count(b => b.SourceFile is not null)} -> {outDir}");
        return 0;
    }

    private static int OpnsSimaticMl(Args args)
    {
        if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
        var outDir = args.Require("out");
        var export = ObjectExport.Read(args.Positional[2]);
        var result = SimaticMlExporter.Export(export, outDir);
        JsonUtil.Write(result, Path.Combine(outDir, "simaticml.json"));
        Console.WriteLine($"SimaticML blocks={result.Blocks.Count} -> {outDir}");
        foreach (var b in result.Blocks) Console.WriteLine($"  {b.Name,-24} {b.Element,-16} body={b.BodyRoot ?? "-",-10} sections={string.Join(",", b.Sections)}");
        return 0;
    }

    private static int Mc7(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("disasm", StringComparison.OrdinalIgnoreCase))
            throw new CliException("mc7 needs subcommand: disasm");
        if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
        var outDir = args.Require("out");
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin() ?? throw new CliException("TIA bin not found; pass --bin");
        var export = ObjectExport.Read(args.Positional[2]);
        var result = Mc7Extractor.Extract(export, outDir, bin);
        Console.WriteLine($"MC7 blocks={result.Blocks.Count} instructions={result.Blocks.Sum(b => b.Instructions.Count)} -> {outDir}");
        foreach (var w in result.Warnings) Console.Error.WriteLine("mc7: " + w);
        foreach (var b in result.Blocks) Console.WriteLine($"  {b.Name,-24} {b.Subtype,-16} code={b.CodeLength,4} lang={b.LanguageLevel?.ToString() ?? "?",4} ins={b.Instructions.Count,3} {b.Error}");
        return 0;
    }

    private static int Platform(Args args)
    {
        if (args.Positional.Count < 2)
            throw new CliException("platform needs subcommand: run|realpair");
        var sub = args.Positional[1].ToLowerInvariant();
        var paths = ProjectPaths.Resolve(args.Require("project"));
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin();
        if (sub == "run")
        {
            var result = PlatformRun.Run(paths, args.Require("fs2xml"), args.Require("out"), args.Get("key"), bin,
                args.Get("release-project"), args.Get("release-export"), args.Get("compiler-export"));
            Console.WriteLine($"platform status={result.Status} steps={result.Steps.Count} failed={result.Steps.Count(s => s.Status == "fail")}");
            foreach (var s in result.Steps) Console.WriteLine($"  {s.Name,-24} {s.Status,-5} {s.Detail}");
            return result.Status == "fail" ? 1 : 0;
        }
        if (sub == "realpair")
        {
            var releaseProject = ProjectPaths.Resolve(args.Require("release-project"));
            var result = PlatformRealPair.Run(paths, args.Require("fs2xml"), releaseProject,
                args.Require("release-export"), args.Require("out"), bin, args.Get("fleet-spec"),
                args.Get("compiler-export"));
            Console.WriteLine($"realpair status={result.Status} steps={result.Steps.Count} failed={result.Steps.Count(s => s.Status == "fail")}");
            foreach (var s in result.Steps) Console.WriteLine($"  {s.Name,-12} {s.Status,-5} {s.Detail}");
            return result.Status == "fail" ? 1 : 0;
        }
        throw new CliException("unknown platform subcommand");
    }
    private static int Deploy(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("deploy needs subcommand: rollback|pod|verify-pod|consolidate");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "rollback")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var manifest = RollbackDesk.Create(paths, args.Require("fs2xml"), args.Require("out"), args.Get("key"));
            Console.WriteLine($"rollback payload={manifest.Payload.Count} seal={manifest.Seal[..16]} signature={manifest.Signature is not null}");
            return 0;
        }
        if (sub == "pod")
        {
            var rec = ProofOfDelivery.Create(args.Require("artifact"), args.Require("cpu-serial"), args.Require("rack-slot"), args.Require("key"));
            JsonUtil.Write(rec, args.Require("out"));
            Console.WriteLine($"pod artifact={rec.ArtifactHash[..12]} cpu={rec.CpuSerial} slot={rec.RackSlot}");
            return 0;
        }
        if (sub == "verify-pod")
        {
            var rec = JsonSerializer.Deserialize<PodRecord>(File.ReadAllText(args.Require("pod")), JsonUtil.Options)!;
            var ok = ProofOfDelivery.Verify(rec, args.Require("public"));
            Console.WriteLine($"pod verify ok={ok}");
            return ok ? 0 : 1;
        }
        if (sub == "consolidate")
        {
            var changes = JsonSerializer.Deserialize<List<ChangeItem>>(File.ReadAllText(args.Require("changes")), JsonUtil.Options)!;
            var result = LtlConsolidator.Consolidate(changes);
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"consolidate changes={result.Changes} batches={result.Batches}");
            return 0;
        }
        throw new CliException("unknown deploy subcommand");
    }

    private static int Warehouse(Args args)
    {
        if (args.Positional.Count < 2)
            throw new CliException("warehouse needs subcommand: add|release|list|review|umac|test");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "add")
        {
            var item = BondedWarehouse.Add(args.Require("file"), args.Require("id"), args.Require("out"));
            var digest = item.UmacDigest.Length >= 12 ? item.UmacDigest[..12] : item.UmacDigest;
            Console.WriteLine($"warehouse add id={item.Id} status={item.Status} hash={item.Hash[..12]} umacObjects={item.UmacObjectCount} umacDigest={digest}");
            return 0;
        }
        if (sub == "release")
        {
            var tests = (args.Get("tests") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            UmacReviewRecord? review = null;
            if (args.Get("umac-review") is { } reviewPath)
                review = JsonSerializer.Deserialize<UmacReviewRecord>(File.ReadAllText(reviewPath), JsonUtil.Options);
            var item = BondedWarehouse.Release(args.Require("dir"), args.Require("id"), tests, review, args.Get("public"));
            var reviewHash = item.UmacReviewHash is { Length: >= 12 } h ? h[..12] : item.UmacReviewHash;
            Console.WriteLine($"warehouse release id={item.Id} status={item.Status} tests={item.Tests.Count} umac={item.UmacReviewDecision} review={reviewHash}");
            return 0;
        }
        if (sub == "list")
        {
            var items = BondedWarehouse.List(args.Require("dir"));
            JsonUtil.Write(items, args.Require("out"));
            foreach (var i in items)
                Console.WriteLine($"  {i.Id,-20} {i.Status,-10} {i.Hash[..12]} umac={i.UmacObjectCount}");
            return 0;
        }
        if (sub == "review")
        {
            var record = UmacReview.Create(args.Require("artifact"), args.Get("reviewed-by") ?? "unknown",
                args.Get("decision") ?? "approved", args.Get("key"));
            JsonUtil.Write(record, args.Require("out"));
            Console.WriteLine($"warehouse review artifact={record.ArtifactHash[..12]} objects={record.ObjectCount} decision={record.Decision} hash={record.ReviewHash[..12]} signed={record.Signature is not null}");
            return 0;
        }
        if (sub == "umac")
        {
            var digest = UmacDigest.Compute(args.Require("artifact"));
            JsonUtil.Write(digest, args.Require("out"));
            Console.WriteLine($"warehouse umac objects={digest.ObjectCount} digest={digest.Digest[..12]} classes={digest.Classes.Count}");
            return 0;
        }
        if (sub == "test")
        {
            var result = WarehouseSelfTest.Run(args.Require("artifact"), args.Require("out"),
                args.Get("key") ?? Path.Combine(args.Require("out"), "registry", "warehouse"));
            Console.WriteLine($"warehouse test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }
        throw new CliException("unknown warehouse subcommand");
    }
    private static int Customs(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("classify", StringComparison.OrdinalIgnoreCase))
            throw new CliException("customs needs subcommand: classify");
        var tariff = CustomsBroker.Classify(ObjectExport.Read(args.Require("fs2xml")), args.Require("block"));
        JsonUtil.Write(tariff, args.Require("out"));
        Console.WriteLine($"customs block={tariff.Block} code={tariff.TariffCode} duties={string.Join(",", tariff.Duties)}");
        return 0;
    }

    private static int Queue(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("age", StringComparison.OrdinalIgnoreCase))
            throw new CliException("queue needs subcommand: age");
        var items = JsonSerializer.Deserialize<List<QueueItem>>(File.ReadAllText(args.Require("items")), JsonUtil.Options)!;
        var invoice = QueueMeter.Age(items, double.TryParse(args.Get("rate"), out var rate) ? rate : 50);
        JsonUtil.Write(invoice, args.Require("out"));
        Console.WriteLine($"queue items={invoice.Items} hours={invoice.TotalHours:F1} cost={invoice.TotalCost:F2}");
        return 0;
    }

    private static CompilerProbeResult LoadProbe(Args args)
    {
        if (args.Get("probe") is { } p)
            return JsonSerializer.Deserialize<CompilerProbeResult>(File.ReadAllText(p), JsonUtil.Options)!;
        return CompilerProbe.Build(ObjectExport.Read(args.Require("fs2xml")),
            args.Get("bin") ?? FileUtil.FindTiaBin());
    }

    private static int Compiler(Args args)
    {
        if (args.Positional.Count < 2)
            throw new CliException("compiler needs subcommand: probe|stats|model|predict|differential|test|classic-test");
        var sub = args.Positional[1].ToLowerInvariant();
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin();
        if (sub == "probe")
        {
            var result = CompilerProbe.Build(ObjectExport.Read(args.Require("fs2xml")), bin);
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"compiler probe observed={result.Observed} probes={result.Probes} withMc7={result.WithMc7} opcodes={result.OpcodeVocabulary.Count}");
            foreach (var r in result.Records.Take(24))
                Console.WriteLine($"  {r.Kind,-8} {r.Block,-34} src={r.Source.SourceLength,5} mc7={r.Mc7.Length,4} ins={r.Mc7.Instructions,3}");
            return 0;
        }
        if (sub == "stats")
        {
            var probe = LoadProbe(args);
            var stats = new
            {
                probe.Observed,
                probe.Probes,
                probe.WithMc7,
                opcodes = probe.OpcodeVocabulary.Count,
                observedRows = probe.Records.Count(r => r.Kind == "observed"),
                probeRows = probe.Records.Count(r => r.Kind == "probe"),
                observedWithMc7 = probe.Records.Count(r => r.Kind == "observed" && r.Mc7.Length > 0),
                avgObservedLength = probe.Records.Where(r => r.Kind == "observed" && r.Mc7.Length > 0)
                    .Select(r => (double)r.Mc7.Length).DefaultIfEmpty(0).Average(),
                opcodeHistogram = probe.Records.Where(r => r.Mc7.Length > 0)
                    .SelectMany(r => r.Mc7.Opcodes).GroupBy(kv => kv.Key)
                    .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value))
            };
            JsonUtil.Write(stats, args.Require("out"));
            Console.WriteLine($"compiler stats observed={stats.Observed} probes={stats.Probes} withMc7={stats.WithMc7} opcodes={stats.opcodes} avgObservedLength={stats.avgObservedLength:F1}");
            return 0;
        }
        if (sub == "model")
        {
            var probe = LoadProbe(args);
            var model = CodegenModel.Train(probe.Records);
            JsonUtil.Write(model, args.Require("out"));
            Console.WriteLine($"compiler model rows={model.TrainingRows} r2={model.R2:F4} rmse={model.Rmse:F2} opcodes={model.Opcodes.Count}");
            return 0;
        }
        if (sub == "predict")
        {
            var probe = LoadProbe(args);
            var model = CodegenModel.Train(probe.Records);
            var blockName = args.Require("block");
            var rec = probe.Records.FirstOrDefault(r => r.Kind == "observed"
                && string.Equals(r.Block, blockName, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException("observed block not found: " + blockName);
            var pred = model.Predict(rec.Source);
            var hasActual = rec.Mc7.Length > 0;
            var output = new
            {
                block = blockName,
                predictedLength = pred.Length,
                predictedInstructions = pred.Instructions,
                confidence = pred.Confidence,
                opcodes = pred.Opcodes,
                features = pred.Features,
                actualLength = hasActual ? rec.Mc7.Length : (int?)null,
                error = hasActual ? pred.Length - rec.Mc7.Length : (int?)null,
                actualOpcodes = rec.Mc7.Opcodes
            };
            JsonUtil.Write(output, args.Require("out"));
            var actualText = hasActual ? rec.Mc7.Length.ToString() : "not-compiled";
            var errorText = hasActual ? (pred.Length - rec.Mc7.Length).ToString() : "n/a";
            Console.WriteLine($"compiler predict block={blockName} length={pred.Length} actual={actualText} error={errorText} confidence={pred.Confidence:F2}");
            return 0;
        }
        if (sub == "differential")
        {
            var probe = LoadProbe(args);
            var model = CodegenModel.Train(probe.Records);
            var diff = CompilerDifferential.Run(probe, model);
            JsonUtil.Write(diff, args.Require("out"));
            Console.WriteLine($"compiler differential rows={diff.Rows} mae={diff.MeanAbsoluteError:F2} jaccard={diff.MeanJaccard:F2} exact={diff.ExactOpcodeRate:F2} pass={diff.Pass}");
            foreach (var row in diff.Items)
                Console.WriteLine($"  {row.Block,-16} actual={row.ActualLength,3} predicted={row.PredictedLength,3} err={row.Error,3} jaccard={row.Jaccard:F2}");
            return diff.Pass ? 0 : 1;
        }
        if (sub == "classic-test")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var result = CompilerSelfTest.Run(export, bin, args.Require("out"));
            var gate = result.Pass && result.RealCodegen && result.ObservedDistinctMc7 > 1;
            Console.WriteLine($"compiler classic-test pass={gate} observed={result.Observed} withMc7={result.WithMc7} distinct={result.ObservedDistinctMc7} realCodegen={result.RealCodegen} observedLoo={result.ObservedLooPassed}/{result.ObservedLooRows}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return gate ? 0 : 1;
        }
        if (sub == "test")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var result = CompilerSelfTest.Run(export, bin, args.Require("out"));
            Console.WriteLine($"compiler test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }
        throw new CliException("unknown compiler subcommand");
    }

    private static List<string> SplitCsv(string text) =>
        text.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int Evolve(Args args)
    {
        if (args.Positional.Count < 2)
            throw new CliException("evolve needs subcommand: run|trace|test|recorded|recorded-test");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "test")
        {
            var result = EvolveSelfTest.Run(args.Require("out"));
            Console.WriteLine($"evolve test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }
        if (sub == "recorded-test")
        {
            var trace = JsonSerializer.Deserialize<FlightTrace>(File.ReadAllText(args.Require("trace")), JsonUtil.Options)
                        ?? throw new CliException("invalid flight trace");
            var result = RecordedEvolution.RunSynthetic(trace, args.Require("input"), args.Require("output"), args.Require("out"));
            Console.WriteLine($"evolve recorded-test samples={result.Samples} baseline={result.BaselineFitness:F2} mutated={result.MutatedFitness:F2} pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }
        var export = ObjectExport.Read(args.Require("fs2xml"));
        var blockName = args.Require("block");
        if (sub == "trace")
        {
            var trace = EvolveEngine.GenerateTrace(export, blockName, args.GetInt("samples", 8));
            JsonUtil.Write(trace, args.Require("out"));
            Console.WriteLine($"evolve trace block={blockName} samples={trace.Samples.Count}");
            return 0;
        }
        if (sub == "run")
        {
            var trace = JsonSerializer.Deserialize<IoTrace>(File.ReadAllText(args.Require("trace")), JsonUtil.Options)
                        ?? throw new CliException("invalid trace");
            var result = EvolveEngine.Run(export, blockName, trace, args.GetInt("count", 12));
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"evolve block={result.Block} baseline={result.BaselineFitness:F2} best={result.BestFitness:F2} improved={result.Improved} mutation={result.Best?.Mutation}");
            foreach (var c in result.Candidates.OrderByDescending(c => c.Fitness).Take(5))
                Console.WriteLine($"  {c.Index,2} {c.Fitness:F2} {c.Matched}/{c.Total} {c.Mutation}");
            return result.BestFitness + 1e-9 >= result.BaselineFitness ? 0 : 1;
        }
        if (sub == "recorded")
        {
            var trace = JsonSerializer.Deserialize<FlightTrace>(File.ReadAllText(args.Require("trace")), JsonUtil.Options)
                        ?? throw new CliException("invalid flight trace");
            var inputs = SplitCsv(args.Require("inputs"));
            var outputs = SplitCsv(args.Require("outputs"));
            var result = RecordedEvolution.RunReal(export, trace, blockName, inputs, outputs, args.Require("out"));
            Console.WriteLine($"evolve recorded block={result.Block} samples={result.Samples} baseline={result.BaselineFitness:F2} mutated={result.MutatedFitness:F2} mutation={result.Mutation} pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }

        throw new CliException("unknown evolve subcommand");
    }
    private static int ThymusCmd(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("thymus needs subcommand: train|scan");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "train")
        {
            var result = Thymus.Train(ObjectExport.Read(args.Require("fs2xml")), args.GetInt("mutants", 20));
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"thymus train legitimate={result.LegitimateBlocks} detectors={result.Detectors}");
            return 0;
        }
        if (sub == "scan")
        {
            var trained = JsonSerializer.Deserialize<ThymusTrainResult>(File.ReadAllText(args.Require("detectors")), JsonUtil.Options)!;
            var result = Thymus.Scan(ObjectExport.Read(args.Require("fs2xml")), trained);
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"thymus scan blocks={result.Blocks} flagged={result.Flagged}");
            return 0;
        }
        throw new CliException("unknown thymus subcommand");
    }

    private static int Replay(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("consolidate", StringComparison.OrdinalIgnoreCase))
            throw new CliException("replay needs subcommand: consolidate");
        var paths = ProjectPaths.Resolve(args.Require("project"));
        var export = ObjectExport.Read(args.Require("fs2xml"));
        var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
            Path.Combine(Path.GetTempPath(), "tia-project", "replay"), export, false);
        var result = ReplayConsolidator.Consolidate(history);
        JsonUtil.Write(result, args.Require("out"));
        Console.WriteLine($"replay savepoints={result.Savepoints} objects={result.Objects} stable={result.Stable} oneOff={result.OneOff}");
        return 0;
    }

    private static int Contracts(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("discover", StringComparison.OrdinalIgnoreCase))
            throw new CliException("contracts needs subcommand: discover");
        var result = ContractDiscovery.Discover(ObjectExport.Read(args.Require("fs2xml")), args.GetInt("min-blocks", 2));
        JsonUtil.Write(result, args.Require("out"));
        Console.WriteLine($"contracts minBlocks={result.MinBlocks} contracts={result.Contracts}");
        foreach (var c in result.Items) Console.WriteLine($"  {c.Symbol}: {string.Join(",", c.Blocks)}");
        return 0;
    }

    private static int MiddenCmd(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("analyze", StringComparison.OrdinalIgnoreCase))
            throw new CliException("midden needs subcommand: analyze");
        var result = Midden.Analyze(ObjectExport.Read(args.Require("fs2xml")));
        JsonUtil.Write(result, args.Require("out"));
        Console.WriteLine($"midden objects={result.Objects} orphans={result.Orphans} clusters={result.Clusters.Count}");
        foreach (var c in result.Clusters.Take(10)) Console.WriteLine($"  {c.Kind,-40} {c.Count}");
        return 0;
    }

    private static int Fixture(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("create", StringComparison.OrdinalIgnoreCase))
            throw new CliException("fixture needs subcommand: create");
        var result = ReleaseFixture.Create(args.Require("project"), args.Require("fs2xml"), args.Require("out"),
            args.Get("name") ?? "release-r2", args.Get("version") ?? "0.1400.2.0", !args.Has("no-tag"));
        var report = args.Get("json") ?? Path.Combine(args.Require("out"), "fixture.json");
        JsonUtil.Write(result, report);
        Console.WriteLine($"fixture create name={result.Name} project={result.ProjectRoot} version={result.FormatVersion} records={result.Records} live={result.Live} tag={result.AddedTag}");
        return 0;
    }

    private static int Drift(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("drift needs subcommand: snapshot|compare");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "snapshot")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var snap = FormatDrift.Snapshot(paths, args.Require("fs2xml"));
            JsonUtil.Write(snap, args.Require("out"));
            Console.WriteLine($"drift snapshot types={snap.PlfTypes.Count} xref={snap.XrefSchemaHash?[..12]} entries={snap.IdxEntries}");
            return 0;
        }
        if (sub == "compare")
        {
            var a = JsonSerializer.Deserialize<FormatSnapshot>(File.ReadAllText(args.Require("a")), JsonUtil.Options)!;
            var b = JsonSerializer.Deserialize<FormatSnapshot>(File.ReadAllText(args.Require("b")), JsonUtil.Options)!;
            var report = FormatDrift.Compare(a, b);
            JsonUtil.Write(report, args.Require("out"));
            Console.WriteLine($"drift severity={report.Severity} breaking={report.Breaking.Count} additive={report.Additive.Count}");
            foreach (var x in report.Breaking) Console.WriteLine("  BREAKING " + x);
            return report.Severity == "breaking" ? 1 : 0;
        }
        throw new CliException("unknown drift subcommand");
    }

    private static int Corpus(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("corpus needs subcommand: build|verify|mutate");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "build")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var manifest = GoldenCorpus.Build(paths, args.Require("fs2xml"), args.Require("out"));
            Console.WriteLine($"corpus build blocks={manifest.Expected.Blocks.Count} tags={manifest.Expected.Tags.Count} screens={manifest.Expected.Screens.Count}");
            return 0;
        }
        if (sub == "verify")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var result = GoldenCorpus.Verify(args.Require("corpus"), paths, args.Require("fs2xml"));
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"corpus verify pass={result.Pass} checks={result.Checks.Count} failures={result.Failures.Count}");
            foreach (var f in result.Failures) Console.WriteLine("  FAIL " + f);
            return result.Pass ? 0 : 1;
        }
        if (sub == "mutate")
        {
            var files = GoldenCorpus.Mutate(args.Require("fs2xml"), args.Require("out"), args.GetInt("count", 5));
            Console.WriteLine($"corpus mutants={files.Count}");
            return 0;
        }
        if (sub == "multi")
        {
            var spec = JsonSerializer.Deserialize<CorpusSpec>(File.ReadAllText(args.Require("spec")), JsonUtil.Options)
                       ?? throw new CliException("invalid corpus spec");
            var index = GoldenCorpusIndex.Build(spec, args.Require("out"));
            Console.WriteLine($"corpus multi projects={index.Projects} records={index.TotalRecords} blocks={index.TotalBlocks} tags={index.TotalTags} screens={index.TotalScreens} pass={index.Pass}");
            foreach (var e in index.Entries)
                Console.WriteLine($"  {e.Name,-18} records={e.Records,5} blocks={e.Blocks,3} tags={e.Tags,3} screens={e.Screens,3} xref={e.XrefParts}/{e.XrefRelations} verify={e.VerifyPass}");
            return index.Pass ? 0 : 1;
        }
        throw new CliException("unknown corpus subcommand");
    }

    private static int Fleet(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("fleet needs subcommand: record|report|aggregate|pattern-test");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "record")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var record = FleetCorpus.Record(paths, args.Require("fs2xml"), args.Get("patterns"));
            JsonUtil.Write(record, args.Require("out"));
            Console.WriteLine($"fleet record name={record.Name} blocks={record.Blocks} tags={record.Tags} screens={record.Screens} patterns={record.FailurePatterns.Count}");
            return 0;
        }
        if (sub == "report")
        {
            var report = FleetCorpus.Report(args.Require("dir"));
            JsonUtil.Write(report, args.Require("out"));
            Console.WriteLine($"fleet records={report.Records} avgBlocks={report.AvgBlocks:F1} avgTags={report.AvgTags:F1} highFindings={report.HighFindings} patterns={report.TotalPatterns} crossPlant={report.CrossPlantPatterns}");
            foreach (var pattern in report.Patterns)
                Console.WriteLine($"  {pattern.Pattern,-18} plants={pattern.Plants} count={pattern.Count} tags={string.Join(",", pattern.Tags)}");
            return 0;
        }
        if (sub == "aggregate")
        {
            var spec = JsonSerializer.Deserialize<FleetSpec>(File.ReadAllText(args.Require("spec")), JsonUtil.Options)
                       ?? throw new CliException("invalid fleet spec");
            var report = FleetAggregate.Run(spec, args.Require("out"));
            Console.WriteLine($"fleet aggregate records={report.Records} patterns={report.TotalPatterns} crossPlant={report.CrossPlantPatterns} plantsWithPatterns={report.PlantsWithPatterns}");
            foreach (var pattern in report.Patterns)
                Console.WriteLine($"  {pattern.Pattern,-18} plants={pattern.Plants} count={pattern.Count} plants={string.Join(",", pattern.PlantNames)}");
            return 0;
        }
        if (sub == "pattern-test")
        {
            var result = FleetPatternSelfTest.Run(args.Require("out"));
            Console.WriteLine($"fleet pattern-test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            foreach (var c in result.NegativeChecks) Console.WriteLine("  negative." + c);
            return result.Pass ? 0 : 1;
        }
        throw new CliException("unknown fleet subcommand");
    }

    private static int Certify(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("certify needs subcommand: equivalence|bisim|bisim-test|mc7-test|symbolic|symbolic-test|semantic|semantic-test");
        var export = ObjectExport.Read(args.Require("fs2xml"));
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin();
        var mc7 = Mc7Analyzer.Analyze(export, bin);
        var ir = IrExporter.Build(export);
        var a = args.Get("a") ?? "";
        var b = args.Get("b") ?? "";
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "mc7-test")
        {
            var test = Mc7SelfTest.Run(mc7);
            JsonUtil.Write(test, args.Require("out"));
            Console.WriteLine($"mc7-test pass={test.Pass}");
            foreach (var c in test.Checks) Console.WriteLine("  " + c);
            return test.Pass ? 0 : 1;
        }
        if (sub == "symbolic-test")
        {
            var test = Mc7SymbolicSelfTest.Run(mc7);
            JsonUtil.Write(test, args.Require("out"));
            Console.WriteLine($"symbolic-test pass={test.Pass}");
            foreach (var c in test.Checks) Console.WriteLine("  " + c);
            return test.Pass ? 0 : 1;
        }
        if (sub == "semantic-test")
        {
            var test = Mc7SemanticSelfTest.Run(mc7);
            JsonUtil.Write(test, args.Require("out"));
            Console.WriteLine($"semantic-test pass={test.Pass}");
            foreach (var c in test.Checks) Console.WriteLine("  " + c);
            return test.Pass ? 0 : 1;
        }
        if (sub == "semantic")
        {
            var ma = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, a, StringComparison.OrdinalIgnoreCase))
                     ?? throw new CliException("block not found: " + a);
            var mb = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, b, StringComparison.OrdinalIgnoreCase))
                     ?? throw new CliException("block not found: " + b);
            var cert = Mc7SemanticChecker.Compare(ma, mb);
            JsonUtil.Write(cert, args.Require("out"));
            Console.WriteLine($"semantic a={a} b={b} equivalent={cert.Equivalent} smt={cert.SmtResult} known={cert.AKnown}/{cert.BKnown} unknown={cert.AUnknown}/{cert.BUnknown}");
            if (cert.CounterexampleReason is not null)
                Console.WriteLine($"  counterexample reason={cert.CounterexampleReason} offset={cert.CounterexampleOffset} modelLength={cert.CounterexampleModel?.Length ?? 0}");
            return cert.Equivalent ? 0 : 1;
        }
        if (sub == "symbolic")
        {
            var ma = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, a, StringComparison.OrdinalIgnoreCase))
                     ?? throw new CliException("block not found: " + a);
            var mb = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, b, StringComparison.OrdinalIgnoreCase))
                     ?? throw new CliException("block not found: " + b);
            var cert = Mc7SymbolicChecker.Compare(ma, mb);
            JsonUtil.Write(cert, args.Require("out"));
            Console.WriteLine($"symbolic a={a} b={b} equivalent={cert.Equivalent} smt={cert.SmtResult} instructions={cert.AInstructions}/{cert.BInstructions}");
            if (cert.Counterexample is not null)
                Console.WriteLine($"  counterexample offset={cert.Counterexample.Offset} reason={cert.Counterexample.Reason} {cert.Counterexample.AOpcode} -> {cert.Counterexample.BOpcode}");
            return cert.Equivalent ? 0 : 1;
        }
        if (sub == "equivalence")
        {
            var ma = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, a, StringComparison.OrdinalIgnoreCase));
            var mb = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, b, StringComparison.OrdinalIgnoreCase));
            if (ma is not null && mb is not null)
            {
                var cert = Mc7Equivalence.Compare(ma, mb);
                JsonUtil.Write(cert, args.Require("out"));
                Console.WriteLine($"equivalence a={a} b={b} method={cert.Method} equivalent={cert.Equivalent} bisimilar={cert.Bisimilar} findings={cert.Findings.Count}");
                foreach (var f in cert.Findings) Console.WriteLine("  " + f);
                return cert.Equivalent ? 0 : 1;
            }
            var ua = ir.Units.FirstOrDefault(u => string.Equals(u.Name, a, StringComparison.OrdinalIgnoreCase))
                     ?? throw new CliException("block not found: " + a);
            var ub = ir.Units.FirstOrDefault(u => string.Equals(u.Name, b, StringComparison.OrdinalIgnoreCase))
                     ?? throw new CliException("block not found: " + b);
            var fallback = EquivalenceCertifier.Compare(ua, ub);
            JsonUtil.Write(fallback, args.Require("out"));
            Console.WriteLine($"equivalence a={a} b={b} method=structural equivalent={fallback.Equivalent} findings={fallback.Findings.Count}");
            return fallback.Equivalent ? 0 : 1;
        }
        if (sub == "bisim-test")
        {
            var test = Mc7CycleSelfTest.Run(mc7);
            JsonUtil.Write(test, args.Require("out"));
            Console.WriteLine($"bisim-test pass={test.Pass} cases={test.Cases.Count}");
            foreach (var c in test.Checks) Console.WriteLine("  " + c);
            return test.Pass ? 0 : 1;
        }
        if (sub == "bisim")
        {
            var ma = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, a, StringComparison.OrdinalIgnoreCase));
            var mb = mc7.Blocks.FirstOrDefault(x => string.Equals(x.Name, b, StringComparison.OrdinalIgnoreCase));
            if (ma is not null && mb is not null && !args.Has("graph"))
            {
                var cert = Mc7CycleChecker.Compare(ma, mb,
                    cycles: args.GetInt("cycles", 3),
                    observeBits: ParseAddresses(args.Get("observe-bits")),
                    observeBytes: ParseAddresses(args.Get("observe-bytes")),
                    initialBits: ParseInitialBits(args.Get("init-bits")),
                    initialBytes: ParseInitialBytes(args.Get("init-bytes")));
                JsonUtil.Write(cert, args.Require("out"));
                Console.WriteLine($"bisim a={a} b={b} method={cert.Method} equivalent={cert.Equivalent} smt={cert.SmtResult} cycles={cert.CyclesChecked}/{cert.Cycles} paths={cert.PathsA}/{cert.PathsB} pairs={cert.PairsChecked} complete={cert.Complete}");
                Console.WriteLine($"  observe.bits={string.Join(",", cert.ObservationBits)} observe.bytes={string.Join(",", cert.ObservationBytes)}");
                if (cert.Counterexample is not null)
                {
                    Console.WriteLine($"  counterexample cycle={cert.Counterexample.Cycle} reason={cert.Counterexample.Reason} pathA={cert.Counterexample.PathA} pathB={cert.Counterexample.PathB}");
                    foreach (var d in cert.Counterexample.Differences)
                        Console.WriteLine($"    {d.Kind} {d.Address}: A={d.A} B={d.B}");
                }
                return cert.Equivalent ? 0 : 1;
            }
            if (ma is not null && mb is not null)
            {
                var cert = Mc7Bisimulation.Compare(Mc7GraphBuilder.Build(ma), Mc7GraphBuilder.Build(mb));
                JsonUtil.Write(cert, args.Require("out"));
                Console.WriteLine($"bisim a={a} b={b} method=mc7-graph bisimilar={cert.Bisimilar} iterations={cert.Iterations}");
                Console.WriteLine($"  aColor={cert.AColor}");
                Console.WriteLine($"  bColor={cert.BColor}");
                return cert.Bisimilar ? 0 : 1;
            }
            var idA = ir.Units.FirstOrDefault(u => string.Equals(u.Name, a, StringComparison.OrdinalIgnoreCase))?.Id ?? a;
            var idB = ir.Units.FirstOrDefault(u => string.Equals(u.Name, b, StringComparison.OrdinalIgnoreCase))?.Id ?? b;
            var fallback = BisimulationOracle.Check(ir, idA, idB);
            JsonUtil.Write(fallback, args.Require("out"));
            Console.WriteLine($"bisim a={a} b={b} method=structural bisimilar={fallback.Bisimilar} iterations={fallback.Iterations}");
            return fallback.Bisimilar ? 0 : 1;
        }
        throw new CliException("unknown certify subcommand");
    }

    private static IEnumerable<uint>? ParseAddresses(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var list = new List<uint>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var text = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
            if (!uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value))
                throw new CliException("invalid address: " + part);
            list.Add(value);
        }
        return list;
    }

    private static Dictionary<uint, bool>? ParseInitialBits(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var map = new Dictionary<uint, bool>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            var address = ParseAddresses(pair[0])!.First();
            if (pair.Length < 2) throw new CliException("initial bit needs addr=value: " + part);
            map[address] = pair[1] is "1" or "true" or "TRUE";
        }
        return map;
    }

    private static Dictionary<uint, byte>? ParseInitialBytes(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var map = new Dictionary<uint, byte>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            var address = ParseAddresses(pair[0])!.First();
            if (pair.Length < 2 || !byte.TryParse(pair[1], out var value))
                throw new CliException("initial byte needs addr=value: " + part);
            map[address] = value;
        }
        return map;
    }

    private static int Registry(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("registry needs subcommand: init|sign|verify|attest");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "init")
        {
            var keys = LogicRegistry.InitKey(args.Require("key"));
            Console.WriteLine($"private={keys.PrivateKey}");
            Console.WriteLine($"public={keys.PublicKey}");
            return 0;
        }
        if (sub == "sign")
        {
            var record = LogicRegistry.Sign(args.Require("key"), args.Require("artifact"));
            JsonUtil.Write(record, args.Require("out"));
            Console.WriteLine($"signed {record.Artifact} hash={record.Hash}");
            return 0;
        }
        if (sub == "verify")
        {
            var record = JsonSerializer.Deserialize<SignatureRecord>(File.ReadAllText(args.Require("signature")), JsonUtil.Options)!;
            var ok = LogicRegistry.Verify(args.Require("public"), args.Require("artifact"), record);
            Console.WriteLine($"verify ok={ok}");
            return ok ? 0 : 1;
        }
        if (sub == "attest")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var manifest = new RegistryManifest { Project = export.FilePath, Created = DateTime.UtcNow.ToString("O") };
            foreach (var o in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
                                                       || o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal)
                                                       || o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal)))
                manifest.Entries.Add(new ManifestEntry
                {
                    Kind = o.ClassName.Split('.').Last(),
                    Name = o.Name ?? o.InstanceId.ToString(),
                    Hash = PlatformIo.ObjectFingerprint(o)
                });
            var outDir = args.Require("out");
            Directory.CreateDirectory(outDir);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonUtil.Options);
            File.WriteAllBytes(Path.Combine(outDir, "manifest.json"), manifestBytes);
            var sig = LogicRegistry.SignBytes(args.Require("key"), "manifest.json", manifestBytes);
            JsonUtil.Write(sig, Path.Combine(outDir, "manifest.sig.json"));
            Console.WriteLine($"attested entries={manifest.Entries.Count} -> {outDir}");
            return 0;
        }
        throw new CliException("unknown registry subcommand");
    }

    private static int Market(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("market needs subcommand: publish|verify|test");
        var sub = args.Positional[1].ToLowerInvariant();
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin();
        if (sub == "publish")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var analysis = Mc7Analyzer.Analyze(export, bin);
            MarketContract? contract = null;
            if (args.Get("contract") is { } cf) contract = JsonSerializer.Deserialize<MarketContract>(File.ReadAllText(cf), JsonUtil.Options);
            string? projectRoot = null;
            if (args.Get("project") is { } project) projectRoot = ProjectPaths.Resolve(project).Root;
            var pkg = BlockMarket.Publish(export, analysis, args.Require("block"), contract, args.Require("key"),
                projectRoot, args.Get("vci"));
            var outDir = args.Require("out");
            Directory.CreateDirectory(outDir);
            ContractSchema.Write(outDir);
            JsonUtil.Write(pkg, Path.Combine(outDir, "package.json"));
            Console.WriteLine($"market publish block={pkg.Name} contract={pkg.ContractVerified} provenance={pkg.ProvenanceVerified} satisfied={pkg.ContractSatisfied} smt={pkg.ContractVerification?.SmtResult} signature={pkg.Signature?.Hash}");
            foreach (var f in pkg.ContractVerification?.Findings ?? []) Console.WriteLine("  contract: " + f);
            if (pkg.Provenance is not null) Console.WriteLine($"  provenance: {pkg.Provenance.Status} {pkg.Provenance.RelativePath} expected={pkg.Provenance.ExpectedHash} actual={pkg.Provenance.ActualHash}");
            return pkg.ContractSatisfied ? 0 : 1;
        }
        if (sub == "verify")
        {
            var pkg = JsonSerializer.Deserialize<MarketPackage>(File.ReadAllText(args.Require("package")), JsonUtil.Options)!;
            string? projectRoot = null;
            if (args.Get("project") is { } project) projectRoot = ProjectPaths.Resolve(project).Root;
            ObjectExport? export = null;
            Mc7AnalysisResult? analysis = null;
            if (args.Get("fs2xml") is { } fs2xml)
            {
                export = ObjectExport.Read(fs2xml);
                analysis = Mc7Analyzer.Analyze(export, bin);
            }
            var ok = BlockMarket.Verify(pkg, args.Require("public"), projectRoot, args.Get("vci"), export, analysis);
            Console.WriteLine($"market verify block={pkg.Name} contract={pkg.ContractVerified} provenance={pkg.ProvenanceVerified} reverified={export is not null} ok={ok}");
            return ok ? 0 : 1;
        }
        if (sub == "test")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var keyBase = args.Get("key") ?? Path.Combine(args.Require("out"), "registry", "market");
            var result = MarketSelfTest.Run(paths, args.Require("fs2xml"), keyBase, args.Require("out"));
            Console.WriteLine($"market test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }
        throw new CliException("unknown market subcommand");
    }

    private static int Ontology(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("ontology needs subcommand: build|query|stats");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "build")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var result = OntologyBuilder.Build(paths, args.Require("fs2xml"), args.Require("out"));
            Console.WriteLine($"ontology nodes={result.Nodes} edges={result.Edges} savepoints={result.Savepoints} -> {result.DbPath}");
            foreach (var kv in result.Kinds) Console.WriteLine($"  {kv.Key,-12} {kv.Value}");
            return 0;
        }
        if (sub == "query")
        {
            var sql = args.Get("sql") ?? (args.Get("file") is { } f ? File.ReadAllText(f) : throw new CliException("missing --sql or --file"));
            var result = OntologyQuery.Query(args.Require("db"), sql);
            JsonUtil.Write(result, args.Get("json"));
            Console.WriteLine($"rows={result.Rows.Count} columns={string.Join(",", result.Columns)}");
            return 0;
        }
        if (sub == "stats")
        {
            var result = OntologyQuery.Query(args.Require("db"), "SELECT kind, COUNT(*) AS n FROM nodes GROUP BY kind ORDER BY n DESC");
            JsonUtil.Write(result, args.Get("json"));
            foreach (var row in result.Rows) Console.WriteLine($"{row["kind"],-12} {row["n"]}");
            return 0;
        }
        throw new CliException("unknown ontology subcommand");
    }

    private static int Api(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("api needs subcommand: call|benchmark");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "call")
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "id", "name", "tag", "sql", "a", "b", "node", "block" })
                if (args.Get(key) is { } v) dict[key] = v;
            var result = PlatformApi.Call(args.Require("db"), args.Require("verb"), dict);
            JsonUtil.Write(result, args.Get("json"));
            Console.WriteLine($"api verb={result.Verb} count={result.Count}");
            return 0;
        }
        if (sub == "benchmark")
        {
            var result = ApiBenchmark.Run(args.Require("db"));
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"benchmark tasks={result.Tasks} passed={result.Passed} score={result.Score:P0}");
            return 0;
        }
        throw new CliException("unknown api subcommand");
    }

    private static int Ir(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("ir needs subcommand: export|verify|lower|roundtrip|passes");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "passes")
        {
            foreach (var p in IrPasses.List()) Console.WriteLine(p);
            return 0;
        }
        if (sub == "export")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var ir = IrExporter.Build(export);
            JsonUtil.Write(ir, args.Require("out"));
            Console.WriteLine($"ir units={ir.Units.Count} tags={ir.Tags.Count} edges={ir.Edges.Count}");
            return 0;
        }
        if (sub == "verify")
        {
            var ir = JsonSerializer.Deserialize<IrDocument>(File.ReadAllText(args.Require("ir")), JsonUtil.Options)!;
            var result = IrVerifier.Verify(ir);
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"ir verify units={result.Units} bodies={result.Bodies} verified={result.Verified} failed={result.Failed}");
            return result.Failed == 0 ? 0 : 1;
        }
        if (sub == "roundtrip")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var bin = args.Get("bin") ?? FileUtil.FindTiaBin();
            var schemas = args.Get("schemas") ?? FileUtil.FindOpennessSchemas(bin)
                ?? throw new CliException("Openness schema directory not found; pass --schemas <dir>");
            var mc7 = Mc7Analyzer.Analyze(export, bin);
            var result = IrRoundTrip.Run(export, args.Require("out"), schemas, mc7);
            Console.WriteLine($"ir roundtrip pass={result.Pass} units={result.Units} lowered={result.Lowered} schema={result.SchemaValid} body={result.BodyMatches} interface={result.InterfaceMatches} mc7={result.Mc7Matches}/{result.Mc7Units} tags={result.TagsMatch}/{result.Tags} projection={result.ProjectionMatch}");
            foreach (var check in result.Checks) Console.WriteLine("  " + check);
            return result.Pass ? 0 : 1;
        }
        if (sub == "lower")
        {
            var ir = JsonSerializer.Deserialize<IrDocument>(File.ReadAllText(args.Require("ir")), JsonUtil.Options)!;
            var backend = (args.Get("backend") ?? "json").ToLowerInvariant();
            var outPath = args.Require("out");
            if (backend == "json") { JsonUtil.Write(ir, outPath); return 0; }
            if (backend == "dot")
            {
                var sb = new StringBuilder();
                sb.AppendLine("digraph IR { rankdir=LR;");
                foreach (var u in ir.Units) sb.AppendLine($"  \"{u.Id}\" [shape=box,label=\"{u.Name}\\n{u.Language}\"];");
                foreach (var e in ir.Edges) sb.AppendLine($"  \"{e.Src}\" -> \"{e.Dst}\" [label=\"{e.Kind}\"];");
                sb.AppendLine("}");
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                return 0;
            }
            if (backend == "simaticml")
            {
                var export = ObjectExport.Read(args.Require("fs2xml"));
                var result = SimaticMlExporter.Export(export, outPath);
                Console.WriteLine($"ir lower simaticml blocks={result.Blocks.Count}");
                return 0;
            }
            throw new CliException("unknown IR backend: " + backend);
        }
        throw new CliException("unknown ir subcommand");
    }

    private static int Ci(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("ci needs subcommand: run|init|linux");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "init")
        {
            var files = CiInit.Write(args.Require("out"));
            Console.WriteLine($"ci scaffold files={files.Count}");
            foreach (var f in files) Console.WriteLine("  " + f);
            return 0;
        }
        if (sub == "run")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var result = CiRunner.Run(paths, args.Require("fs2xml"), args.Require("out"));
            JsonUtil.Write(result, Path.Combine(args.Require("out"), "ci-report.json"));
            Console.WriteLine($"ci status={result.Status}");
            foreach (var c in result.Checks) Console.WriteLine($"  {c.Name,-14} {c.Status,-6} {c.Detail}");
            return result.Status == "fail" ? 1 : 0;
        }
        if (sub == "linux")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var result = CiLinux.Run(paths, args.Require("fs2xml"), args.Require("out"), args.Get("project-file"));
            Console.WriteLine($"ci linux pass={result.Pass} skipped={result.Skipped} status={result.Report?.Status} reason={result.Reason}");
            foreach (var c in result.Report?.Checks ?? []) Console.WriteLine($"  {c.Name,-14} {c.Status,-6} {c.Detail}");
            return result.Pass ? 0 : 1;
        }
        throw new CliException("unknown ci subcommand");
    }

    private static int Gate(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("gate needs subcommand: compare|bisect");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "compare")
        {
            var baseline = ObjectExport.Read(args.Require("baseline"));
            var candidate = ObjectExport.Read(args.Require("candidate"));
            XrefData? bx = null, cx = null;
            if (args.Get("baseline-project") is { } bp) bx = XrefReader.Read(ProjectPaths.Resolve(bp).XrefDb, null);
            if (args.Get("candidate-project") is { } cp) cx = XrefReader.Read(ProjectPaths.Resolve(cp).XrefDb, null);
            var result = GateEngine.Compare(baseline, candidate, bx, cx);
            var outDir = args.Require("out");
            Directory.CreateDirectory(outDir);
            JsonUtil.Write(result, Path.Combine(outDir, "gate.json"));
            File.WriteAllText(Path.Combine(outDir, "gate.md"), GateMarkdown(result), new UTF8Encoding(false));
            Console.WriteLine($"gate verdict={result.Verdict} high={result.High} medium={result.Medium} low={result.Low}");
            foreach (var f in result.Findings) Console.WriteLine($"  [{f.Severity}] {f.Rule}: {f.Object} — {f.Message}");
            return result.Verdict == "block" ? 1 : 0;
        }
        if (sub == "bisect")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx, Path.Combine(Path.GetTempPath(), "tia-project", "bisect"), export, false);
            var result = GateBisect.Bisect(export, history, args.Require("block"));
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"bisect block={result.Block} key={result.Key} firstChangedSavepoint={result.FirstChangedSavepoint} versions={result.Versions.Count}");
            return 0;
        }
        throw new CliException("unknown gate subcommand");
    }

    private static string GateMarkdown(GateResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Semantic gate");
        sb.AppendLine();
        sb.AppendLine($"Verdict: **{r.Verdict}**  ·  high={r.High} medium={r.Medium} low={r.Low}");
        sb.AppendLine();
        sb.AppendLine("| Severity | Rule | Object | Message |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var f in r.Findings) sb.AppendLine($"| {f.Severity} | {f.Rule} | {f.Object} | {f.Message} |");
        return sb.ToString();
    }

    private static int Merge(Args args)
    {
        var result = SemanticMerge.Merge(args.Require("base"), args.Require("ours"), args.Require("theirs"), args.Require("out"));
        if (args.Get("report") is { } report) JsonUtil.Write(result, report);
        Console.WriteLine($"merge objects={result.Objects} conflicts={result.Conflicts} -> {result.Output}");
        foreach (var c in result.ConflictList) Console.WriteLine($"  conflict {c.Name} ({c.Guid})");
        return result.Conflicts > 0 ? 1 : 0;
    }

    private static int ForensicsCmd(Args args)
    {
        var paths = ProjectPaths.Resolve(args.Require("project"));
        var exportPath = args.Require("fs2xml");
        var export = ObjectExport.Read(exportPath);
        var outDir = args.Require("out");
        var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx, Path.Combine(outDir, "history"), export, false);
        var result = Forensics.Build(paths.Root, exportPath, history, outDir);
        JsonUtil.Write(result, Path.Combine(outDir, "forensics.json"));
        Console.WriteLine($"forensics savepoints={result.Savepoints} versions={result.Versions} -> {outDir}");
        return 0;
    }

    private static int FlightRec(Args args)
    {
        if (args.Positional.Count < 2) throw new CliException("flightrec needs subcommand: record|replay|test|ingest|recorded-test");
        var sub = args.Positional[1].ToLowerInvariant();
        if (sub == "record")
        {
            var export = ObjectExport.Read(args.Require("fs2xml"));
            FlightTrace trace;
            if (args.Get("project") is { } project)
            {
                var paths = ProjectPaths.Resolve(project);
                var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
                    Path.Combine(Path.GetTempPath(), "tia-project", "flightrec"), export, false);
                trace = FlightRecorder.Record(history, export, args.GetInt("samples", 1));
            }
            else
            {
                trace = FlightRecorder.Record(export, args.GetInt("samples", 10));
            }
            JsonUtil.Write(trace, args.Require("out"));
            Console.WriteLine($"recorded samples={trace.Samples.Count} alarms={trace.Alarms.Count} savepoints={trace.SavepointStates.Count}");
            return 0;
        }
        if (sub == "replay")
        {
            var trace = JsonSerializer.Deserialize<FlightTrace>(File.ReadAllText(args.Require("trace")), JsonUtil.Options)!;
            var rules = JsonSerializer.Deserialize<RuleSet>(File.ReadAllText(args.Require("rules")), JsonUtil.Options)!;
            HistoryResult? history = null;
            ObjectExport? export = null;
            if (args.Get("project") is { } project && args.Get("fs2xml") is { } fs2xml)
            {
                var paths = ProjectPaths.Resolve(project);
                export = ObjectExport.Read(fs2xml);
                history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
                    Path.Combine(Path.GetTempPath(), "tia-project", "flightrec"), export, false);
            }
            var result = FlightRecorder.Replay(trace, rules, history, export);
            JsonUtil.Write(result, args.Require("out"));
            Console.WriteLine($"replay samples={result.Samples} violations={result.Violations} attributed={result.AttributedViolations} alarms={result.Alarms.Count}");
            foreach (var v in result.Findings)
                Console.WriteLine($"  t={v.T} sp={v.Savepoint} state={v.StateId?[..12]} {v.Rule} {v.Expr} actual={v.Actual} reason={v.Reason} tagPresent={v.TagPresent}");
            return 0;
        }
        if (sub == "test")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
                Path.Combine(Path.GetTempPath(), "tia-project", "flightrec-test"), export, false);
            var result = FlightRecorder.SelfTest(history, export, args.Require("out"));
            Console.WriteLine($"flightrec test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            return result.Pass ? 0 : 1;
        }
        if (sub == "ingest")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
                Path.Combine(Path.GetTempPath(), "tia-project", "flightrec-ingest"), export, false);
            var ingest = RecordedTraceIngest.Ingest(export, history,
                args.Require("samples"), args.Require("alarms"), args.Require("savepoint-times"));
            if (!ingest.Pass)
            {
                foreach (var error in ingest.Errors) Console.WriteLine("  error: " + error);
                return 1;
            }
            JsonUtil.Write(ingest.Trace, args.Require("out"));
            Console.WriteLine($"flightrec ingest samples={ingest.Samples} alarms={ingest.Alarms} tags={ingest.Tags} savepoints={ingest.Savepoints} hash={ingest.Trace.IngestHash}");
            return 0;
        }
        if (sub == "recorded-test")
        {
            var paths = ProjectPaths.Resolve(args.Require("project"));
            var export = ObjectExport.Read(args.Require("fs2xml"));
            var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
                Path.Combine(Path.GetTempPath(), "tia-project", "flightrec-recorded"), export, false);
            var result = RecordedTraceSelfTest.Run(history, export, args.Require("out"));
            Console.WriteLine($"flightrec recorded-test pass={result.Pass}");
            foreach (var c in result.Checks) Console.WriteLine("  " + c);
            foreach (var c in result.NegativeChecks) Console.WriteLine("  negative." + c);
            return result.Pass ? 0 : 1;
        }
        throw new CliException("unknown flightrec subcommand");
    }

    private static int Prune(Args args)
    {
        var paths = ProjectPaths.Resolve(args.Require("project"));
        var export = ObjectExport.Read(args.Require("fs2xml"));
        var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx, Path.Combine(Path.GetTempPath(), "tia-project", "prune"), export, false);
        var validated = new List<string>();
        if (args.Get("validated") is { } vf && File.Exists(vf))
            validated.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(vf), JsonUtil.Options) ?? []);
        var result = Retention.Prune(history, args.GetInt("keep-last", 2), validated);
        var outDir = args.Require("out");
        Directory.CreateDirectory(outDir);
        JsonUtil.Write(result, Path.Combine(outDir, "retention.json"));
        Console.WriteLine($"prune savepoints={result.Savepoints} kept={result.Kept} removed={result.Removed}");
        return 0;
    }

    private static int Forget(Args args)
    {
        var paths = ProjectPaths.Resolve(args.Require("project"));
        var outRoot = args.Require("out");
        var validated = new List<string>();
        if (args.Get("validated") is { } vf && File.Exists(vf))
            validated.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(vf), JsonUtil.Options) ?? []);
        var result = Forgetting.Execute(paths, args.Require("fs2xml"), outRoot,
            args.GetInt("keep-last", 2), validated, args.Has("full"));
        var report = args.Get("json") ?? Path.Combine(outRoot, "forgetting-result.json");
        JsonUtil.Write(result, report);
        Console.WriteLine($"forget pass={result.Pass} kept={result.KeptSavepoints} removed={result.RemovedSavepoints} newRecords={result.NewRecords} newLive={result.NewLive}");
        foreach (var v in result.Verification) Console.WriteLine("  " + v);
        return result.Pass ? 0 : 1;
    }

    private static int Domain(Args args, string kind)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("export", StringComparison.OrdinalIgnoreCase))
            throw new CliException($"{kind} needs subcommand: export");
        if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
        var outDir = args.Require("out");
        var export = ObjectExport.Read(args.Positional[2]);
        var result = DomainExporter.Export(export, kind, outDir);
        JsonUtil.Write(result, Path.Combine(outDir, kind + ".json"));
        Console.WriteLine($"{kind} objects={result.Count} -> {outDir}");
        return 0;
    }

    private static int History(Args args)
    {
        var paths = Paths(args);
        var outDir = args.Require("out");
        ObjectExport? export = null;
        if (args.Get("fs2xml") is { } xml) export = ObjectExport.Read(xml);
        var result = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx, outDir, export, args.Has("extract"));
        JsonUtil.Write(result, Path.Combine(outDir, "history.json"));
        Console.WriteLine($"history savepoints={result.Savepoints.Count} versions={result.Versions} -> {outDir}");
        foreach (var sp in result.Savepoints)
            Console.WriteLine($"  sp{sp.Index,-2} live={sp.Live,4} new={sp.New,3} changed={sp.Changed,3} deleted={sp.Deleted,3} commit={sp.CommitGuid}");
        return 0;
    }

    private static int Hardware(Args args)
    {
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("export", StringComparison.OrdinalIgnoreCase))
            throw new CliException("hardware needs subcommand: export");
        if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
        var outDir = args.Require("out");
        var export = ObjectExport.Read(args.Positional[2]);
        var result = HardwareExporter.Export(export, outDir);
        JsonUtil.Write(result, Path.Combine(outDir, "hardware.json"));
        Console.WriteLine($"hardware devices={result.Devices.Count} items={result.Devices.Sum(d => d.Items.Count)} nodes={result.Nodes.Count} -> {outDir}");
        foreach (var d in result.Devices) Console.WriteLine($"  {d.Name,-40} {d.Subtype,-20} items={d.Items.Count} nodes={d.Nodes.Count}");
        return 0;
    }

    private static int Hmi(Args args)
    {
        if (args.Positional.Count >= 2 && args.Positional[1].Equals("render", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
            var renderOut = args.Require("out");
            var renderExport = ObjectExport.Read(args.Positional[2]);
            var render = HmiSvgRenderer.Render(renderExport, renderOut);
            JsonUtil.Write(render, Path.Combine(renderOut, "render.json"));
            Console.WriteLine($"HMI screens={render.Screens.Count} -> {renderOut}");
            foreach (var s in render.Screens) Console.WriteLine($"  {s.Name,-24} {s.Width}x{s.Height} items={s.ItemCount} -> {Path.GetFileName(s.SvgFile)}");
            return 0;
        }
        if (args.Positional.Count >= 2 && args.Positional[1].Equals("il", StringComparison.OrdinalIgnoreCase))
        {
            var ilOut = args.Require("out");
            var ilBin = args.Get("bin") ?? FileUtil.FindTiaBin() ?? throw new CliException("TIA bin not found; pass --bin");
            var il = ApiIndexer.Index(ilBin,
                new[]
                {
                    "Siemens.Simatic.Hmi.ILCompiler.RdfModelV21.dll",
                    "Siemens.Simatic.Hmi.ILCompiler.RdfSerializationV21.dll",
                    "Siemens.Simatic.Hmi.ILCompiler.RdfSerializationV21Upd1.dll",
                    "Siemens.Simatic.Hmi.UACompiler.Chrom2VoT.dll",
                    "Siemens.Simatic.Hmi.UACompiler.ChromCache.dll",
                    "Siemens.Simatic.Hmi.UACompiler.ProtoModel.dll"
                },
                new[] { "Siemens.Simatic.Hmi.ILCompiler", "Siemens.Simatic.Hmi.UACompiler" });
            JsonUtil.Write(il, ilOut);
            Console.WriteLine($"HMI IL types={il.Assemblies.Sum(a => a.Types.Count)} -> {ilOut}");
            return 0;
        }
        if (args.Positional.Count >= 2 && args.Positional[1].Equals("objects", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Positional.Count < 3) throw new CliException("missing <plain.xml>");
            var hmiOut = args.Require("out");
            var export = ObjectExport.Read(args.Positional[2]);
            export.WriteHmiModel(hmiOut);
            Console.WriteLine($"HMI objects -> {hmiOut} (screens={export.Objects.Count(o => o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal))}, tags={export.Objects.Count(o => o.ClassName.EndsWith("HmiSystemTagData", StringComparison.Ordinal))})");
            return 0;
        }
        if (args.Positional.Count < 2 || !args.Positional[1].Equals("export", StringComparison.OrdinalIgnoreCase))
            throw new CliException("hmi needs subcommand: export|objects");
        var paths = Paths(args, 2);
        var outDir = args.Require("out");
        var result = HmiExporter.Export(paths, outDir, args.Get("instance") ?? @".\WINCC",
            args.Has("keep"), args.GetInt("max-rows", 20000), args.GetInt("timeout", 300));
        Console.WriteLine($"HMI exported to {result.OutPath}: {result.Tables.Count} tables, tags={result.Summary.VariablesTotal} (user={result.Summary.VariablesUser}), blocks={result.Summary.Blocks}, messages={result.Summary.Messages}");
        return 0;
    }

    private static int All(Args args)
    {
        var paths = Paths(args);
        var outDir = args.Require("out");
        Directory.CreateDirectory(outDir);
        var bin = args.Get("bin") ?? FileUtil.FindTiaBin();

        var ap21 = Ap21Info.Read(paths.Ap21);
        var info = ProjectInfo.Read(Path.Combine(paths.Root, "ProjectInfo.txt"));
        var plf = PlfReader.Read(paths.Plf, verifyHashes: true);
        var idx = PlfReader.ReadIndex(paths.Idx);
        PlfReader.AttachIndex(plf, idx);
        var vci = VciReader.Read(paths.VciDb, 5000);

        string? plainXml = null;
        ObjectExport? export = null;
        if (bin is not null)
        {
            var fsOut = Path.Combine(outDir, "opns", "project.objectframe.xml");
            var fs = Fs2XmlBridge.ExportPlain(paths.Plf, fsOut, bin, args.GetInt("timeout", 600));
            if (fs.Ok)
            {
                plainXml = fs.PlainPath;
                export = ObjectExport.Read(plainXml);
                plf.EnrichWith(export.ByGuid);
                export.WriteBlockSources(Path.Combine(outDir, "opns", "sources"));
                export.WriteHmiModel(Path.Combine(outDir, "hmi"));
                var render = HmiSvgRenderer.Render(export, Path.Combine(outDir, "hmi", "render"));
                JsonUtil.Write(render, Path.Combine(outDir, "hmi", "render.json"));
                var hw = HardwareExporter.Export(export, Path.Combine(outDir, "hardware"));
                JsonUtil.Write(hw, Path.Combine(outDir, "hardware", "hardware.json"));
                Console.Error.WriteLine($"hardware: {hw.Devices.Count} devices, {hw.Devices.Sum(d => d.Items.Count)} items");
                var hist = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx, Path.Combine(outDir, "history"), export, extract: false);
                JsonUtil.Write(hist, Path.Combine(outDir, "history", "history.json"));
                Console.Error.WriteLine($"history: {hist.Savepoints.Count} savepoints, {hist.Versions} versions");
                foreach (var kind in new[] { "safety", "umac", "drives" })
                {
                    var dom = DomainExporter.Export(export, kind, Path.Combine(outDir, kind));
                    JsonUtil.Write(dom, Path.Combine(outDir, kind, kind + ".json"));
                    Console.Error.WriteLine($"{kind}: {dom.Count} objects");
                }
                var sm = SimaticMlExporter.Export(export, Path.Combine(outDir, "opns", "simaticml"));
                JsonUtil.Write(sm, Path.Combine(outDir, "opns", "simaticml.json"));
                var mc7 = Mc7Extractor.Extract(export, Path.Combine(outDir, "mc7"), bin);
                Console.Error.WriteLine($"simaticml: {sm.Blocks.Count} blocks");
                Console.Error.WriteLine($"hmi render: {render.Screens.Count} screens");
                Console.Error.WriteLine($"mc7: {mc7.Blocks.Count} blocks, {mc7.Blocks.Sum(b => b.Instructions.Count)} instructions");
                var il = ApiIndexer.Index(bin,
                    new[]
                    {
                        "Siemens.Simatic.Hmi.ILCompiler.RdfModelV21.dll",
                        "Siemens.Simatic.Hmi.ILCompiler.RdfSerializationV21.dll",
                        "Siemens.Simatic.Hmi.UACompiler.Chrom2VoT.dll",
                        "Siemens.Simatic.Hmi.UACompiler.ChromCache.dll"
                    },
                    new[] { "Siemens.Simatic.Hmi.ILCompiler", "Siemens.Simatic.Hmi.UACompiler" });
                JsonUtil.Write(il, Path.Combine(outDir, "hmi", "il-api.json"));
                JsonUtil.Write(new
                {
                    export.FilePath, export.FileVersion, export.ObjectCount,
                    blocks = export.Blocks.Select(b => new
                    {
                        b.Name, b.Subtype, b.Number, b.BlockType, b.ProgrammingLanguage, b.LanguageName,
                        b.BlockGuid, b.BlockInstanceId, b.CompileUnitInstanceId, b.SourceRoot, b.SourceLength, b.SourceFile
                    }),
                    export.Warnings
                }, Path.Combine(outDir, "opns", "blocks.json"));
                Console.Error.WriteLine($"opns: {plainXml} ({new FileInfo(plainXml).Length:N0} bytes), blocks={export.Blocks.Count}");
            }
            else
            {
                Console.Error.WriteLine("opns: FS2XML export failed: " + fs.Error);
            }

            var schemas = SchemaExtractor.Extract(bin, Path.Combine(outDir, "opns", "schemas"));
            Console.Error.WriteLine($"opns: {schemas.Schemas.Count} XSD schemas");
            JsonUtil.Write(ApiIndexer.Index(bin), Path.Combine(outDir, "opns", "api.json"));
        }

        var xref = XrefReader.Read(paths.XrefDb, plf);
        File.WriteAllText(Path.Combine(outDir, "xref.dot"), xref.ToDot(), new UTF8Encoding(false));

        JsonUtil.Write(new { project = new { paths.Root, ap21, info }, plf = new { plf.Header, records = plf.Objects.Count, live = plf.LiveObjects.Count, history = plf.HistoryCount, typeNames = plf.TypeNames }, index = new { entries = idx.Entries.Count, commitPoints = idx.CommitPoints.Count } }, Path.Combine(outDir, "info.json"));
        JsonUtil.Write(new
        {
            plf.Header,
            plf.FileLength,
            records = plf.Objects.Count,
            live = plf.LiveObjects.Count,
            history = plf.HistoryCount,
            hashFailures = plf.HashFailures,
            indexGuidMismatches = plf.IndexGuidMismatches,
            typeNames = plf.TypeNames,
            typeCounts = plf.TypeCounts,
            objects = plf.Objects.Select(o => new
            {
                o.RecordIndex, o.Offset, o.Size, o.TypeId, o.TypeHex, o.InstanceId, o.ClusterId, o.Guid,
                o.Live, o.States, o.IndexFlags, o.ClassName, o.Name, o.Subtype, o.Number, o.BlockType, o.ProgrammingLanguage, o.HashValid, o.PayloadLength
            })
        }, Path.Combine(outDir, "plf.json"));
        JsonUtil.Write(xref, Path.Combine(outDir, "xref.json"));
        JsonUtil.Write(vci, Path.Combine(outDir, "vci.json"));

        if (paths.HmiBackup is not null)
        {
            try
            {
                var hmi = HmiExporter.Export(paths, Path.Combine(outDir, "hmi"), args.Get("instance") ?? @".\WINCC", keep: false, maxRowsPerTable: 20000);
                Console.Error.WriteLine($"hmi: {hmi.Tables.Count} tables, tags={hmi.Summary.VariablesTotal}, blocks={hmi.Summary.Blocks}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("hmi: " + ex.Message);
            }
        }

        var vciVerify = VciVerifier.Verify(paths.Root, paths.VciDb);
        JsonUtil.Write(vciVerify, Path.Combine(outDir, "vci-verify.json"));
        Console.Error.WriteLine($"vci verify: total={vciVerify.Total} ok={vciVerify.Ok} mismatch={vciVerify.Mismatch} missing={vciVerify.Missing}");

        Console.WriteLine($"wrote {outDir}");
        return 0;
    }
}
