using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Siemens.Simatic.Lang.CodeGenPlus.DOL.MC7plus;
using Siemens.Simatic.Lang.CodeGenPlus.DOLInternal.MC7plus;
using Siemens.Simatic.Lang.CodeGenPlus.DOLExternal.MC7plus;

internal static class Mc7Host
{
    private sealed class Instruction
    {
        public int Uid;
        public string Operator = "";
        public string Condition = "";
        public bool CondFlag;
        public bool EnoFlag;
        public bool StwFlag;
        public bool WriteFlag;
        public bool NoIntFlag;
        public bool NegatedFlag;
        public List<string> Types = new List<string>();
        public List<string> Operands = new List<string>();
    }

    private sealed class Collector : IMC7plusExchange2
    {
        public readonly List<Instruction> Instructions = new List<Instruction>();
        private Instruction _current;

        public void BeginInstruction(int uid, PlusOperator plusOperator, PlusCondition condition, bool condFlag, bool enoFlag, bool stwFlag, bool writeFlag, bool noIntFlag, bool negatedFlag)
        {
            _current = new Instruction();
            _current.Uid = uid;
            _current.Operator = plusOperator.ToString();
            _current.Condition = condition.ToString();
            _current.CondFlag = condFlag;
            _current.EnoFlag = enoFlag;
            _current.StwFlag = stwFlag;
            _current.WriteFlag = writeFlag;
            _current.NoIntFlag = noIntFlag;
            _current.NegatedFlag = negatedFlag;
            Instructions.Add(_current);
        }

        public void AddType(PlusType type)
        {
            if (_current != null) _current.Types.Add(type.ToString());
        }

        public void AddDirectNative(int uid, Scope scope, Location location, uint nr)
        {
            if (_current != null) _current.Operands.Add(string.Format("native uid={0} scope={1} location={2} nr={3}", uid, scope, location, nr));
        }

        public void AddDirectArea(int uid, Area area, uint offset)
        {
            if (_current != null) _current.Operands.Add(string.Format("area uid={0} area={1} offset={2}", uid, area, offset));
        }

        public void AddDirectDB(int uid, Area area, int dbNr, uint offset)
        {
            if (_current != null) _current.Operands.Add(string.Format("db uid={0} area={1} db={2} offset={3}", uid, area, dbNr, offset));
        }

        public void AddImmediate(int uid, PlusType type, ulong constant)
        {
            if (_current != null) _current.Operands.Add(string.Format("imm uid={0} type={1} value={2}", uid, type, constant));
        }

        public void AddImmediateLong(int uid, PlusType type, byte[] bytes)
        {
            if (_current != null) _current.Operands.Add(string.Format("imml uid={0} type={1} hex={2}", uid, type, Hex(bytes)));
        }

        public void AddIndirect(int uid, bool typeSafeFlag, bool grantedFlag, Scope ptrScope, uint ptrNr, uint offset)
        {
            if (_current != null) _current.Operands.Add(string.Format("indirect uid={0} typeSafe={1} granted={2} ptrScope={3} ptrNr={4} offset={5}", uid, typeSafeFlag, grantedFlag, ptrScope, ptrNr, offset));
        }

        public void AddIndirect(int uid, bool typeSafeFlag, bool grantedFlag, Scope ptrScope, uint ptrNr, Scope slotScope, Location slotLocation, uint slotNr)
        {
            if (_current != null) _current.Operands.Add(string.Format("indirect uid={0} typeSafe={1} granted={2} ptrScope={3} ptrNr={4} slotScope={5} slotLocation={6} slotNr={7}", uid, typeSafeFlag, grantedFlag, ptrScope, ptrNr, slotScope, slotLocation, slotNr));
        }
    }

    private static string Hex(byte[] bytes)
    {
        if (bytes == null) return "";
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string Esc(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    public static int Main(string[] args)
    {
        string bin = null, codePath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--bin" && i + 1 < args.Length) bin = args[++i];
            else if (args[i] == "--code" && i + 1 < args.Length) codePath = args[++i];
        }
        if (bin == null || codePath == null)
        {
            Console.Error.WriteLine("usage: Mc7Host --bin <tia-bin> --code <code.bin>");
            return 2;
        }

        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            var path = Path.Combine(bin, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        return Run(bin, codePath);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Run(string bin, string codePath)
    {
        try
        {
            var code = File.ReadAllBytes(codePath);
            var disassembler = new Disassembler();
            int level = -999;
            string levelError = null;
            try { level = disassembler.GetHighestLanguageLevel(code); }
            catch (Exception ex) { levelError = ex.GetType().Name + ": " + ex.Message; }
            byte[] compilerVersion = new byte[0];
            string compilerError = null;
            try { compilerVersion = disassembler.GetCompilerVersion(code); }
            catch (Exception ex) { compilerError = ex.GetType().Name + ": " + ex.Message; }
            var collector = new Collector();
            string disasmError = null;
            try { disassembler.DisassembleCallStatements(code, collector); }
            catch (Exception ex) { disasmError = ex.GetType().Name + ": " + ex.Message; }

            var sb = new StringBuilder();
            sb.Append("{\"languageLevel\":").Append(level);
            sb.Append(",\"compilerVersion\":\"").Append(Hex(compilerVersion)).Append("\""); sb.Append(",\"languageLevelError\":\"").Append(Esc(levelError)).Append("\""); sb.Append(",\"compilerError\":\"").Append(Esc(compilerError)).Append("\""); sb.Append(",\"disasmError\":\"").Append(Esc(disasmError)).Append("\"");
            sb.Append(",\"codeLength\":").Append(code.Length);
            sb.Append(",\"instructions\":[");
            for (int i = 0; i < collector.Instructions.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var ins = collector.Instructions[i];
                sb.Append("{\"uid\":").Append(ins.Uid);
                sb.Append(",\"operator\":\"").Append(Esc(ins.Operator)).Append("\"");
                sb.Append(",\"condition\":\"").Append(Esc(ins.Condition)).Append("\"");
                sb.Append(",\"condFlag\":").Append(ins.CondFlag ? "true" : "false");
                sb.Append(",\"enoFlag\":").Append(ins.EnoFlag ? "true" : "false");
                sb.Append(",\"stwFlag\":").Append(ins.StwFlag ? "true" : "false");
                sb.Append(",\"writeFlag\":").Append(ins.WriteFlag ? "true" : "false");
                sb.Append(",\"noIntFlag\":").Append(ins.NoIntFlag ? "true" : "false");
                sb.Append(",\"negatedFlag\":").Append(ins.NegatedFlag ? "true" : "false");
                sb.Append(",\"types\":[");
                for (int j = 0; j < ins.Types.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append('"').Append(Esc(ins.Types[j])).Append('"');
                }
                sb.Append("],\"operands\":[");
                for (int j = 0; j < ins.Operands.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append('"').Append(Esc(ins.Operands[j])).Append('"');
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            Console.WriteLine(sb.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }
}
