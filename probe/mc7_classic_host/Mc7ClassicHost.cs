using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Siemens.Simatic.Lang.CodeGenClassic.DOL;
using Siemens.Simatic.PlcLanguages.BlockLogic;

// Decode a classic S7-300 MC7 code blob with TIA's AwlRegenerator.
//
// The tool loads Siemens assemblies from a local TIA Portal installation at
// runtime; no Siemens binaries are distributed with this repository.
internal static class Mc7ClassicHost
{
    private static string _tiaBin = "";

    private static readonly string[] FallbackDirs =
    {
        @"C:\Program Files\Siemens\Automation\Portal V21\Bin",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin",
        @"C:\Program Files\Siemens\Automation\Portal V19\Bin",
        @"C:\Program Files\Siemens\Automation\Portal V18\Bin"
    };

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        if (!string.IsNullOrEmpty(_tiaBin))
        {
            var primary = Path.Combine(_tiaBin, name);
            if (File.Exists(primary)) return Assembly.LoadFrom(primary);
        }
        foreach (var dir in FallbackDirs)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    private static void Main(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--tia-bin") _tiaBin = args[++i];
        }
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Run(args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        string dataPath = null;
        var start = 0;
        var length = -1;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--data") dataPath = args[++i];
            else if (args[i] == "--start") int.TryParse(args[++i], out start);
            else if (args[i] == "--length") int.TryParse(args[++i], out length);
        }

        var result = new StringBuilder();
        result.Append("{\"ok\":false");
        try
        {
            if (string.IsNullOrEmpty(dataPath) || !File.Exists(dataPath))
                throw new ArgumentException("data file not found");
            if (string.IsNullOrEmpty(_tiaBin) || !Directory.Exists(_tiaBin))
                throw new ArgumentException("--tia-bin must point at the TIA Portal Bin directory");

            var data = File.ReadAllBytes(dataPath);
            if (length < 0) length = data.Length - start;
            if (start < 0 || length <= 0 || start + length > data.Length)
                throw new ArgumentException("invalid code range " + start + "+" + length + " of " + data.Length);

            var code = new byte[length];
            Array.Copy(data, start, code, 0, length);

            var regenerator = new AwlRegenerator();
            IExpandedStatement[] statements;
            var ok = regenerator.RegenerateExpanded(code, out statements);
            if (!ok || statements == null)
                throw new InvalidOperationException("AwlRegenerator rejected the code");

            result.Clear();
            result.Append("{\"ok\":true");
            result.Append(",\"codeHex\":\"").Append(BitConverter.ToString(code).Replace("-", "").ToLowerInvariant()).Append('"');
            result.Append(",\"codeLength\":").Append(code.Length);
            result.Append(",\"statements\":[");
            for (var i = 0; i < statements.Length; i++)
            {
                if (i > 0) result.Append(',');
                var s = statements[i];
                result.Append('{');
                result.Append("\"address\":").Append(s.Address);
                result.Append(",\"length\":").Append(s.ByteLen);
                result.Append(",\"operator\":\"").Append(Escape(s.Operation.ToString())).Append('"');
                result.Append(",\"access\":\"").Append(Escape(s.Access.ToString())).Append('"');
                result.Append(",\"range\":\"").Append(Escape(s.StatementRange.ToString())).Append('"');
                result.Append(",\"width\":\"").Append(Escape(s.StatementWidth.ToString())).Append('"');
                result.Append(",\"byteNr\":").Append(s.ByteNumber);
                result.Append(",\"bitNr\":").Append(s.BitNumber);
                result.Append(",\"const\":").Append(s.ConstValue);
                result.Append('}');
            }
            result.Append("]}");
            Console.WriteLine(result.ToString());
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            result.Append(",\"error\":\"").Append(Escape(ex.GetType().Name + ": " + ex.Message)).Append("\"}");
            Console.WriteLine(result.ToString());
            Environment.ExitCode = 1;
        }
    }

    private static string Escape(string text)
    {
        if (text == null) return "";
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
