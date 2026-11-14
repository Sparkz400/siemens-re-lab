using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Download;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

// Openness probe for the PLCSIM / download experiments.
//
// Commands:
//   OpennessProbe.exe close
//   OpennessProbe.exe hold    <project.ap21>
//   OpennessProbe.exe config  <project.ap21>
//   OpennessProbe.exe subnet  <project.ap21>
//   OpennessProbe.exe download <project.ap21> <address>
//
// TIA Portal must already be running. The first call prompts for Openness
// access; choose "Yes to all" for the lab.
internal static class Program
{
    private static readonly string[] Dirs = new[]
    {
        Environment.GetEnvironmentVariable("TIA_PUBLICAPI") ?? "",
        @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48",
        @"C:\Program Files\Siemens\Automation\Portal V21\Bin\PublicAPI\V21\net48",
        @"C:\Program Files\Siemens\Automation\Portal V21\Bin\PublicAPI",
        @"C:\Program Files\Siemens\Automation\Portal V21\Bin"
    }.Where(d => !string.IsNullOrEmpty(d)).ToArray();

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        foreach (var dir in Dirs)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    private static IEnumerable<DeviceItem> AllItems(HardwareObject parent)
    {
        foreach (var item in parent.DeviceItems)
        {
            yield return item;
            foreach (var child in AllItems(item)) yield return child;
        }
    }

    private static void Main(string[] args)
    {
        var temp = Path.Combine(Path.GetTempPath(), "tia-temp");
        Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("TEMP", temp);
        Environment.SetEnvironmentVariable("TMP", temp);
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Run(args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.WriteLine("usage: OpennessProbe.exe close|hold|config|subnet|download ...");
                Environment.ExitCode = 2;
                return;
            }
            var procs = TiaPortal.GetProcesses();
            Console.WriteLine("processes=" + procs.Count);
            if (procs.Count == 0)
            {
                Console.WriteLine("TIA Portal is not running; start it first.");
                Environment.ExitCode = 2;
                return;
            }
            var tia = procs[0].Attach();
            try
            {
                switch (args[0])
                {
                    case "close":
                        CloseAll(tia);
                        break;
                    case "hold":
                        Hold(tia, args[1]);
                        break;
                    case "config":
                        WithProject(tia, args[1], project => DumpConfig(project));
                        break;
                    case "subnet":
                        WithProject(tia, args[1], project => Subnet(project));
                        break;
                    case "download":
                        WithProject(tia, args[1], project => Download(project, args.Length > 2 ? args[2] : "192.168.0.1"));
                        break;
                    default:
                        Console.WriteLine("unknown command: " + args[0]);
                        Environment.ExitCode = 2;
                        break;
                }
            }
            finally
            {
                tia.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            Environment.ExitCode = 1;
        }
    }

    private static void CloseAll(TiaPortal tia)
    {
        foreach (var project in tia.Projects.ToList())
        {
            Console.WriteLine("closing " + project.Name);
            project.Close();
        }
    }

    private static void Hold(TiaPortal tia, string path)
    {
        var project = tia.Projects.Open(new FileInfo(path));
        Console.WriteLine("opened " + project.Name);
        Console.WriteLine("ready");
        var stop = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "opnshold.stop");
        if (File.Exists(stop)) File.Delete(stop);
        for (var i = 0; i < 600 && !File.Exists(stop); i++) System.Threading.Thread.Sleep(1000);
        project.Close();
        Console.WriteLine("closed");
    }

    private static void WithProject(TiaPortal tia, string path, Action<Project> action)
    {
        var project = tia.Projects.Open(new FileInfo(path));
        Console.WriteLine("opened " + project.Name);
        try
        {
            action(project);
        }
        finally
        {
            project.Close();
        }
    }

    private static DeviceItem FindCpu(Project project)
    {
        foreach (var device in project.Devices)
        {
            foreach (var item in AllItems(device))
            {
                if (item.GetService<DownloadProvider>() != null) return item;
            }
        }
        throw new InvalidOperationException("no CPU with DownloadProvider found");
    }

    private static void DumpConfig(Project project)
    {
        var cpu = FindCpu(project);
        var dp = cpu.GetService<DownloadProvider>();
        var config = dp.Configuration;
        Console.WriteLine("cpu=" + cpu.Name + " configured=" + config.IsConfigured + " modes=" + config.Modes.Count);
        foreach (var mode in config.Modes)
        {
            Console.WriteLine("mode " + mode.Name + " pcInterfaces=" + mode.PcInterfaces.Count);
            foreach (var pc in mode.PcInterfaces)
            {
                Console.WriteLine("  pc " + pc.Name + " number=" + pc.Number + " addresses=" + pc.Addresses.Count + " targets=" + pc.TargetInterfaces.Count);
                foreach (var address in pc.Addresses) Console.WriteLine("    address " + address.Name + " = " + address.Address);
                foreach (var target in pc.TargetInterfaces)
                {
                    Console.WriteLine("    target " + target.Name + " addresses=" + target.Addresses.Count);
                    foreach (var address in target.Addresses) Console.WriteLine("      address " + address.Name + " = " + address.Address);
                }
            }
        }
    }

    private static void Subnet(Project project)
    {
        var device = project.Devices.First();
        var iface = AllItems(device).First(i => i.Name == "PROFINET interface_1");
        var ni = iface.GetService<NetworkInterface>();
        if (ni == null) throw new InvalidOperationException("no NetworkInterface on PROFINET interface_1");
        Console.WriteLine("nodes=" + ni.Nodes.Count);
        var node = ni.Nodes[0];
        Console.WriteLine("node=" + node.Name + " connectedSubnet=" + (node.ConnectedSubnet == null ? "null" : node.ConnectedSubnet.Name));
        if (node.ConnectedSubnet == null)
        {
            var subnet = node.CreateAndConnectToSubnet("PROFINET_1");
            Console.WriteLine("created+connected subnet=" + subnet.Name + " netType=" + subnet.NetType);
        }
        foreach (var pair in new[]
                 {
                     new KeyValuePair<string, object>("Address", "192.168.0.1"),
                     new KeyValuePair<string, object>("SubnetMask", "255.255.255.0")
                 })
        {
            if (node.GetAttributeInfos().Any(i => i.Name == pair.Key))
            {
                node.SetAttribute(pair.Key, pair.Value);
                Console.WriteLine("SET " + pair.Key + "=" + pair.Value);
            }
        }
        project.Save();
        Console.WriteLine("saved");
    }

    private static void Download(Project project, string address)
    {
        var cpu = FindCpu(project);
        var dp = cpu.GetService<DownloadProvider>();
        var config = dp.Configuration;
        var mode = config.Modes.First();
        var pc = mode.PcInterfaces.First();
        var target = pc.TargetInterfaces.First();
        Console.WriteLine("target=" + target.Name + " addresses=" + target.Addresses.Count);
        var addr = target.Addresses.Create(address);
        Console.WriteLine("address=" + addr.Name + " value=" + addr.Address);
        var result = dp.Download(
            target,
            addr,
            cfg => Console.WriteLine("pre: " + cfg.Message),
            cfg => Console.WriteLine("post: " + cfg.Message),
            DownloadOptions.Software);
        Console.WriteLine("state=" + result.State + " errors=" + result.ErrorCount + " warnings=" + result.WarningCount);
        DumpMessages(result.Messages, 0);
    }

    private static void DumpMessages(DownloadResultMessageComposition messages, int depth)
    {
        foreach (var message in messages)
        {
            Console.WriteLine(new string(' ', depth * 2) + "[" + message.State + "] err=" + message.ErrorCount + " warn=" + message.WarningCount + " msg='" + message.Message + "'");
            DumpMessages(message.Messages, depth + 1);
        }
    }
}
