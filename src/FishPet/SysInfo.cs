using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FishPet;

// 住在机器里的感觉（本机加，2026-11-16）：读一点系统状态，偶尔念叨一句「内存快满了」「显卡有点烫」。
// 全是本地计数，不发任何东西出去。
internal static class SysInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private static ulong _idle, _kernel, _user;

    private static ulong Ticks(FileTime time) => ((ulong)time.High << 32) | time.Low;

    // 两次采样之间的 CPU 占用（0–1）；第一次调用返回 -1
    internal static double Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return -1;
        ulong i = Ticks(idle), k = Ticks(kernel), u = Ticks(user);
        if (_kernel == 0 && _user == 0) { _idle = i; _kernel = k; _user = u; return -1; }
        ulong di = i - _idle, dk = k - _kernel, du = u - _user;
        _idle = i; _kernel = k; _user = u;
        ulong total = dk + du; // kernel 里含 idle，所以分母只用 kernel+user
        if (total == 0) return -1;
        return Math.Clamp(1.0 - (double)di / total, 0, 1);
    }

    // 内存占用（0–1）
    internal static double Memory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return -1;
        return Math.Clamp(status.MemoryLoad / 100.0, 0, 1);
    }

    // 显卡：温度、占用（读不到就 -1）
    internal static (double Temp, double Load) Gpu()
    {
        try
        {
            var info = new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu,utilization.gpu --format=csv,noheader,nounits")
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var process = Process.Start(info);
            if (process == null) return (-1, -1);
            var text = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(1500);
            var parts = text.Split(',');
            if (parts.Length < 2) return (-1, -1);
            return (double.TryParse(parts[0].Trim(), out var t) ? t : -1,
                    double.TryParse(parts[1].Trim(), out var l) ? l : -1);
        }
        catch { return (-1, -1); }
    }

    // 够不够格让她念叨一句：内存过 85%、CPU 过 90%、显卡过 78 度
    internal static string? Worry()
    {
        var memory = Memory();
        if (memory >= .85) return "内存快满了……要不要关几个不用的？";
        var (temp, load) = Gpu();
        if (temp >= 78) return "显卡有点烫，都 " + (int)temp + " 度了";
        var cpu = Cpu();
        if (cpu >= .90) return "CPU 一直在满跑，是不是有什么在算东西";
        return null;
    }
}
