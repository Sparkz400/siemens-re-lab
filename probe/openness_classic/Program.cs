using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;

// Create and compile the classic S7-300 PlantE lab project through Openness.
//
// Usage:
//   OpennessClassic.exe <project-root> <project-name> <PlantE.scl>
//
// TIA Portal must already be running. The first Openness call prompts for
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
            var procs = TiaPortal.GetProcesses();
            Console.WriteLine("processes=" + procs.Count);
            if (procs.Count == 0)
            {
                Console.WriteLine("TIA Portal is not running; start it first.");
                Environment.ExitCode = 2;
                return;
            }
            using (var tia = procs[0].Attach())
            {
                var root = new DirectoryInfo(args[0]);
                var name = args[1];
                var scl = args[2];
                var target = new DirectoryInfo(Path.Combine(root.FullName, name));
                if (target.Exists) Directory.Delete(target.FullName, true);
                var project = tia.Projects.Create(root, name);
                Console.WriteLine("created " + project.Name);
                var device = project.Devices.CreateWithItem(
                    "OrderNumber:6ES7 315-2EH14-0AB0/V3.2",
                    "PLC_" + name,
                    "PLC_" + name);
                PlcSoftware plc = null;
                foreach (var item in AllItems(device))
                {
                    var sc = item.GetService<SoftwareContainer>();
                    if (sc == null) continue;
                    plc = sc.Software as PlcSoftware;
                    if (plc != null) break;
                }
                if (plc == null) throw new Exception("no PLC software container");
                Console.WriteLine("plc=" + plc.Name);

                var table = plc.TagTableGroup.TagTables.First(t => t.IsDefault);
                table.Tags.Create("Start_C", "Bool", "%I0.0");
                table.Tags.Create("Stop_C", "Bool", "%I0.1");
                table.Tags.Create("Motor_C", "Bool", "%Q0.0");
                table.Tags.Create("Fault_C", "Bool", "%Q0.1");
                Console.WriteLine("tags=" + table.Tags.Count);

                var source = plc.ExternalSourceGroup.ExternalSources.CreateFromFile("Source_" + name, scl);
                source.GenerateBlocksFromSource();
                Console.WriteLine("blocks=" + plc.BlockGroup.Blocks.Count);
                foreach (var block in plc.BlockGroup.Blocks)
                {
                    Console.WriteLine("  " + block.Name + " " + block.GetType().Name + " number=" + block.Number);
                }

                var compilable = plc.GetService<ICompilable>();
                var result = compilable.Compile();
                Console.WriteLine("compile state=" + result.State + " errors=" + result.ErrorCount + " warnings=" + result.WarningCount);
                foreach (var message in result.Messages.Take(30))
                {
                    Console.WriteLine("  msg " + message.Description + " path=" + message.Path);
                }

                project.Save();
                Console.WriteLine("saved");
                project.Close();
                Console.WriteLine("closed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            Environment.ExitCode = 1;
        }
    }
}
