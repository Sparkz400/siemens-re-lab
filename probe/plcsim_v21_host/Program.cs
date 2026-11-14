using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Siemens.Simatic.PlcSim.VplcApi;
using PlcSim.VplcApi;

internal static class Program
{
    private static readonly string Bin = @"C:\Program Files\Siemens\Automation\PLCSIM_V21\resources\bin";
    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var local = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(local)) return Assembly.LoadFrom(local);
        var path = Path.Combine(Bin, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        if (Directory.Exists(Bin)) Directory.SetCurrentDirectory(Bin);
        Run(args);
    }
    private static void Run(string[] args)
    {
        try
        {
            var storage = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "plcsim-v21-storage");
            Directory.CreateDirectory(storage);
            var plc = new FileInfo(Path.Combine(Bin, "Siemens.Simatic.PlcSim.Vplc1500.dll"));
            var motion = new FileInfo(Path.Combine(Bin, "Siemens.Simatic.PlcSim.Vplc1500.Motion.dll"));
            Console.WriteLine("plc=" + plc.Exists + " motion=" + motion.Exists);
            var controller = new SimulationController();
            Console.WriteLine("PLCSIM V21 standard; powering on S7-1500");
            var ok = controller.PowerOn(Family.S71500, plc, motion, storage, CycleTimeoutMode.IgnoreConfiguration, 0);
            Console.WriteLine("powerOn=" + ok);
            foreach (var p in controller.State.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v = null;
                try { v = p.GetValue(controller.State); } catch { }
                Console.WriteLine("  state " + p.Name + " = " + (v == null ? "null" : v.ToString()));
            }
            try
            {
                var m = typeof(SimulationController).GetMethod("SetAddress_DEBUG_ONLY", BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null)
                {
                    m.Invoke(controller, new object[] { 1, new byte[] { 192, 168, 0, 1 }, true });
                    Console.WriteLine("set IP 192.168.0.1 on port 1");
                }
                else Console.WriteLine("SetAddress_DEBUG_ONLY not found");
            }
            catch (Exception ex) { Console.WriteLine("set IP failed: " + ex.GetBaseException().Message); }
            Thread.Sleep(15000);
            Console.WriteLine("after boot: mode=" + controller.State.OperatingMode + " booting=" + controller.State.IsBooting + " powered=" + controller.State.IsPoweredOn);
            try
            {
                foreach (var kv in controller.State.NetworkAddress) Console.WriteLine("  network port " + kv.Key + " = " + kv.Value);
            }
            catch (Exception ex) { Console.WriteLine("network dump failed: " + ex.GetBaseException().Message); }
            var stop = Path.Combine(Path.GetTempPath(), "plcsim-v21.stop");
            if (File.Exists(stop)) File.Delete(stop);
            Console.WriteLine("ready; waiting for stop file");
            for (int i = 0; i < 600 && !File.Exists(stop); i++) Thread.Sleep(1000);
            Console.WriteLine("stopping");
            try { controller.PowerOff(); } catch (Exception ex) { Console.WriteLine("poweroff error: " + ex.GetBaseException().Message); }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            Environment.ExitCode = 1;
        }
    }
}
