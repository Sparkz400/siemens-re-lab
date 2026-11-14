using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TiaProject;

public sealed class RecordedSampleRow
{
    public string Timestamp { get; set; } = "";
    public int? Savepoint { get; set; }
    public Dictionary<string, double> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RecordedAlarmRow
{
    public string Timestamp { get; set; } = "";
    public int? Savepoint { get; set; }
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "high";
    public string Message { get; set; } = "";
    public string? Tag { get; set; }
}

public sealed class RecordedIngestResult
{
    public bool Pass { get; set; }
    public FlightTrace Trace { get; set; } = new();
    public List<string> Errors { get; set; } = [];
    public int Samples { get; set; }
    public int Alarms { get; set; }
    public int Tags { get; set; }
    public int Savepoints { get; set; }
}

public sealed class RecordedTraceSelfTestResult
{
    public bool Pass { get; set; }
    public FlightTrace Trace { get; set; } = new();
    public FlightReplayResult Replay { get; set; } = new();
    public List<string> Checks { get; set; } = [];
    public List<string> NegativeChecks { get; set; } = [];
}

public sealed class RecordedEvolutionResult
{
    public bool Pass { get; set; }
    public string Block { get; set; } = "";
    public string Mode { get; set; } = "";
    public int Samples { get; set; }
    public double BaselineFitness { get; set; }
    public int BaselineMatched { get; set; }
    public int BaselineTotal { get; set; }
    public double MutatedFitness { get; set; }
    public int MutatedMatched { get; set; }
    public string? Mutation { get; set; }
    public double BestFitness { get; set; }
    public List<string> Checks { get; set; } = [];
}

public static class RecordedCsv
{
    public static List<string[]> Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows.Where(r => r.Length > 0 && !(r.Length == 1 && string.IsNullOrWhiteSpace(r[0]))).ToList();
    }

    public static string Join(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(f =>
            f.Contains(',') || f.Contains('"') || f.Contains('\n')
                ? "\"" + f.Replace("\"", "\"\"") + "\""
                : f));

    public static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseIso(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

public static class RecordedTraceIngest
{
    public static RecordedIngestResult Ingest(ObjectExport export, HistoryResult history,
        string samplesPath, string alarmsPath, string savepointTimesPath)
    {
        var result = new RecordedIngestResult();
        var knownTags = export.Objects
            .Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .Select(o => o.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var saveTimes = ReadSavepointTimes(savepointTimesPath);
        var states = SavepointStateBuilder.Build(history);

        List<RecordedSampleRow> samples;
        List<RecordedAlarmRow> alarms;
        try
        {
            samples = ReadSamples(samplesPath, knownTags);
            alarms = ReadAlarms(alarmsPath, knownTags);
        }
        catch (CliException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }

        if (saveTimes.Count == 0)
        {
            result.Errors.Add("savepoint times are empty");
            return result;
        }
        var orderedTimes = saveTimes.OrderBy(kv => kv.Value).ToList();

        var trace = new FlightTrace
        {
            Name = "recorded-io",
            Project = export.FilePath,
            Source = "recorded-csv",
            RecordedAt = DateTimeOffset.UtcNow.ToString("O"),
            SamplesFile = Path.GetFullPath(samplesPath),
            AlarmsFile = Path.GetFullPath(alarmsPath),
            SavepointTimesFile = Path.GetFullPath(savepointTimesPath),
            SavepointStates = states.ToDictionary(s => s.Index, s => s.StateId)
        };

        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var timestamp = RecordedCsv.ParseIso(sample.Timestamp);
            var bound = Bind(timestamp, orderedTimes);
            if (bound is null)
            {
                result.Errors.Add($"sample at {sample.Timestamp} precedes the first savepoint at {orderedTimes[0].Value:O}");
                return result;
            }
            if (sample.Savepoint is int explicitSp && explicitSp != bound.Value)
            {
                result.Errors.Add($"sample at {sample.Timestamp} declares savepoint {explicitSp} but time binds to {bound.Value}");
                return result;
            }
            trace.Samples.Add(new TraceSample
            {
                T = i,
                Timestamp = RecordedCsv.Iso(timestamp),
                Savepoint = bound.Value,
                Source = "recorded-csv",
                Values = new Dictionary<string, double>(sample.Values, StringComparer.OrdinalIgnoreCase)
            });
        }

        for (int i = 0; i < alarms.Count; i++)
        {
            var alarm = alarms[i];
            var timestamp = RecordedCsv.ParseIso(alarm.Timestamp);
            var bound = Bind(timestamp, orderedTimes);
            if (bound is null)
            {
                result.Errors.Add($"alarm {alarm.Code} at {alarm.Timestamp} precedes the first savepoint");
                return result;
            }
            if (alarm.Savepoint is int explicitSp && explicitSp != bound.Value)
            {
                result.Errors.Add($"alarm {alarm.Code} declares savepoint {explicitSp} but time binds to {bound.Value}");
                return result;
            }
            trace.Alarms.Add(new TraceAlarm
            {
                T = i,
                Timestamp = RecordedCsv.Iso(timestamp),
                Savepoint = bound.Value,
                Code = alarm.Code,
                Severity = alarm.Severity,
                Message = alarm.Message,
                Tag = alarm.Tag,
                Source = "recorded-csv"
            });
        }

        trace.Tags = trace.Samples.SelectMany(s => s.Values.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        trace.IngestHash = IrExporter.Hash(JsonSerializer.Serialize(new
        {
            trace.Samples,
            trace.Alarms,
            saveTimes = saveTimes.OrderBy(kv => kv.Key).ToList()
        }, JsonUtil.Options));
        result.Trace = trace;
        result.Samples = trace.Samples.Count;
        result.Alarms = trace.Alarms.Count;
        result.Tags = trace.Tags.Count;
        result.Savepoints = saveTimes.Count;
        result.Pass = result.Errors.Count == 0 && result.Samples > 0 && result.Savepoints > 0;
        return result;
    }

    public static Dictionary<int, DateTimeOffset> ReadSavepointTimes(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<int, DateTimeOffset>();
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("savepoints", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                map[index] = RecordedCsv.ParseIso(item.GetProperty("time").GetString()!);
            }
            return map;
        }
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (int.TryParse(property.Name, out var index))
                map[index] = RecordedCsv.ParseIso(property.Value.GetString()!);
        }
        return map;
    }

    private static int? Bind(DateTimeOffset timestamp, List<KeyValuePair<int, DateTimeOffset>> ordered)
    {
        int? bound = null;
        foreach (var kv in ordered)
        {
            if (kv.Value <= timestamp) bound = kv.Key;
            else break;
        }
        return bound;
    }

    private static List<RecordedSampleRow> ReadSamples(string path, HashSet<string> knownTags)
    {
        var rows = RecordedCsv.Read(path);
        if (rows.Count < 2) throw new CliException("samples CSV has no data rows");
        var header = rows[0].Select(h => h.Trim()).ToArray();
        if (!header[0].Equals("timestamp", StringComparison.OrdinalIgnoreCase))
            throw new CliException("samples CSV must start with a timestamp column");
        var longFormat = header.Length >= 3
                         && header[1].Equals("tag", StringComparison.OrdinalIgnoreCase)
                         && header[2].Equals("value", StringComparison.OrdinalIgnoreCase);
        var samples = new List<RecordedSampleRow>();
        DateTimeOffset? previous = null;
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Length != header.Length)
                throw new CliException($"samples CSV row {r + 1} has {row.Length} columns, expected {header.Length}");
            var timestamp = ParseTimestamp(row[0], r);
            if (previous is not null && timestamp < previous.Value)
                throw new CliException($"samples CSV is not monotonic at row {r + 1}: {row[0]} < {previous.Value:O}");
            previous = timestamp;
            var sample = new RecordedSampleRow { Timestamp = row[0] };
            if (longFormat)
            {
                var tag = row[1].Trim();
                RequireKnownTag(tag, knownTags, r);
                sample.Values[tag] = ParseValue(row[2], r);
            }
            else
            {
                for (int c = 1; c < header.Length; c++)
                {
                    var tag = header[c].Trim();
                    RequireKnownTag(tag, knownTags, r);
                    sample.Values[tag] = ParseValue(row[c], r);
                }
            }
            var savepointColumn = Array.FindIndex(header, h => h.Equals("savepoint", StringComparison.OrdinalIgnoreCase));
            if (savepointColumn >= 0 && int.TryParse(row[savepointColumn], out var savepoint))
                sample.Savepoint = savepoint;
            samples.Add(sample);
        }
        return samples;
    }

    private static List<RecordedAlarmRow> ReadAlarms(string path, HashSet<string> knownTags)
    {
        var rows = RecordedCsv.Read(path);
        if (rows.Count < 2) throw new CliException("alarms CSV has no data rows");
        var header = rows[0].Select(h => h.Trim()).ToArray();
        string[] required = ["timestamp", "code", "severity", "message"];
        foreach (var name in required)
            if (!header.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new CliException("alarms CSV missing column: " + name);
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        var alarms = new List<RecordedAlarmRow>();
        DateTimeOffset? previous = null;
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var timestamp = ParseTimestamp(row[Col("timestamp")], r);
            if (previous is not null && timestamp < previous.Value)
                throw new CliException($"alarms CSV is not monotonic at row {r + 1}");
            previous = timestamp;
            var severity = row[Col("severity")].Trim().ToLowerInvariant();
            if (severity is not ("low" or "medium" or "high" or "critical"))
                throw new CliException($"alarms CSV row {r + 1} has invalid severity: {severity}");
            var code = row[Col("code")].Trim();
            var message = row[Col("message")].Trim();
            if (code.Length == 0 || message.Length == 0)
                throw new CliException($"alarms CSV row {r + 1} has an empty code or message");
            var tagCol = Col("tag");
            var tag = tagCol >= 0 ? row[tagCol].Trim() : null;
            if (!string.IsNullOrEmpty(tag)) RequireKnownTag(tag, knownTags, r);
            var alarm = new RecordedAlarmRow
            {
                Timestamp = row[Col("timestamp")],
                Code = code,
                Severity = severity,
                Message = message,
                Tag = string.IsNullOrEmpty(tag) ? null : tag
            };
            var savepointCol = Col("savepoint");
            if (savepointCol >= 0 && int.TryParse(row[savepointCol], out var savepoint))
                alarm.Savepoint = savepoint;
            alarms.Add(alarm);
        }
        return alarms;
    }

    private static void RequireKnownTag(string tag, HashSet<string> knownTags, int row)
    {
        if (!knownTags.Contains(tag))
            throw new CliException($"unknown tag '{tag}' at row {row + 1}");
    }

    private static DateTimeOffset ParseTimestamp(string text, int row)
    {
        try
        {
            return RecordedCsv.ParseIso(text);
        }
        catch (FormatException)
        {
            throw new CliException($"invalid timestamp at row {row + 1}: {text}");
        }
    }

    private static double ParseValue(string text, int row)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value) || double.IsInfinity(value))
            throw new CliException($"invalid numeric value at row {row + 1}: {text}");
        return value;
    }
}

public static class RecordedTraceFixture
{
    public static string WriteSavepointTimes(string path, int savepoints, DateTimeOffset baseTime)
    {
        var map = new Dictionary<string, string>();
        for (int i = 0; i < savepoints; i++)
            map[i.ToString(CultureInfo.InvariantCulture)] = RecordedCsv.Iso(baseTime.AddSeconds(i * 100));
        File.WriteAllText(path, JsonSerializer.Serialize(map, JsonUtil.Options), new UTF8Encoding(false));
        return path;
    }

    public static string WriteSamples(string path, IReadOnlyList<string> tags,
        Func<int, int, double> value, int savepoints, DateTimeOffset baseTime)
    {
        var sb = new StringBuilder();
        sb.AppendLine(RecordedCsv.Join(["timestamp", .. tags]));
        for (int sp = 0; sp < savepoints; sp++)
        {
            var time = RecordedCsv.Iso(baseTime.AddSeconds(sp * 100 + 10));
            var values = tags.Select((_, index) => value(sp, index).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(RecordedCsv.Join([time, .. values]));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    public static string WriteAlarms(string path, int savepoints, DateTimeOffset baseTime, string? tag)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,code,severity,message,tag");
        for (int sp = 0; sp < savepoints; sp++)
        {
            var time = RecordedCsv.Iso(baseTime.AddSeconds(sp * 100 + 50));
            var code = sp == 5 ? "1000000" : "1000004";
            var message = sp == 5
                ? "WCCRT:@100%s@:Error"
                : "WCCRT:@100%s@:Tag value of the high limit is exceeded";
            sb.AppendLine(RecordedCsv.Join([time, code, "high", message, tag ?? ""]));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }
}

public static class RecordedTraceSelfTest
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 13, 14, 46, 0, TimeSpan.Zero);

    public static RecordedTraceSelfTestResult Run(HistoryResult history, ObjectExport export, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var tags = export.Objects
            .Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .Select(o => o.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (tags.Count < 2) throw new CliException("recorded trace fixture needs at least two tags");
        var savepoints = Math.Max(2, history.Savepoints.Count);

        var samplesPath = Path.Combine(outDir, "io.csv");
        var alarmsPath = Path.Combine(outDir, "alarms.csv");
        var timesPath = Path.Combine(outDir, "savepoint-times.json");
        RecordedTraceFixture.WriteSavepointTimes(timesPath, savepoints, BaseTime);
        RecordedTraceFixture.WriteSamples(samplesPath, tags,
            (sp, index) => sp, savepoints, BaseTime);
        RecordedTraceFixture.WriteAlarms(alarmsPath, savepoints, BaseTime, tags[0]);

        var ingest = RecordedTraceIngest.Ingest(export, history, samplesPath, alarmsPath, timesPath);
        var rules = new RuleSet { Rules = [new FlightRule { Name = "tag1-high", Expr = tags[0] + " >= 5", Severity = "high" }] };
        var replay = FlightRecorder.Replay(ingest.Trace, rules, history, export);

        var result = new RecordedTraceSelfTestResult
        {
            Trace = ingest.Trace,
            Replay = replay
        };
        var atExpected = replay.Findings.Count(f => f.Savepoint == 5);
        var state0 = replay.SavepointStates.GetValueOrDefault(0);
        var state5 = replay.SavepointStates.GetValueOrDefault(5);
        result.Checks.Add($"ingest.pass={ingest.Pass}");
        result.Checks.Add($"samples={ingest.Samples}");
        result.Checks.Add($"alarms={ingest.Alarms}");
        result.Checks.Add($"tags={ingest.Tags}");
        result.Checks.Add($"savepoints={ingest.Savepoints}");
        result.Checks.Add($"timestamped.samples={replay.TimestampedSamples}");
        result.Checks.Add($"timestamped.alarms={replay.TimestampedAlarms}");
        result.Checks.Add($"violations={replay.Violations}");
        result.Checks.Add($"at-savepoint-5={atExpected}");
        result.Checks.Add($"attributed={replay.AttributedViolations}");
        result.Checks.Add($"state-mismatches={replay.StateMismatches}");
        result.Checks.Add($"unknown-savepoints={replay.UnknownSavepoints}");
        result.Checks.Add($"state-0={state0}");
        result.Checks.Add($"state-5={state5}");
        result.Checks.Add($"states-differ={state0 != state5}");
        result.Checks.Add($"ingest-hash={ingest.Trace.IngestHash}");

        var negative = new List<string>();
        var negativeDir = Path.Combine(outDir, "negative");
        Directory.CreateDirectory(negativeDir);

        void ExpectFailure(string name, Func<RecordedIngestResult> action, string expected)
        {
            var ingestResult = action();
            var error = string.Join("; ", ingestResult.Errors);
            var hit = !ingestResult.Pass && error.Contains(expected, StringComparison.OrdinalIgnoreCase);
            negative.Add($"{name}={hit} expected={expected} actual={error}");
        }

        var unknownTagPath = Path.Combine(negativeDir, "unknown-tag.csv");
        File.WriteAllText(unknownTagPath, "timestamp,tag,value\n2026-11-13T14:46:10.000Z,Bogus_Tag,1\n");
        ExpectFailure("unknown-tag", () =>
            RecordedTraceIngest.Ingest(export, history, unknownTagPath, alarmsPath, timesPath), "unknown tag");

        var nonMonotonicPath = Path.Combine(negativeDir, "non-monotonic.csv");
        File.WriteAllText(nonMonotonicPath,
            $"timestamp,tag,value\n2026-11-13T14:46:20.000Z,{tags[0]},1\n2026-11-13T14:46:10.000Z,{tags[0]},2\n");
        ExpectFailure("non-monotonic", () =>
            RecordedTraceIngest.Ingest(export, history, nonMonotonicPath, alarmsPath, timesPath), "not monotonic");

        var beforePath = Path.Combine(negativeDir, "before-first.csv");
        File.WriteAllText(beforePath,
            $"timestamp,tag,value\n2026-11-13T13:00:00.000Z,{tags[0]},1\n");
        ExpectFailure("before-first-savepoint", () =>
            RecordedTraceIngest.Ingest(export, history, beforePath, alarmsPath, timesPath), "precedes");

        var alarmUnknownPath = Path.Combine(negativeDir, "alarm-unknown-tag.csv");
        File.WriteAllText(alarmUnknownPath,
            "timestamp,code,severity,message,tag\n2026-11-13T14:46:50.000Z,1000004,high,limit exceeded,Bogus_Tag\n");
        ExpectFailure("alarm-unknown-tag", () =>
            RecordedTraceIngest.Ingest(export, history, samplesPath, alarmUnknownPath, timesPath), "unknown tag");

        var tampered = JsonSerializer.Deserialize<FlightTrace>(
            JsonSerializer.Serialize(ingest.Trace, JsonUtil.Options), JsonUtil.Options)!;
        tampered.SavepointStates[3] = new string('0', 64);
        var tamperedReplay = FlightRecorder.Replay(tampered, rules, history, export);
        negative.Add($"tampered-state.mismatches={tamperedReplay.StateMismatches > 0}");
        negative.Add($"tampered-state.reason={tamperedReplay.Findings.Any(f => f.Reason == "savepoint-state-mismatch")}");

        var unknownSavepoint = JsonSerializer.Deserialize<FlightTrace>(
            JsonSerializer.Serialize(ingest.Trace, JsonUtil.Options), JsonUtil.Options)!;
        unknownSavepoint.Alarms[0].Savepoint = 99;
        var unknownReplay = FlightRecorder.Replay(unknownSavepoint, rules, history, export);
        negative.Add($"unknown-alarm-savepoint={unknownReplay.UnknownSavepoints > 0}");

        result.NegativeChecks = negative;
        result.Pass = ingest.Pass
                      && ingest.Samples >= savepoints
                      && ingest.Alarms == savepoints
                      && replay.TimestampedSamples == ingest.Samples
                      && replay.TimestampedAlarms == ingest.Alarms
                      && atExpected > 0
                      && replay.AttributedViolations == replay.Violations
                      && replay.StateMismatches == 0
                      && replay.UnknownSavepoints == 0
                      && state0 != state5
                      && negative.All(n => n.Contains("=True") || n.Contains("=true"))
                      && negative.Any(n => n.StartsWith("unknown-tag=", StringComparison.Ordinal))
                      && negative.Any(n => n.StartsWith("non-monotonic=", StringComparison.Ordinal))
                      && negative.Any(n => n.StartsWith("before-first-savepoint=", StringComparison.Ordinal))
                      && negative.Any(n => n.StartsWith("alarm-unknown-tag=", StringComparison.Ordinal))
                      && negative.Any(n => n.StartsWith("tampered-state.mismatches=", StringComparison.Ordinal))
                      && negative.Any(n => n.StartsWith("unknown-alarm-savepoint=", StringComparison.Ordinal));

        JsonUtil.Write(result.Trace, Path.Combine(outDir, "recorded-trace.json"));
        JsonUtil.Write(replay, Path.Combine(outDir, "replay.json"));
        JsonUtil.Write(result, Path.Combine(outDir, "recorded-test.json"));
        return result;
    }
}

public static class RecordedEvolution
{
    public static IoTrace ToIoTrace(FlightTrace trace, string block, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
    {
        var io = new IoTrace
        {
            Name = trace.Name + ":" + block,
            Block = block,
            Source = trace.Source
        };
        var inputBindings = inputs.Select(Binding).ToList();
        var outputBindings = outputs.Select(Binding).ToList();
        foreach (var sample in trace.Samples)
        {
            var inputValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in inputBindings)
            {
                if (!sample.Values.TryGetValue(binding.Tag, out var value))
                    throw new CliException($"recorded sample {sample.Timestamp ?? sample.T.ToString()} is missing input tag '{binding.Tag}'");
                inputValues[binding.Symbol] = value;
            }
            var expected = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in outputBindings)
            {
                if (!sample.Values.TryGetValue(binding.Tag, out var value))
                    throw new CliException($"recorded sample {sample.Timestamp ?? sample.T.ToString()} is missing output tag '{binding.Tag}'");
                expected[binding.Symbol] = value;
            }
            io.Samples.Add(new IoTraceSample { T = sample.T, Inputs = inputValues, Expected = expected });
        }
        return io;
    }

    private static (string Tag, string Symbol) Binding(string spec)
    {
        var parts = spec.Split('=', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : parts[0]);
    }

    public static RecordedEvolutionResult RunSynthetic(FlightTrace trace, string input, string output, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var io = ToIoTrace(trace, "SyntheticBuffer", [input], [output]);
        var body = SyntheticBufferBody(output, input, negate: false);
        var baseline = EvolveEngine.Score(body, io);
        var mutated = new XElement(body);
        var mutation = EvolveEngine.Mutate(mutated, 0);
        var mutatedScore = EvolveEngine.Score(mutated, io);
        var run = EvolveEngine.Run(body, "SyntheticBuffer", io, 8);
        var result = new RecordedEvolutionResult
        {
            Block = "SyntheticBuffer",
            Mode = "synthetic",
            Samples = io.Samples.Count,
            BaselineFitness = baseline.Fitness,
            BaselineMatched = baseline.Matched,
            BaselineTotal = baseline.Total,
            MutatedFitness = mutatedScore.Fitness,
            MutatedMatched = mutatedScore.Matched,
            Mutation = mutation,
            BestFitness = run.BestFitness
        };
        result.Checks.Add($"mode={result.Mode}");
        result.Checks.Add($"samples={result.Samples}");
        result.Checks.Add($"baseline={result.BaselineFitness:F2}");
        result.Checks.Add($"baseline.matched={result.BaselineMatched}/{result.BaselineTotal}");
        result.Checks.Add($"mutated={result.MutatedFitness:F2}");
        result.Checks.Add($"mutated.matched={result.MutatedMatched}/{result.BaselineTotal}");
        result.Checks.Add($"mutation={result.Mutation}");
        result.Checks.Add($"best={result.BestFitness:F2}");
        result.Pass = result.Samples >= 2
                      && Math.Abs(result.BaselineFitness - 1.0) < 1e-9
                      && result.BaselineMatched == result.BaselineTotal
                      && result.MutatedFitness < result.BaselineFitness
                      && result.BestFitness >= result.BaselineFitness;
        JsonUtil.Write(io, Path.Combine(outDir, "recorded-io-trace.json"));
        JsonUtil.Write(result, Path.Combine(outDir, "recorded-evolve.json"));
        return result;
    }

    public static RecordedEvolutionResult RunReal(ObjectExport export, FlightTrace trace, string blockName,
        IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var io = ToIoTrace(trace, blockName, inputs, outputs);
        var block = export.Objects.FirstOrDefault(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
            && string.Equals(o.Name, blockName, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException("block not found: " + blockName);
        var body = PlatformIo.Body(block, export) ?? throw new CliException("block has no body: " + blockName);
        var baseline = EvolveEngine.Score(body, io);
        var mutated = new XElement(body);
        var mutation = EvolveEngine.Mutate(mutated, 0);
        var mutatedScore = EvolveEngine.Score(mutated, io);
        var run = EvolveEngine.Run(export, blockName, io, 8);
        var result = new RecordedEvolutionResult
        {
            Block = blockName,
            Mode = "real",
            Samples = io.Samples.Count,
            BaselineFitness = baseline.Fitness,
            BaselineMatched = baseline.Matched,
            BaselineTotal = baseline.Total,
            MutatedFitness = mutatedScore.Fitness,
            MutatedMatched = mutatedScore.Matched,
            Mutation = mutation,
            BestFitness = run.BestFitness
        };
        result.Checks.Add($"mode={result.Mode}");
        result.Checks.Add($"block={result.Block}");
        result.Checks.Add($"inputs={string.Join(",", inputs)}");
        result.Checks.Add($"outputs={string.Join(",", outputs)}");
        result.Checks.Add($"samples={result.Samples}");
        result.Checks.Add($"baseline={result.BaselineFitness:F2}");
        result.Checks.Add($"baseline.matched={result.BaselineMatched}/{result.BaselineTotal}");
        result.Checks.Add($"mutated={result.MutatedFitness:F2}");
        result.Checks.Add($"mutated.matched={result.MutatedMatched}/{result.BaselineTotal}");
        result.Checks.Add($"mutation={result.Mutation}");
        result.Checks.Add($"best={result.BestFitness:F2}");
        result.Pass = result.Samples >= 2
                      && Math.Abs(result.BaselineFitness - 1.0) < 1e-9
                      && result.BaselineMatched == result.BaselineTotal
                      && result.MutatedFitness < result.BaselineFitness
                      && result.BestFitness >= result.BaselineFitness;
        JsonUtil.Write(io, Path.Combine(outDir, "recorded-io-trace.json"));
        JsonUtil.Write(result, Path.Combine(outDir, "recorded-evolve.json"));
        return result;
    }

    public static FlightTrace CreateRealLogicTrace(string name, IReadOnlyList<string> savepointTags,
        IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, int savepoints, DateTimeOffset baseTime)
    {
        var trace = new FlightTrace
        {
            Name = name,
            Source = "recorded-fixture",
            RecordedAt = DateTimeOffset.UtcNow.ToString("O"),
            Tags = [.. inputs, .. outputs]
        };
        var combinations = new (double Start, double Stop)[]
        {
            (0, 0), (0, 1), (1, 0), (1, 1)
        };
        for (int sp = 0; sp < savepoints; sp++)
        {
            var combo = combinations[sp % combinations.Length];
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < inputs.Count; i++)
                values[inputs[i]] = i == 0 ? combo.Start : combo.Stop;
            values[outputs[0]] = values[inputs[0]] != 0 && values[inputs[1]] == 0 ? 1 : 0;
            values[outputs[1]] = values[inputs[1]];
            trace.Samples.Add(new TraceSample
            {
                T = sp,
                Timestamp = RecordedCsv.Iso(baseTime.AddSeconds(sp * 100 + 10)),
                Savepoint = sp,
                Source = "recorded-fixture",
                Values = values
            });
            trace.Alarms.Add(new TraceAlarm
            {
                T = sp,
                Timestamp = RecordedCsv.Iso(baseTime.AddSeconds(sp * 100 + 50)),
                Savepoint = sp,
                Code = "1000004",
                Severity = "high",
                Message = "WCCRT:@100%s@:Tag value of the high limit is exceeded",
                Tag = savepointTags.FirstOrDefault(),
                Source = "recorded-fixture"
            });
        }
        return trace;
    }

    private static XElement SyntheticBufferBody(string output, string input, bool negate) => XElement.Parse($"""
        <SCLSource Version="3.4.0.0">
          <Symbols>
            <Unknown SymID="1" RefId="1" />
            <Unknown SymID="2" RefId="2" />
          </Symbols>
          <RootStatements Version="3.4.0.0">
            <Statement UId="1" SI="STAss">
              <SymVa UId="2" SI="Var" SyId="1" ODN="{output}" />
              <Token UId="3" TE=":=" />
              <Expression UId="4" SI="ExprPrimD">
                {(negate ? "<Token UId=\"5\" TE=\"NOT\" />" : "")}
                <SymVa UId="6" SI="Var" SyId="2" ODN="{input}" />
              </Expression>
              <EToken UId="7" />
            </Statement>
          </RootStatements>
        </SCLSource>
        """);
}
