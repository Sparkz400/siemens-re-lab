using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private static string s_Bin = Environment.GetEnvironmentVariable("TIA_BIN")
        ?? @"D:\Arbeit\Siemens\Portal V21\Bin";

    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        string familyKeyHex = GetArg(args, "--family-key");
        string seedHex = GetArg(args, "--seed");
        string wantPublic = GetArg(args, "--public-id");
        string wantTarget = GetArg(args, "--target-id");
        if (familyKeyHex == null || seedHex == null)
        {
            Console.Error.WriteLine("usage: mc7plus-keys --family-key <hex> --seed <hex> [--public-id <hex>] [--target-id <hex>]");
            Environment.Exit(2);
        }

        Assembly asm = Assembly.LoadFrom(Path.Combine(s_Bin, "Siemens.Simatic.Lang.MC7Codegenerator.dll"));
        Type kkType = asm.GetType("Siemens.Simatic.Lang.CodeGenPlus.DOL.MC7plus.KeyKeeper", true);
        object kk = Activator.CreateInstance(kkType);
        MethodInfo addKey = kkType.GetMethod("AddKey");
        MethodInfo genKey = kkType.GetMethod("GenKey");

        object publicId = addKey.Invoke(kk, new object[] { FromHex(familyKeyHex) });
        object targetId = genKey.Invoke(kk, new object[] { FromHex(seedHex), publicId });

        string pub = ToHex(GetIdBytes(publicId));
        string tgt = ToHex(GetIdBytes(targetId));
        Console.WriteLine("publicId=" + pub);
        Console.WriteLine("targetId=" + tgt);
        if (wantPublic != null)
            Console.WriteLine("publicIdMatch=" + string.Equals(pub, Normalize(wantPublic), StringComparison.OrdinalIgnoreCase));
        if (wantTarget != null)
            Console.WriteLine("targetIdMatch=" + string.Equals(tgt, Normalize(wantTarget), StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] GetIdBytes(object id)
    {
        Type t = id.GetType();
        FieldInfo idField = t.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo flagsField = t.GetField("flags", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo internalField = t.GetField("flags_internal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        ulong value = (ulong)idField.GetValue(id);
        uint flags = (uint)flagsField.GetValue(id);
        uint flagsInternal = (uint)internalField.GetValue(id);
        byte[] b = new byte[16];
        BitConverter.GetBytes(value).CopyTo(b, 0);
        BitConverter.GetBytes(flags).CopyTo(b, 8);
        BitConverter.GetBytes(flagsInternal).CopyTo(b, 12);
        return b;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        string name = new AssemblyName(e.Name).Name + ".dll";
        string path = Path.Combine(s_Bin, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static string GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    private static string Normalize(string s)
    {
        return s.Replace(" ", "").Replace("-", "").ToLowerInvariant();
    }

    private static byte[] FromHex(string s)
    {
        s = Normalize(s);
        byte[] b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    private static string ToHex(byte[] b)
    {
        return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
    }
}
