// 重写版（2026-11-17）：入口。

namespace SettingTool;

internal static class Program
{
    /// <summary>插件根目录（settings.json 所在的那一层）。</summary>
    internal static string Root { get; private set; } = "";

    /// <summary>单实例锁：整个进程活着期间一直拿着。</summary>
    private static Mutex? single;

    [STAThread]
    private static void Main()
    {
        // 本机加（2026-11-17）：以前完全没管 DPI，150% 缩放下排版全乱。
        // PerMonitorV2 让每个显示器按自己的缩放走，自绘控件也才拿到正确的尺寸。
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
        catch { }

        // 本机加（2026-11-17）：只允许开一个。
        // 以前能开出好几个窗口，各自拿着一份 settings.json 的内存副本，
        // 后点的「保存」会把先保存的改动整个盖掉。
        single = new Mutex(true, @"Local\DafeiyuSettingTool", out var isFirst);
        if (!isFirst)
        {
            ShowExisting();
            return;
        }

        // 无边框窗口万一出事，连对话框都可能弹不出来，所以先接住异常写日志
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash("Unhandled", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => Crash("Thread", e.Exception);

        try
        {
            Root = FindRoot();
            ApplicationConfiguration.Initialize();

            Application.Run(new MainForm());
        }
        catch (Exception error)
        {
            Crash("Main", error);
            throw;
        }
        finally
        {
            try { single.ReleaseMutex(); } catch { }
            single.Dispose();
        }
    }

    /// <summary>已经有窗口在开了：把她叫到前面来，不再开第二个。</summary>
    private static void ShowExisting()
    {
        var handle = FindWindowW(null, "大肥鱼设置");
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, 9);   // SW_RESTORE：最小化着也能还原
            SetForegroundWindow(handle);
            return;
        }

        MessageBox.Show("设置工具已经在运行了，请在任务栏里找一下。", "大肥鱼设置",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    /// <summary>从 exe 所在目录往上找插件根；找不到就退回用户目录下的「大肥鱼插件」。
    /// 本机修（2026-11-17）：之前没有兜底，exe 放桌面时根目录变成桌面，
    /// 结果在桌面上生成了一个 settings.json——一定要有最后的兜底。</summary>
    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "settings.json"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "model"))) return dir.FullName;
            if (File.Exists(Path.Combine(dir.FullName, "FishPet.exe"))) return dir.FullName;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "大肥鱼插件");
        if (Directory.Exists(fallback)) return fallback;

        return AppContext.BaseDirectory;
    }

    private static void Crash(string where, Exception? error)
    {
        try
        {
            var log = Path.Combine(string.IsNullOrEmpty(Root) ? AppContext.BaseDirectory : Root, "errors.log");
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} 设置工具 {where}：{error}\n");
        }
        catch { }
    }
}
