// 重写版（2026-11-17）：起停桌宠。

using System.Diagnostics;

namespace SettingTool;

internal static class PetHost
{
    internal static bool Running => Process.GetProcessesByName("FishPet").Length > 0;

    internal static string Exe
    {
        get
        {
            var one = System.IO.Path.Combine(Program.Root, "bin", "FishPet.exe");
            return File.Exists(one) ? one : System.IO.Path.Combine(Program.Root, "FishPet.exe");
        }
    }

    internal static void Start()
    {
        var exe = Exe;
        if (!File.Exists(exe)) return;
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Program.Root, UseShellExecute = true });
    }

    internal static void Stop()
    {
        foreach (var one in Process.GetProcessesByName("FishPet"))
        {
            try { one.Kill(); } catch { }
        }
    }

    internal static void Restart()
    {
        Stop();
        Thread.Sleep(1200);
        Start();
    }
}
