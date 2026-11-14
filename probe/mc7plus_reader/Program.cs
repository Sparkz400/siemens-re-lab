using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Siemens.Simatic.Lang.CodeGenPlus.DOL.MC7plus;
using Siemens.Simatic.Lang.CodeGenPlus.DOLExternal.MC7plus;
using Siemens.Simatic.Lang.CodeGenPlus.DOLInternal.MC7plus;
using Mc7PlusCommon;

// Probe MC7PlusBinaryReader and MC7plus.Disassembler on extracted plus-code
// sections. The immediate table is OpenImmediateData (or an alternative
// section); this tool exists to test container framing and operand decoding.
//
// The tool loads Siemens assemblies from a local TIA Portal installation at
// runtime; no Siemens binaries are distributed with this repository.
internal sealed class Collector : IMC7plusExchange2
{
    public readonly List<string> Lines = new List<string>();

    public void BeginInstruction(int uid, PlusOperator op, PlusCondition condition, bool condFlag, bool enoFlag, bool stwFlag, bool writeFlag, bool noIntFlag, bool negatedFlag)
    {
        Lines.Add(op + " cond=" + condition + " condFlag=" + condFlag + " eno=" + enoFlag + " stw=" + stwFlag + " write=" + writeFlag + " noint=" + noIntFlag + " neg=" + negatedFlag);
    }
    public void AddType(PlusType type) { }
    public void AddDirectNative(int uid, Scope scope, Location location, uint nr) { }
    public void AddDirectArea(int uid, Area area, uint offset) { }
    public void AddDirectDB(int uid, Area area, int dbNr, uint offset) { }
    public void AddImmediate(int uid, PlusType type, ulong constant) { }
    public void AddImmediateLong(int uid, PlusType type, byte[] bytes) { }
    public void AddIndirect(int uid, bool typeSafeFlag, bool grantedFlag, Scope ptrScope, uint ptrNr, uint offset) { }
    public void AddIndirect(int uid, bool typeSafeFlag, bool grantedFlag, Scope ptrScope, uint ptrNr, Scope slotScope, Location slotLocation, uint slotNr) { }
}

internal static class Program
{
    private static string _tiaBin = "";

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var local = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(local)) return Assembly.LoadFrom(local);
        if (!string.IsNullOrEmpty(_tiaBin))
        {
            var path = Path.Combine(_tiaBin, name);
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
        string codePath = null, immPath = null, label = "";
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--code") codePath = args[++i];
            else if (args[i] == "--immediate") immPath = args[++i];
            else if (args[i] == "--label") label = args[++i];
        }
        if (string.IsNullOrEmpty(codePath) || !File.Exists(codePath))
        {
            Console.WriteLine("usage: Mc7PlusReader --tia-bin <TIA_BIN> --code <section.bin> [--immediate <table.bin>] [--label <name>]");
            Environment.ExitCode = 2;
            return;
        }
        if (string.IsNullOrEmpty(_tiaBin) || !Directory.Exists(_tiaBin))
        {
            Console.WriteLine("--tia-bin must point at the TIA Portal Bin directory");
            Environment.ExitCode = 2;
            return;
        }

        var code = File.ReadAllBytes(codePath);
        var imm = immPath != null ? File.ReadAllBytes(immPath) : Array.Empty<byte>();
        Console.WriteLine("=== " + (label == "" ? Path.GetFileName(codePath) : label) + " code=" + code.Length + " immediate=" + imm.Length + " ===");

        try
        {
            var collector = new Collector();
            using (var stream = new MemoryStream(code))
            using (var reader = new BinaryReader(stream))
            {
                var decoder = new MC7PlusBinaryReader(reader, imm, collector);
                decoder.ReadAll();
            }
            Console.WriteLine("reader OK instructions=" + collector.Lines.Count);
            for (var i = 0; i < collector.Lines.Count && i < 20; i++)
                Console.WriteLine("  " + (i + 1).ToString("000") + " " + collector.Lines[i]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("reader ERROR " + ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            var collector = new Collector();
            new Disassembler().DisassembleCallStatements(code, collector);
            Console.WriteLine("disassembler OK instructions=" + collector.Lines.Count);
            for (var i = 0; i < collector.Lines.Count && i < 10; i++)
                Console.WriteLine("  d" + (i + 1).ToString("000") + " " + collector.Lines[i]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("disassembler ERROR " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
