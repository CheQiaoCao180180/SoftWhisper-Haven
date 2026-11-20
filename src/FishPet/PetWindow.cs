using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FishPet;

// The pet: a transparent topmost window with her in it, her speech bubble above her head, her reflexes when
// clicked, dragged or left alone (Behavior), her own body language from what the agent has her say (Body,
// Line), and her mirror. Everything the agent does goes through Execute (Tools.cs for what each tool means).
internal sealed partial class PetWindow : Window
{
    private readonly Settings settings;
    private readonly string? model;
    private readonly bool test;
    private readonly SpriteImage image = new() { Stretch = Stretch.Fill };
    private readonly BubbleWindow bubble;
    private readonly Speech speech;
    private readonly Behavior behavior;
    private readonly Body body;
    private readonly BubbleWindow mutter; // 待机自言自语的小气泡
    private readonly Speech mutterSpeech; // 嘀咕/小剧场/事件也用 Speech 打字，好让嘴跟着字动（2026-11-06）
    private static readonly HttpClient Http = new();
    private readonly Random random = new();
    private readonly Dictionary<string, string> textCues = []; // 固定嘀咕文本 → 表情动作
    // 本机加（2026-11-18）：人设专属的台词单独放一个桶。
    // 以前它们和通用台词挤在同一个 list 里随机抽，结果人设台词常常抽不到——
    // 明明是「傲娇」只该说傲娇的话，说出来却是通用那句。
    private readonly Dictionary<string, List<(string Text, int Level, Condition Cond)>> personaTouch = []; // 人设专属那部分
    // 本机加（2026-11-18）：节日台词，key 是节日名（春节 / 中秋 / 除夕……）
    private readonly Dictionary<string, List<string>> festivalLines = [];
    // 本机加（2026-11-18）：今天说过节日的日子，避免一天里反复念
    private DateTime festivalSaidOn = DateTime.MinValue;
    private readonly Dictionary<string, List<(string Text, int Level, Condition Cond)>> touchLines = []; // 触碰反应的台词：反应名 → (台词, 至少要几级亲密度, 完整条件)
    // 摸头彩蛋的五句用抽签袋：一轮五句各一次，跨轮也尽量不连着同一句（本机加，2026-11-11）
    private readonly List<string> meltBag = [];
    private string? lastMeltLine;
    private readonly Updates updates;
    private readonly DispatcherTimer timer;
    // Out of the way while another program fills the screen (settings: hide_on_fullscreen), looked at twice a second.
    private bool hiddenForFullscreen;
    // 收起待命：程序和接口都开着，她不露面（窗口藏起、渲染暂停、待机表演停掉）；show / say / coax 会叫她出场。
    private bool standby, ready, hiding, coaxWoke, restarting;
    private int hideToken;
    private DateTime nextFullscreenLook;
    // 本机加（2026-11-11）：她的置顶偶尔会被人从外面撤掉（别的“总在最前”的程序、系统切全屏之类），
    // 普通窗口就压到她头上了。每秒看一次，掉了补回来；她被点、被摸、被拖时也抬到置顶层最上面
    // （WS_EX_NOACTIVATE 的窗口点了不会自己浮上来）。
    private DateTime nextTopmostLook;
    private DateTime topmostLostLogged;
    private Player? player;
    // 本机加（2026-11-19）：动作调试台冻结——她自己的待机、视线、姿势全停，只留 set_param 拧的值。
    private bool frozen;
    // 哄睡演示（coax）：记住她原来的位置和大小，好归位。
    private double? coaxLeft, coaxTop;
    private double coaxScale;
    private string? coaxShape;
    private bool coaxFollowMouse;
    private bool coaxAutoReturn, coaxSpeechSeen;
    private DateTime? coaxReturnAt;
    // 自言自语让位：大肥鱼开口说话（或哄睡演出）时，嘀咕立刻收起，说完后再安静一小会儿。
    private DateTime mutterQuietUntil;
    private int mutterToken;
    private bool textIdleActive;
    private bool coaxHoldPose;
    private string? coaxStage4Text; // 第四层台词（到点时取自 alarms.txt；演示时为空，用固定的那句）
    private ControlServer? server;
    private HttpApi? api;
    private string? trouble;
    private bool exiting, exitReady;
    private long shapeChange;
    private Shape Shape => Shape.Find(settings.Shape);

    // Dragging her about.
    private bool dragging, dragged;
    private Point dragStart, windowStart;

    // What the window does to her: moving pushes her body, the taskbar hides what reaches it.
    private double lastLeft, lastTop, lookX = 9, lookY = 9;
    // 待机时鼠标停在同一个地方超过 5 秒，就暂时关掉视线跟随；鼠标一动就恢复。
    private NativePoint lastCursor;
    private DateTime lastCursorAt = DateTime.UtcNow;
    private bool mouseIdle;
    // 鼠标停住后视线回正：0 没在回、1 眼睛在回（头还没动）、2 头也在回。头比眼睛晚 HeadReturnDelay 秒开始，转得慢（速率越小越慢，平时是 10）。
    private int returnStage;
    private DateTime returnAt;
    private const double HeadReturnDelay = .5, HeadReturnRate = 1.6, EyeReturnRate = 5;
    private Rect open;

    // Her lines: one at a time, in order; a line of nothing but 〔〕 plays with no bubble.
    private Task lines = Task.CompletedTask;
    private Line? silent;
    private readonly Stopwatch silentClock = new();
    private DateTime? relaxAt;
    private const double LingerSeconds = 3.5;

    // Her mouth, while the words come out.
    private double mouth;

    // Every line she says is filmed, for looking back at afterwards; kept half an hour.
    private sealed record Photo(DateTime At, string Said, string Picture);
    private readonly List<Photo> photos = [];
    private List<BitmapSource>? film;
    private string filmSaid = "";
    private DateTime filmShot;
    private DateTime? filmEnded;
    private const int PhotosKept = 20, FilmFrames = 120;
    private static readonly TimeSpan PhotoLife = TimeSpan.FromMinutes(30), FilmEvery = TimeSpan.FromMilliseconds(150), FilmAfter = TimeSpan.FromMilliseconds(600);
    private const int MirrorHeight = 520, MirrorGridHeight = 360, MirrorShots = 6, MirrorColumns = 3;
    private const double MirrorSpan = .5;

    // What the user did to her, for the agent (recent_events).
    private sealed record Happening(long Sequence, DateTime At, string Kind, object Detail);
    private readonly List<Happening> happenings = [];
    private long sequence;

    internal PetWindow(Settings settings, string? model, bool test, string? pipe = null, bool manual = false)
    {
        this.settings = settings; this.model = model; this.test = test;
        Condition.Birthday = settings.Birthday; // 生日那天的台词、小剧场（mutter.txt、skits.txt 里的 @生日）
        manualStart = manual;
        standby = settings.StartHidden && !test && !manual; // 手动叫出来的（桌面图标）不管「启动时先收起」，直接露面
        Title = "大肥鱼";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.Opacity = 0;
        Content = new Grid { Children = { image } };
        bubble = new BubbleWindow(this, () => Height * Shape.HeadTop) { RecordAs = test ? null : "speech" };
        bubble.Quiet = test;
        bubble.Scale = settings.BubbleScale;
        mutter = new BubbleWindow(this, () => Height * Shape.HeadTop) { Scale = 0.85, RecordAs = test ? null : "mutter" };
        mutter.Quiet = test;
        LoadMutterFile();
        LoadSkits();
        LoadTouchFile();
        WatchLines();
        speech = new Speech(bubble) { Seconds = settings.BubbleSeconds };
        mutterSpeech = new Speech(mutter) { Seconds = -1 }; // 停留由嘀咕/小剧场/事件自己控制，Speech 不自动收
        body = new Body(Send);
        updates = new Updates(Places.Root, Places.Log);
        behavior = new Behavior(Send, () => [.. settings.Outfit, .. body.Overlays], new Random()) { HandBusy = () => body.HandUp, Ended = body.Restore, IdleStarted = name => OnIdleStarted(name), TextIdle = text => PlayTextIdle(text), TextIdleMoods = textCues.Keys.ToArray(), SkitIdle = name => PlaySkit(name), SkitIdleMoods = skits.Keys.ToArray(),
            TextWeight = (name, local) => WeightOf(textConds, name, local), SkitWeightOf = (name, local) => WeightOf(skitConds, name, local),
            ChimeTexts = ChimeNow, ChimeBlocked = () => MutterBlocked || textIdleActive, Smitten = () => affection.Level >= Affection.Levels.Length, LevelOf = () => affection.Level, PersonaOf = () => settings.Persona };
        ApplySize();
        Place();
          HookDrop();
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Never takes the focus from what the user is doing, not in Alt+Tab.
            SetWindowLongPtr(handle, -20, (nint)((long)GetWindowLongPtr(handle, -20) | 0x8000080));
            // 本机修（2026-11-06）：Windows 默认不让窗口比屏幕大太多（最大跟踪尺寸≈屏幕+边框）。全身体型放到 2 倍时窗口比屏幕还高，
            // 高度被系统压矮约 20%，而画面还是按原高度渲染、被硬塞进矮窗口里，人就显得横向被拉扁。这里把上限放开。
            HwndSource.FromHwnd(handle)?.AddHook(NoWindowSizeLimit);
            // 本机加（2026-11-15）：Ctrl+Shift+0 在 Q 版和全身之间切换。RegisterHotKey 是全局热键，别的窗口有焦点也认。
            try
            {
                // 本机改（2026-11-17）：这几个键改成从 settings.json 读，设置工具里能自己改
                RegisterHotkey(handle, ShapeHotkeyId, settings.HotkeyShape, Hotkeys.DefaultShape, "换体型", on => shapeHotkey = on);
                RegisterHotkey(handle, PersonaHotkeyId, settings.HotkeyPersona, Hotkeys.DefaultPersona, "换人格", on => personaHotkey = on);
                RegisterHotkey(handle, GlassesHotkeyId, settings.HotkeyGlasses, Hotkeys.DefaultGlasses, "换眼镜", on => glassesHotkey = on);
                RegisterHotkey(handle, ShoesHotkeyId, settings.HotkeyShoes, Hotkeys.DefaultShoes, "换鞋", on => shoesHotkey = on);
                RegisterHotkey(handle, SkirtHotkeyId, settings.HotkeySkirt, Hotkeys.DefaultSkirt, "换裙子", on => skirtHotkey = on);
                RegisterHotkey(handle, ChatHotkeyId, settings.HotkeyChat, Hotkeys.DefaultChat, "对话框", on => chatHotkey = on);
                HwndSource.FromHwnd(handle)?.AddHook(ShapeHotkeyHook);
            }
            catch (Exception error) { Places.Log("热键注册失败：" + error.Message); }
        };
        Loaded += (_, _) =>
        {
            Start();
            GreetFestival(); // 本机加：节日当天说一句
            StartMusic(); // 跟着电脑里的音乐跳（PetWindow.Dance.cs）
            LaunchTray();
            if (!test) // 开发用透明帧导出：插件\record\on.json 在就录（Recorder.cs，本机加 2026-11-08）
            {
                Recorder.BoostChanged += ApplySize;
                Recorder.Init(Dispatcher, RecordLayout);
            }
            server = new ControlServer(Execute, pipe);
            server.WakeRequested = () => Dispatcher.BeginInvoke(() => { if (Wake(greet: true)) NoAgentHint(); });
            try { api = HttpApi.Start(Places.ApiFile, test ? 0 : HttpApi.DefaultPort, (name, args, token) => { if (!HttpApi.Passive.Value) lastHttp = DateTime.UtcNow; return Execute(name, args, token); }); }
            catch (Exception error) { Places.Log("网页接口开不起来：" + error.Message); }
            // A newer plugin on GitHub, looked for now and twice a day; the agent hears of it with its next tool (Execute).
            if (!test) _ = Task.Run(async () =>
            {
                while (true)
                {
                    if (settings.CheckUpdates) await updates.Check();
                    await Task.Delay(TimeSpan.FromHours(12));
                }
            });
        };
        PreviewMouseLeftButtonDown += Down;
        // 本机加（2026-11-15）：滚轮在她身上滚，缩放大小（0.5～2 倍），滚完记住
        PreviewMouseWheel += (_, e) =>
        {
            double next = Math.Clamp(Math.Round((settings.Scale + (e.Delta > 0 ? .05 : -.05)) * 20) / 20, .5, 2);
            e.Handled = true;
            if (Math.Abs(next - settings.Scale) < .001) return;
            settings.Scale = next;
            settings.Save();
            ApplySize();
            Clamp();
        };
        PreviewMouseMove += Move;
        PreviewMouseLeftButtonUp += Up;
        PreviewMouseRightButtonDown += StrokeDown; // 右键抚摸、悬停反应、拖拽惯性和头晕（PetWindow.Touch.cs）
        PreviewMouseRightButtonUp += StrokeUp;
        PreviewMouseMove += HoverMove;
        MouseLeave += (_, _) => hoverZone = null;
        LostMouseCapture += (_, _) => { if (dragging) { dragging = false; if (dragged) Dropped(); } if (stroking) EndStroke(); };
        Closing += Leaving;
        Closed += (_, _) => { StopMusic(); TouchClosed(); Recorder.Stop(wait: true); server?.Dispose(); api?.Dispose(); player?.Dispose(); bubble.Close(); };
    }

    private void Send(string command) => player?.Send(command);

    // 录制的版面信息，都是帧像素：气泡尖儿该对着哪儿（窗口中线、头顶往下 6 DIP），气泡的样式参数。
    private object RecordLayout()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double k = dpi.DpiScaleX * Recorder.Boost;
        return new
        {
            dpiScale = dpi.DpiScaleX, supersample = Recorder.Boost, windowDip = new { width = Width, height = Height },
            shape = settings.Shape, scale = settings.Scale, texScale = settings.TexScale,
            bubbleTip = new { x = Math.Round(Width / 2 * k, 1), y = Math.Round((Height * Shape.HeadTop + 6) * k, 1) },
            bubbleStyle = new { scale = settings.BubbleScale, mutterScale = 0.85, font = "Microsoft YaHei UI", fontSizeDip = 15, lineHeightDip = 22, maxWidthDip = 300,
                padding = new[] { 18, 14, 14, 14 + Bubble.PointerHeight }, cornerRadius = 24, border = 1.5, borderColor = "#506098", background = "#FFFBF4", textColor = "#323854", pointerHalfWidth = 8, pointerHeight = Bubble.PointerHeight },
        };
    }

    // 本机加的（2026-11-06）：桌宠起来时顺手带起任务栏托盘里的大肥鱼控制台。先找插件里编好的（插件\tray\app\FishTray.exe，setup.ps1 编），
    // 再找老位置（文档\大肥鱼控制台\app\FishTray.exe）。它自己只留一个、桌宠退场后自己也退；没装它就什么也不做。
    private void LaunchTray()
    {
        if (test) return;
        try
        {
            var exe = new[] { Path.Combine(Places.Root, "tray", "app", "FishTray.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "大肥鱼控制台", "app", "FishTray.exe") }.FirstOrDefault(File.Exists);
            if (exe != null)
            {
                // 插件文件夹常被沙箱打上“低完整性”标签，桌宠因此低权限运行，直接拉起的托盘会继承低权限：写不了文件、图标也出不来。
                // 所以交给计划任务 FishTray（普通权限，装控制台时建的）来拉；没有这个任务才退回直接拉起。
                using var ask = Process.Start(new ProcessStartInfo("schtasks.exe", "/run /tn FishTray") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                if (ask is null || !ask.WaitForExit(5000) || ask.ExitCode != 0)
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
            }
        }
        catch (Exception error) { Places.Log("托盘没起来：" + error.Message); }
    }

    // ---- Her renderer ----

    private void Start()
    {
        // 本机加（2026-11-19）：第一次启动缺东西就弹一次引导（放模型 / 装语音 / 怎么叫她）——只弹一次
        if (!test) FirstRun.ShowIfNeeded(settings, System.IO.Path.Combine(Places.Root, "model"), model != null, FirstRun.VoiceCount());
        if (model == null) { Trouble("没找到模型：把模型文件夹放进插件的 model 文件夹里（里面要有 " + Places.ModelFile + "）。"); return; }
        if (!File.Exists(Places.Renderer)) { Trouble("没找到渲染程序：先运行插件里的 setup.ps1 编译它。"); return; }
        timeBeginPeriod(1); // 摸头那只手要更高帧：把系统计时精度提到 1ms（关闭时恢复）
        var started = new Player(image);
        started.Ready += Ready;
        started.Failed += message => Trouble("渲染出问题了：" + message);
        player = started;
        danceSent.Clear(); // 新渲染器不知道上一轮按住过什么，跳舞的参数要重新发一遍（PetWindow.Dance.cs）
        lastLeft = Left; lastTop = Top;
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = dpi.DpiScaleX * Recorder.Boost; // 宽高用同一系数，保证渲染画面比例始终与窗口一致（不会变形）；录制 supersample 时按几倍出图
        Recorder.Expect((int)(Width * scale), (int)(Height * scale));
        started.Start(model, Shape, settings.Outfit, (int)(Width * scale), (int)(Height * scale), settings.Fps, settings.TexScale);
        body.Forget(); silent = null; relaxAt = null;
        behavior.Resume(DateTime.UtcNow, settings.IdleSeconds);
        timer.Start();
        open = default;
        UpdateCut();
    }

    // 换清晰度档位后重启渲染器：贴图按新的 texscale 重新加载（表情回到默认，衣服配饰和体型会重新穿上）。
    private void RestartRenderer()
    {
        restarting = true;
        player?.Dispose();
        Start();
    }

    private void Trouble(string message)
    {
        trouble = message;
        Places.Log(message);
        bubble.Say(message);
    }

    // She comes in and greets once her body type has settled.
    private void Ready()
    {
        ready = true;
        if (standby) return; // 收着待命：先不露面、不问候
        if (restarting) { restarting = false; return; } // 换清晰度重启渲染：不重新问候
        image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        behavior.Show("问候", DateTime.UtcNow);
        body.Apply("抚胸");
        WelcomeSoon(); // 全新安装的第一次出场，接着说几句欢迎（只说这一次）
        GreetSoon(); // 每次启动按钟点问一句（本机加，2026-11-15）
        if (manualStart) NoAgentHint();
    }

    // 手动叫出来（桌面图标）时，几秒后还没有 Agent 连上，就提一句，免得用户以为她坏了。
    private readonly bool manualStart;
    private async void NoAgentHint()
    {
        try
        {
            await Task.Delay(3500);
            if (exiting || standby || (server?.Connections ?? 0) > 0) return;
            _ = InTurn(() => { if (!exiting && (server?.Connections ?? 0) == 0) SayLine("〔歪一下头:右 微笑〕现在还没连上 Agent 哦~"); return Task.CompletedTask; }, CancellationToken.None);
        }
        catch (Exception error) { Places.Log("没连上 Agent 的提示出错：" + error.Message); }
    }


    private void ApplySize()
    {
        var shape = Shape;
        // Her feet stay where they are, not the window's corner, so resizing does not lift her out of the taskbar.
        double middle = Left + Width / 2, feet = Top + Height * shape.FootAt;
        Width = Math.Round(shape.Width * settings.Scale);
        Height = Math.Round(shape.Height * settings.Scale);
        if (double.IsFinite(middle) && double.IsFinite(feet)) { Left = middle - Width / 2; Top = feet - Height * shape.FootAt; }
        if (PresentationSource.FromVisual(this) != null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var scale = dpi.DpiScaleX * Recorder.Boost; // 宽高用同一系数，避免非整数缩放造成轻微变形；录制 supersample 时按几倍出图
            Recorder.Expect((int)(Width * scale), (int)(Height * scale));
            Send($"resize {(int)(Width * scale)} {(int)(Height * scale)}");
        }
        UpdateCut();
    }

    // Where she was, or the bottom right corner half sunk behind the taskbar. In a test, out of sight.
    private void Place()
    {
        if (test) { Left = -6000; Top = 100; return; }
        var area = SystemParameters.WorkArea;
        Left = settings.Left ?? area.Right - Width - 8;
        Top = settings.Top ?? area.Bottom - Height * .5;
        Clamp();
    }

    // 本机加（2026-11-15）：只看前台窗口的进程名，不看内容 —— 判断你现在是不是在做沉浸的事
    private DateTime sysWorryAt = DateTime.MinValue; // 上次念叨机器状态的时间
    private string foregroundProcess = "";
    private DateTime nextForegroundLook = DateTime.MinValue;

    private void LookAtForeground()
    {
        var now = DateTime.UtcNow;
        if (now < nextForegroundLook) return;
        nextForegroundLook = now.AddSeconds(20);
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) { foregroundProcess = ""; return; }
            GetWindowThreadProcessId(hwnd, out uint owner);
            foregroundProcess = owner == 0 ? "" : Process.GetProcessById((int)owner).ProcessName.ToLowerInvariant();
        }
        catch { foregroundProcess = ""; }
    }

    // 全屏游戏、播放器这类：她在旁边安安静静，不主动开口
    // 识别系统（本机加，2026-11-16）：看前台进程名猜他现在在干什么，待机时按场景说话
    private string Scene()
    {
        var p = foregroundProcess;
        if (p == "") return "";
        if (p.Contains("code") || p.Contains("devenv") || p.Contains("cursor") || p.Contains("idea")
            || p.Contains("rider") || p.Contains("pycharm") || p.Contains("sublime") || p.Contains("notepad++")) return "写代码";
        if (p.Contains("chrome") || p.Contains("msedge") || p.Contains("firefox") || p.Contains("brave")) return "看网页";
        if (p.Contains("steam") || p.Contains("genshin") || p.Contains("yuanshen") || p.Contains("starrail")
            || p.Contains("zenless") || p.Contains("client-win64") || p.Contains("epicgames")) return "打游戏";
        if (p.Contains("potplayer") || p.Contains("vlc") || p.Contains("mpc-hc") || p.Contains("bilibili")
            || p.Contains("iqiyi") || p.Contains("netflix")) return "看视频";
        if (p.Contains("cloudmusic") || p.Contains("qqmusic") || p.Contains("spotify")) return "听歌";
        if (p.Contains("comfy") || p.Contains("photoshop") || p.Contains("blender") || p.Contains("premiere")) return "干活";
        return "";
    }

    private bool BusyForeground() => foregroundProcess is not "" &&
        (foregroundProcess.Contains("steam") || foregroundProcess.Contains("genshin") || foregroundProcess.Contains("yuanshen")
         || foregroundProcess.Contains("starrail") || foregroundProcess.Contains("zenless") || foregroundProcess.Contains("vlc")
         || foregroundProcess.Contains("potplayer") || foregroundProcess.Contains("mpc-hc"));

    // She may sink behind the taskbar, but her face stays out.
    // 本机加（2026-11-15）：拖到屏幕边缘附近就顺着边贴好
    private void SnapToEdge()
    {
        if (test || dragging) return;
        var area = SystemParameters.WorkArea;
        const double edge = 28;
        var shape = Shape;
        double side = Width * shape.Side;
        if (Math.Abs(Left + side - area.Left) < edge) Left = area.Left - side;
        else if (Math.Abs(Left + Width - side - area.Right) < edge) Left = area.Right + side - Width;
        if (Math.Abs(Top + Height * shape.FootAt - area.Bottom) < edge) Top = area.Bottom - Height * shape.FootAt;
    }

    // 本机加（2026-11-15）：弹一张图（表情包）在她旁边，停几秒收掉；有配字就连字一起说
    private async void ShowPicture(string path, string? text, double seconds)
    {
        try
        {
            YieldMutter();
            mutter.Quiet = false;
            mutter.ShowPicture(path);
            if (!string.IsNullOrWhiteSpace(text)) mutter.Text.Text = text;
            else mutter.Text.Text = "";
            if (settings.Speech && !string.IsNullOrWhiteSpace(text)) voice.Speak(text, settings.Persona);
            mutterSpeech.Say(new Line(string.IsNullOrWhiteSpace(text) ? " " : text, body.Apply));
            mutter.Show();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            mutter.Hide();
            mutter.ShowPicture(null);
        }
        catch { }
    }

    // 本机加（2026-11-15）：跟着 DSH 的人设走 —— 读 persona-switcher 的 sessions.json，最近切换的那个角色
    private static readonly Dictionary<string, string> PersonaByRole = new()
    {
        ["wife"] = "人妻", ["tsundere"] = "傲娇", ["childlike"] = "童真", ["yandere"] = "病娇",
    };
    private DateTime nextPersonaLook = DateTime.MinValue;

    private void FollowHarnessPersona()
    {
        if (!settings.PersonaFollowsHarness) return;
        var now = DateTime.UtcNow;
        if (now < nextPersonaLook) return;
        nextPersonaLook = now.AddSeconds(10);
        try
        {
            string file = string.IsNullOrWhiteSpace(settings.PersonaStateFile)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "persona-switcher", "sessions.json")
                : settings.PersonaStateFile;
            if (!System.IO.File.Exists(file)) return;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("sessions", out var sessions)) return;
            string role = "", newest = "";
            foreach (var one in sessions.EnumerateObject())
            {
                if (!one.Value.TryGetProperty("role", out var roleValue) || roleValue.ValueKind != JsonValueKind.String) continue;
                string at = one.Value.TryGetProperty("at", out var atValue) ? atValue.GetString() ?? "" : "";
                if (at.CompareTo(newest) >= 0) { newest = at; role = roleValue.GetString() ?? ""; }
            }
            if (PersonaByRole.TryGetValue(role, out var persona) && persona != settings.Persona)
            {
                settings.Persona = persona;
                settings.Save();
                Places.Log("人设跟着 DSH 换成「" + persona + "」");
            }
        }
        catch { /* 读不到就保持原来的 */ }
    }

    private void Clamp()
    {
        if (test) return;
        var area = SystemParameters.WorkArea;
        var shape = Shape;
        var bounds = new Rect(Width * shape.Side, Height * shape.HeadTop, Width * (1 - 2 * shape.Side), Height * (shape.HeadAt + .12 - shape.HeadTop));
        if (!settings.KeepOnScreen)
        {
            // 能拖出屏幕：整块桌面（所有显示器）外面最多藏到只剩 60 像素的身子，免得找不回来
            const double keep = 60;
            double x = SystemParameters.VirtualScreenLeft, y = SystemParameters.VirtualScreenTop;
            double r = x + SystemParameters.VirtualScreenWidth, b = y + SystemParameters.VirtualScreenHeight;
            Left = Math.Clamp(Left, x - bounds.Right + keep, Math.Max(x - bounds.Right + keep, r - bounds.Left - keep));
            Top = Math.Clamp(Top, y - bounds.Bottom + keep, Math.Max(y - bounds.Bottom + keep, b - bounds.Top - keep));
            return;
        }
        Left = Math.Clamp(Left, area.Left - bounds.Left, Math.Max(area.Left - bounds.Left, area.Right - bounds.Right));
        Top = Math.Clamp(Top, area.Top - bounds.Top, Math.Max(area.Top - bounds.Top, area.Bottom - bounds.Bottom));
    }

    private void SavePlace()
    {
        if (test) return;
        settings.Left = Left; settings.Top = Top;
        settings.Save();
    }

    // 哄睡演示（coax）：把窗口摆到屏幕各处，让 Agent 快速看三层递进的动作效果。
    private object Coax(JsonElement args)
    {
        int stage = args.GetProperty("stage").GetInt32();
        if (stage != 0 && Wake()) coaxWoke = stage is >= 1 and <= 4; // 收着时被叫出来演哄睡：演完再自己收回去
        YieldMutter(); // 哄睡演出也优先：嘀咕让位
        coaxStage4Text = null; // 演示用固定的那句；到点的第四层台词来自 alarms.txt（Ring 里设置），不再接受 Agent 实时生成传入（用户 2026-11-12 去掉）
        string? skitName = args.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null; // 只有演示小剧场（stage 6）用它
        if (stage == 0)
        {
            CoaxReturn();
            return Tools.Text(new { stage = 0, restored = true });
        }
        if (stage == 5) { DemoIdle(); return Tools.Text(new { stage = 5, demo = "待机小动作" }); }
        if (stage == 6) // 演示待机小剧场：text 给小剧场名，不给就随机一个
        {
            if (skits.Count == 0) return Tools.Error("skits.txt 里还没有小剧场。");
            var pick = skitName != null && skits.ContainsKey(skitName) ? skitName : skits.Keys.ElementAt(random.Next(skits.Count));
            PlaySkit(pick, force: true);
            return Tools.Text(new { stage = 6, skit = pick, all = skits.Keys.ToArray() });
        }
        RememberPlace();
        var pos = RunStage(stage);
        if (pos == null) return Tools.Error("stage 要 0–3。");
        return Tools.Text(new { stage, left = pos.Value.Left, top = pos.Value.Top, scale = settings.Scale });
    }

    // 演哄睡前记下原来的体型、大小、位置，演完归位用。
    private void RememberPlace()
    {
        if (coaxScale != 0) return;
        coaxShape = settings.Shape;
        coaxScale = settings.Scale;
        coaxLeft = Left; coaxTop = Top;
        coaxFollowMouse = settings.FollowMouse;
    }

    // 记下了位置但这一层没演成（被收起、退出打断）：把记录丢掉，等于没记过。
    private void ForgetPlace()
    {
        coaxShape = null;
        coaxScale = 0;
        coaxLeft = coaxTop = null;
    }

    // 摆好某一层的体型、大小和位置并出场。返回出场位置；stage 非法返回 null。
    private (double Left, double Top)? RunStage(int stage, int fadeInMs = 220)
    {
        coaxEpoch++; // 正在归位的话作废
        var area = SystemParameters.WorkArea;
        double targetLeft, targetTop;
        switch (stage)
        {
            case 1: // 第一层：屏幕正中，温柔关心
                if (Shape.Id != "full") { settings.Shape = "full"; player?.SetShape(Shape); }
                settings.Scale = 2;
                settings.FollowMouse = false;
                ApplySize();
                targetLeft = area.Left + (area.Width - Width) / 2;
                targetTop = area.Top + area.Height * 0.40 - Height * Shape.HeadTop;
                coaxAutoReturn = true; coaxSpeechSeen = false; coaxReturnAt = null;
                break;
            case 2: // 第二层：屏幕正中，严肃
                if (Shape.Id != "full") { settings.Shape = "full"; player?.SetShape(Shape); }
                settings.Scale = 2;
                settings.FollowMouse = false;
                ApplySize();
                targetLeft = area.Left + (area.Width - Width) / 2;
                targetTop = area.Top + area.Height * 0.40 - Height * Shape.HeadTop;
                coaxAutoReturn = true; coaxSpeechSeen = false; coaxReturnAt = null;
                break;
            case 3: // 第三层：屏幕正中，屑屑眼「时间到了」
                if (Shape.Id != "full") { settings.Shape = "full"; player?.SetShape(Shape); }
                settings.Scale = 2;
                settings.FollowMouse = false;
                ApplySize();
                targetLeft = area.Left + (area.Width - Width) / 2;
                targetTop = area.Top + area.Height * 0.40 - Height * Shape.HeadTop;
                coaxAutoReturn = true; coaxSpeechSeen = false; coaxReturnAt = null;
                break;
            case 4: // 第四层：屏幕正中，黑脸质问
                if (Shape.Id != "full") { settings.Shape = "full"; player?.SetShape(Shape); }
                settings.Scale = 2;
                settings.FollowMouse = false;
                ApplySize();
                targetLeft = area.Left + (area.Width - Width) / 2;
                targetTop = area.Top + area.Height * 0.40 - Height * Shape.HeadTop;
                coaxAutoReturn = true; coaxSpeechSeen = false; coaxReturnAt = null;
                coaxHoldPose = true; // 说完后保持黑脸盯人，直到归位
                body.Apply("闭眼 黑脸"); // 出场就是闭眼 + 黑脸
                Stage4Sequence(); // 1 秒后睁眼，再 1 秒后慢慢质问
                break;
            default:
                return null;
        }
        AppearAt(targetLeft, targetTop, fadeInMs: fadeInMs);
        return (targetLeft, targetTop);
    }

    // 第四层演出：出场时已经闭眼+黑脸，1 秒后突然睁眼盯前方，再大前倾，再 1 秒后一个字一个字质问（每字 0.5 秒）。
    private async void Stage4Sequence()
    {
        await Task.Delay(1200); // 出场淡入 + 闭眼黑脸停留约 1 秒
        body.Apply("睁大 正视"); // 突然睁眼，直勾勾盯前方
        await Task.Delay(350); // 睁眼后停顿一下
        body.Apply("大前倾"); // 身子猛地向前倾
        await Task.Delay(1000);
        SayLine(coaxStage4Text ?? "为 什 么 还 没 睡 ？", 2); // 每字 0.5 秒
    }

    // 归位（用户 2026-11-13 改）：先快速鞠躬淡出退场，趁看不见恢复原来的体型、大小、视线跟随，
    // 再在上一次的位置快速淡入。不再是正中的她一下变小、一下跳回去。
    private int coaxEpoch; // 归位的路上又来了新的一层（RunStage）就把这次归位作废，别两边抢
    private async void CoaxReturn()
    {
        coaxAutoReturn = false; coaxSpeechSeen = false; coaxReturnAt = null; // 别每帧再触发
        int epoch = ++coaxEpoch;
        try
        {
            bool visible = await QuickLeave();
            if (epoch != coaxEpoch) return;
            if (coaxShape != null) { settings.Shape = coaxShape; player?.SetShape(Shape); coaxShape = null; }
            settings.FollowMouse = coaxFollowMouse;
            if (coaxScale > 0) { settings.Scale = coaxScale; ApplySize(); }
            double left = coaxLeft ?? Left, top = coaxTop ?? Top;
            coaxLeft = coaxTop = null; coaxScale = 0;
            coaxHoldPose = false;
            if (visible) AppearAt(left, top, () => body.Relax(), EnterFadeMs); // 在看不见的时候收起黑脸盯人的样子
            else { body.Relax(); Left = left; Top = top; } // 被收起/退出打断：她们自己管淡出，这里只把状态放回去
        }
        catch (Exception error) { Places.Log("归位出错：" + error.Message); }
        if (epoch == coaxEpoch && coaxWoke) { coaxWoke = false; _ = HideAfterCoax(); }
    }

    private async Task HideAfterCoax()
    {
        await Task.Delay(1000);
        await HideAway();
    }

    // ---- 收起待命 / 出场 ----

    // 出场：窗口显出来、渲染恢复。返回是不是真的从收起状态出来的（本来就在就是 false）。
    private bool Wake(bool greet = false)
    {
        hideToken++; // 正在收起的话，作废
        if (hiding) { hiding = false; image.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))); }
        if (!standby) return false;
        standby = false;
        alarmLast = DateTime.Now; // 收着的时候主计时器不转，错过的哄睡/午饭提醒不补（否则出来后 2 分钟内会被补演一遍，用户 2026-11-13 不要）
        StartMusic(); // 出来了才开始听歌（收着的时候不白开采集进程）。要在 standby 落下之后：StartMusic 自己会看 standby
        nextFullscreenLook = default;
        if (player is { } current) UpdateFullscreen(current);
        if (ready) image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        if (greet) { behavior.Show("问候", DateTime.UtcNow); body.Apply("抚胸"); }
        WelcomeSoon(); // 本来收着（start_hidden）的全新安装，第一次被叫出来时说欢迎
        return true;
    }

    // 收起：等话说完，鞠躬，淡出，再藏起窗口、停掉渲染；程序和接口都还开着。中途被叫出来（Wake）就作废。
    private async Task HideAway()
    {
        if (standby || hiding || exiting || test) return;
        int token = ++hideToken;
        hiding = true;
        YieldMutter();
        for (int wait = 0; wait < 150 && speech.IsSaying && token == hideToken; wait++) await Task.Delay(200);
        if (token != hideToken) return;
        behavior.Show("告别", DateTime.UtcNow);
        await Task.Delay(1600);
        if (token != hideToken) return;
        image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(500)));
        await Task.Delay(600);
        if (token != hideToken || exiting) return;
        hiding = false; standby = true;
        nextFullscreenLook = default;
        mutterSpeech.Stop();
        StopMusic(); // 收进待命就别再听了，省一个常驻的采集进程
    }

    // 演示全部待机小动作（临时给用户看）。
    private async void DemoIdle()
    {
        string[] moods = ["张望", "哼歌", "害羞", "得意", "思考", "打哈欠", "打瞌睡", "歪头", "发呆", "叹气", "玩狗尾草"];
        foreach (var name in moods)
        {
            behavior.Show(name, DateTime.UtcNow, 1, force: true);
            await Task.Delay(4200);
        }
        behavior.ClearActivities();
        body.Relax();
    }

    // ───────── 待机自言自语：嘀咕、固定文本、小剧场、临时道具 ─────────
    // 说话优先：只要大肥鱼开口或在哄睡演出，这里的一切都让位（见 YieldMutter / MutterBlocked）。

    private sealed record SkitStep(string Text, string? Cue, double Seconds);
    private sealed record Skit(string[] Props, SkitStep[] Steps);
    private readonly Dictionary<string, Skit> skits = [];
    private readonly Dictionary<string, string[]> textProps = []; // 固定嘀咕文本 → 临时道具
    private readonly HashSet<string> tempProps = [];               // 现在临时戴着的配饰（模型里的表情名）
    private DateTime propsUntil;

    // 待机原版动作配的临时道具：动作名 → (概率, 道具)。
    private static readonly Dictionary<string, (double Chance, string Prop)> IdlePropTable = new()
    {
        ["思考"] = (.4, "单片眼镜"), ["张望"] = (.25, "单片眼镜"), ["得意"] = (.35, "兔牙"),
        ["叹气"] = (.2, "钢盆"), ["哼歌"] = (.15, "兔牙"), ["发呆"] = (.2, "黑框眼镜"),
    };

    // 台词、小剧场的条件（时段、星期、日期、整点……，见 Conditions.cs）：名字 → 条件；没写条件的不在表里，永远可用。
    private readonly Dictionary<string, Condition> textConds = [];
    private readonly Dictionary<string, Condition> skitConds = [];

    // 抽签权重：没条件 1；整点专用的不参加平时抽签 0；条件符合 Boost，不符合 0。
    private int WeightOf(Dictionary<string, Condition> table, string name, DateTime local) =>
        !table.TryGetValue(name, out var condition) ? 1 : condition.Chime ? 0 : condition.Matches(local, affection.Level) ? condition.Boost : 0;

    // 这个钟点该报的台词：整点专用、条件符合（几点、时段、星期、日期）的。
    private string[] ChimeNow(DateTime local) =>
        textConds.Where(item => item.Value.Chime && item.Value.Matches(local, affection.Level)).Select(item => item.Key).ToArray();

    // mutter.txt：每行「文本|动作|道具|条件」，后三项可省（没有道具但有条件时，道具那栏留空：「文本|动作||深夜」）。
    private void LoadMutterFile()
    {
        // 本机加（2026-11-16）：支持热重载——重读前先清空，不然台词会叠加
        textCues.Clear(); textProps.Clear(); textConds.Clear();
        try
        {
            foreach (var line in File.ReadAllLines(Path.Combine(Places.Root, "mutter.txt")))
            {
                if (line.TrimStart().StartsWith("//")) continue;
                var parts = line.Split('|', 4);
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) continue;
                if (parts.Length == 4)
                {
                    var condition = Condition.Parse(parts[3], out var bad);
                    if (condition == null) { Places.Log($"mutter.txt 的条件读不懂「{bad}」，这行先不用：{parts[0]}"); continue; }
                    if (!condition.Plain) textConds[parts[0]] = condition;
                }
                textCues[parts[0]] = parts[1].Trim();
                if (parts.Length >= 3 && parts[2].Trim().Length > 0) textProps[parts[0]] = ParseProps(parts[2]);
            }
        }
        catch { }
    }

    private static string[] ParseProps(string text) =>
        text.Split([' ', '　', '、', '，', ','], StringSplitOptions.RemoveEmptyEntries).Where(name => PetProps.Names.Contains(name)).Distinct().ToArray();

    // touch.txt：「反应|台词」或「反应|台词|亲密3」，点她身体不同位置时随机念一句（反应见 Behavior.ClickMood：摸头/戳脸/开心/护裙/震惊/心虚，连点 5 下是「挨打」，
    // 亲密度满档时戳裙子是「娇羞」）。写了「亲密N」的那句要到第 N 级才会说，而且级别越高的句子越常被挑中（本机加，2026-11-08）。
    /// <summary>本机加（2026-11-18）：读节日台词 lines\<语言>\festivals.txt。</summary>
    private void LoadFestivals()
    {
        festivalLines.Clear();
        try
        {
            var path = LineFiles.Pick(Voice.Language, "festivals.txt");
            if (path == null || !File.Exists(path)) return;
            string? current = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                if (line.StartsWith("#"))
                {
                    current = line[1..].Trim();
                    if (current.Length > 0) festivalLines[current] = [];
                    continue;
                }
                if (current == null) continue;
                if (line.Contains('|')) line = line[(line.IndexOf('|') + 1)..].Trim();
                if (line.Length > 0) festivalLines[current].Add(line);
            }
            Places.Log($"节日台词：{festivalLines.Count} 个节日");
        }
        catch (Exception error) { Places.Log("节日台词没读成：" + error.Message); }
    }

    /// <summary>本机加（2026-11-18）：今天第一次醒过来时，看看是什么节，说一句。</summary>
    private async void GreetFestival()
    {
        try
        {
            var now = DateTime.Now;
            if (festivalSaidOn.Date == now.Date) return;
            var name = Calendars.Today(now) ?? (Calendars.IsQingming(now) ? "清明" : null);
            if (name == null) return;
            if (!festivalLines.TryGetValue(name, out var list) || list.Count == 0) return;
            festivalSaidOn = now;
            // 起来先愣两秒，别一开机就抢话
            await Task.Delay(2200);
            if (exiting || speech.IsSaying) return;
            var said = list[random.Next(list.Count)];
            mutterSpeech.Say(new Line(said, body.Apply));
            if (settings.Speech) voice.Speak(Body.Strip(said), settings.Persona);
            Places.Log("节日：" + name + " —— " + said);
        }
        catch { }
    }

    private void LoadTouchFile()
    {
        LoadFestivals();
        // 本机加（2026-11-16）：支持热重载——重读前先清空
        touchLines.Clear();
        personaTouch.Clear();
        // touch.txt 之外，右键抚摸、悬停、头晕的台词（stroke.txt / hover.txt / dizzy.txt）格式一样，一起读进来；哪个文件没有就跳过。
        // 本机改（2026-11-17）：路径统一交给 LineFiles（lines\zh|en|ja，缺了退回中文）
        var lang = settings.Language;
        Voice.Language = lang;   // 本机加（2026-11-18）：说话用哪个语言的音色
        foreach (var file in new[]
        {
            LineFiles.Pick(lang, "touch.txt"),
            LineFiles.Pick(lang, "stroke.txt"),
            LineFiles.Pick(lang, "hover.txt"),
            LineFiles.Pick(lang, "dizzy.txt"),
        })
        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.TrimStart().StartsWith("//")) continue;
                var parts = line.Split('|', 3);
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) continue;
                int level = 0;
                var cond = Condition.Always;
                if (parts.Length == 3 && parts[2].Trim().Length > 0)
                {
                    var condition = Condition.Parse(parts[2], out var bad);
                    if (condition == null) { Places.Log($"{file} 的条件读不懂「{bad}」，这行先不用：{parts[1]}"); continue; }
                    level = condition.MinLevel;
                    cond = condition;   // 本机改（2026-11-19）：原来只取等级，星期/时段这些全丢；存下来给抽签时用
                }
                var key = parts[0].Trim();
                var persona = false;   // 本机加（2026-11-18）：这行是不是人设专属
                // 本机加（2026-11-16）：反应名前面可以带人格前缀「病娇.害羞」，只有切成那个人格时才用这行
                int dot = key.IndexOf('.');
                if (dot > 0)
                {
                    string who = key[..dot];
                    if (Array.IndexOf(PersonaNames, who) >= 0)
                    {
                        if (who != settings.Persona) continue;
                        key = key[(dot + 1)..];
                        persona = true;
                    }
                }
                // 本机改（2026-11-18）：人设专属的台词进 personaTouch，通用的进 touchLines
        var bin = persona ? personaTouch : touchLines;
                if (!bin.TryGetValue(key, out var list)) bin[key] = list = [];
                // 本机加（2026-11-18）：台词里的 {称呼} 换成设置里的称呼
                var lineText = parts[1].Trim();
                if (!string.IsNullOrWhiteSpace(settings.CallMe))
                    lineText = lineText.Replace("{称呼}", settings.CallMe);
                list.Add((lineText, level, cond));
            }
        }
        catch { }
    }

    // skits.txt：「# 名字」开头一个小剧场（名字后面可以加「@条件」，如「# 生日快乐 @10月21日」）；「@道具 单片眼镜 兔牙」可选；之后每行「台词|动作|停留秒数」，台词可空（只做动作）、秒数可省（3.5）。
    private void LoadSkits()
    {
        // 本机加（2026-11-16）：支持热重载——重读前先清空
        skits.Clear(); skitConds.Clear();
        try
        {
            string? name = null; Condition? when = null; var props = new List<string>(); var steps = new List<SkitStep>();
            void Flush()
            {
                if (name != null && steps.Count > 0)
                {
                    skits[name] = new Skit([.. props], [.. steps]);
                    if (when is { Plain: false }) skitConds[name] = when;
                }
                props.Clear(); steps.Clear();
            }
            foreach (var raw in File.ReadAllLines(Path.Combine(Places.Root, "skits.txt")))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                if (line.StartsWith('#'))
                {
                    Flush();
                    var title = line.TrimStart('#').Trim();
                    var at = title.IndexOf('@');
                    name = (at >= 0 ? title[..at] : title).Trim();
                    string? bad = null;
                    when = at >= 0 ? Condition.Parse(title[(at + 1)..], out bad) : Condition.Always;
                    if (when == null) { Places.Log($"skits.txt 的条件读不懂「{bad}」，这个小剧场先不用：{name}"); name = null; }
                    continue;
                }
                if (line.StartsWith("@道具")) { props.AddRange(ParseProps(line[3..])); continue; }
                var parts = line.Split('|', 3);
                var cue = parts.Length > 1 && Body.CueReadable(parts[1].Trim(), 8) ? parts[1].Trim() : null;
                double seconds = parts.Length > 2 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 1, 10) : 3.5;
                if (parts[0].Trim().Length > 0 || cue != null) steps.Add(new SkitStep(parts[0].Trim(), cue, seconds));
            }
            Flush();
        }
        catch { }
    }

    // 戴上临时道具：只戴她现在没戴的；眼镜不叠（主人戴着的、读东西时戴的都算）。seconds 后自动摘。
    private void PutOn(IEnumerable<string> labels, double seconds)
    {
        foreach (var label in labels)
        {
            var expression = Outfit.Accessories.FirstOrDefault(item => item.Label == label || item.Expression == label).Expression;
            if (expression == null || settings.Outfit.Contains(expression) || tempProps.Contains(expression)) continue;
            if (expression is "黑框眼镜" or "单片眼镜")
            {
                bool glassesOn = behavior.GlassesOnForWork || tempProps.Contains("黑框眼镜") || tempProps.Contains("单片眼镜")
                    || settings.Outfit.Contains("黑框眼镜") || settings.Outfit.Contains("单片眼镜");
                if (glassesOn) continue;
            }
            tempProps.Add(expression);
            Send($"exp {expression} 1");
        }
        if (tempProps.Count > 0) { var until = DateTime.UtcNow.AddSeconds(seconds); if (until > propsUntil) propsUntil = until; }
    }

    // 摘掉临时道具；主人用 dress 戴着的不动。
    private void TakeOffProps()
    {
        if (tempProps.Count == 0) return;
        foreach (var expression in tempProps)
            if (!settings.Outfit.Contains(expression)) Send($"exp {expression} 0");
        tempProps.Clear();
    }

    // 待机原版动作开始时：有概率顺手戴个临时道具。
    private void IdlePropsFor(string action)
    {
        if (MutterBlocked || !IdlePropTable.TryGetValue(action, out var item) || random.NextDouble() > item.Chance) return;
        PutOn([item.Prop], Behavior.Moods[action].Seconds + 1);
    }

    // 待机小动作（原版11个）开始时：顺手可能戴个道具，再按概率现问模型嘀咕一句（连动作、道具一起配）。
    private void OnIdleStarted(string action)
    {
        // 本机加（2026-11-16）：先看看机器有没有吃不消（半小时最多念叨一次）
        if (DateTime.UtcNow - sysWorryAt > TimeSpan.FromMinutes(30) && SysInfo.Worry() is { } worry)
        {
            sysWorryAt = DateTime.UtcNow;
            Places.Log("看出机器吃力：" + worry);
            if (settings.Speech) voice.Speak(worry, settings.Persona);
            _ = InTurn(() => { if (!exiting) SayLine(worry); return Task.CompletedTask; }, CancellationToken.None);
            return;
        }
        // 本机加（2026-11-16）：按他现在在干什么说一句（场景台词在 touch.txt，名字就是场景名）
        var scene = Scene();
        if (scene.Length > 0 && touchLines.TryGetValue(scene, out var sceneLines) && sceneLines.Count > 0 && random.Next(2) == 0)
        {
            var pick2 = sceneLines[random.Next(sceneLines.Count)];
            Places.Log("认出现在在：" + scene + " → " + pick2.Text);
            if (settings.Speech) voice.Speak(pick2.Text, settings.Persona);
            _ = InTurn(() => { if (!exiting) SayLine(pick2.Text); return Task.CompletedTask; }, CancellationToken.None);
            return;
        }
        // 本机加（2026-11-16）：一天一次，先把小本本里的旧事提一句
        if (Recall.Line(random) is { } old)
        {
            Places.Log("提旧事：" + old);
            if (settings.Speech) voice.Speak(old, settings.Persona);
            _ = InTurn(() => { if (!exiting) SayLine(old); return Task.CompletedTask; }, CancellationToken.None);
            return;
        }
        IdlePropsFor(action);
        MutterFor(action);
    }

    // 本机加（2026-11-17）：打开／收起对话框（Ctrl+Alt+T）。打字之后她那边的气泡会说，同时留一份聊天记录。
    private void ToggleChat()
    {
        if (chatWindow != null && chatWindow.IsVisible) { chatWindow.Hide(); return; }

        chatWindow ??= new ChatWindow(Http, settings, async text =>
        {
            var said = await Chat.Send(Http, settings.LlmBaseUrl, settings.LlmApiKey, settings.LlmModel, text, settings.Persona);
            if (said == null) return null;

            if (said.Cue != null) body.Apply(said.Cue);
            if (settings.Speech) voice.Speak(said.Text, settings.Persona);
            SayLine(said.Text);
            return said;
        });

        chatWindow.Show();
        chatWindow.Activate();
    }

    private async void MutterFor(string action)
    {
        if (FocusWork) return; // 专注陪伴期间不嘀咕（本机加，2026-11-09）
        if (string.IsNullOrWhiteSpace(settings.LlmApiKey)) return;
        if (random.NextDouble() > settings.MutterChance) return; // 随机概率，避免太吵
        if (MutterBlocked) return;
        int token = ++mutterToken;
        try
        {
            var said = await Mutter.Ask(Http, settings.LlmBaseUrl, settings.LlmApiKey, settings.LlmModel, action, settings.Persona);
            // 等模型回话的这段时间里，她可能已经开口说话了：那就丢掉这句嘀咕。
            if (said == null || token != mutterToken || MutterBlocked) return;
            if (said.Cue != null) { body.Apply(said.Cue); textIdleActive = true; }
            if (said.Props.Length > 0) PutOn(said.Props, 5);
            if (settings.Speech) voice.Speak(said.Text, settings.Persona);
            mutterSpeech.Say(new Line(said.Text, body.Apply));
            await Task.Delay(4000);
            if (token != mutterToken) return;
            mutterSpeech.Stop();
            if (said.Cue != null) { textIdleActive = false; body.Relax(); }
        }
        catch { /* 网络或解析失败就静默 */ }
    }

    // 抽到固定文本：显示文本 + 按文本配的表情动作 + 临时道具。
    private async void PlayTextIdle(string text)
    {
        if (FocusWork) return; // 专注时不嘀咕
        if (MutterBlocked) return;
        int token = ++mutterToken;
        textIdleActive = true;
        if (textCues.TryGetValue(text, out var cue)) body.Apply(cue);
        if (textProps.TryGetValue(text, out var props)) PutOn(props, 6);
        if (settings.Speech) voice.Speak(text, settings.Persona);
        mutterSpeech.Say(new Line(text, body.Apply));
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, text.Length * 0.12 + 1)));
        if (token != mutterToken) return; // 中途被说话打断了，收尾已经做过
        textIdleActive = false;
        mutterSpeech.Stop();
        TakeOffProps();
        body.Relax();
    }

    // 小剧场：几句话 + 动作 + 道具连着演。force：演示用，不受「刚说完要安静」限制。
    private async void PlaySkit(string name, bool force = false)
    {
        if (FocusWork && !force) return; // 专注陪伴期间不演小剧场（连语音一起停）
        if (!skits.TryGetValue(name, out var skit) || (!force && MutterBlocked)) return;
        int token = ++mutterToken;
        skitToken = token; // 演出期间暂时不跟鼠标（Tick 里看 SkitOn）；被打断时 mutterToken 一变就自动算演完
        textIdleActive = true;
        PutOn(skit.Props, skit.Steps.Sum(step => step.Seconds) + 1);
        foreach (var step in skit.Steps)
        {
            if (step.Cue != null) body.Apply(step.Cue);
            // 台词里用全角「｜」隔开几句，每次随机演其中一句（2026-11-06）。
            var said = step.Text.Contains('｜') ? step.Text.Split('｜')[random.Next(step.Text.Split('｜').Length)].Trim() : step.Text;
            if (said.Length > 0) { if (settings.Speech) voice.Speak(said, settings.Persona); mutterSpeech.Say(new Line(said, body.Apply)); } else mutterSpeech.Stop();
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(step.Seconds, said.Length * 0.12 + 0.3)));
            if (token != mutterToken || exiting) return; // 被说话打断了，收尾已经做过
        }
        textIdleActive = false;
        skitToken = 0;
        mutterSpeech.Stop();
        TakeOffProps();
        body.Relax();
    }

    private int skitToken;
    private bool SkitOn => skitToken != 0 && skitToken == mutterToken;

    // 嘀咕现在能不能出来：她在说话、放语音、做哄睡演出，或刚说完不久，都不行。
    private bool MutterBlocked =>
        exiting || speech.IsSaying || silent != null || coaxAutoReturn || textIdleActive || DateTime.UtcNow < mutterQuietUntil || (panelHold && body.Posing);

    // 她要开口了：正在演的嘀咕/小剧场马上收起，在途的作废，临时道具摘掉，说话的优先权给她。
    private void YieldMutter()
    {
        mutterToken++;
        mutterQuietUntil = DateTime.UtcNow.AddSeconds(10);
        mutterSpeech.Stop();
        TakeOffProps();
        foreach (var overlay in poseOverlays) body.Apply("-" + overlay); // 组合神情被打断：临时加的脸上表情也收回
        poseOverlays.Clear();
        if (textIdleActive) { textIdleActive = false; body.Relax(); }
    }

    // 出场/换位：先淡出消失，再到新位置淡入（去掉滑动效果）。mid 在淡出后、换位前做。
    private async void AppearAt(double left, double top, Action? mid = null, int fadeInMs = 220)
    {
        image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        await Task.Delay(140);
        mid?.Invoke();
        Left = left; Top = top;
        image.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(fadeInMs)));
    }

    // A topmost window draws over the taskbar: whatever of her reaches it is not drawn, and clicks there
    // pass through, so she stands behind it.
    private void UpdateCut()
    {
        if (!IsVisible || test) return;
        var work = SystemParameters.WorkArea;
        var monitor = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var window = new Rect(Left, Top, Width, Height);
        double left = work.Left > monitor.Left ? Math.Clamp(work.Left - window.Left, 0, window.Width) : 0;
        double top = work.Top > monitor.Top ? Math.Clamp(work.Top - window.Top, 0, window.Height) : 0;
        double right = work.Right < monitor.Right ? Math.Clamp(work.Right - window.Left, left, window.Width) : window.Width;
        double bottom = work.Bottom < monitor.Bottom ? Math.Clamp(work.Bottom - window.Top, top, window.Height) : window.Height;
        var uncovered = new Rect(left, top, right - left, bottom - top);
        // 本机改（2026-11-12）：按住左键拖动时不挡，全身盖过任务栏好看全貌；放手后再躲回任务栏后面。
        if (dragging && dragged) uncovered = new Rect(0, 0, Width, Height);
        if (uncovered == open) return;
        open = uncovered;
        image.Clip = uncovered == new Rect(0, 0, Width, Height) ? null : new RectangleGeometry(uncovered);
    }

    // Her body takes a moment to change while the window changes at once: she goes out, changes unseen, and comes back.
    private async void SetShape(string id)
    {
        settings.Shape = Shape.Find(id).Id;
        settings.Save();
        if (player is not { } current) return;
        long change = ++shapeChange;
        image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        await Task.Delay(130);
        if (change != shapeChange) return;
        ApplySize();
        current.SetShape(Shape);
        Clamp();
        long seen = current.Frames;
        for (int wait = 0; wait < 40 && current.Frames < seen + 3; wait++) await Task.Delay(20);
        if (change == shapeChange) image.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }


    // 本机加（2026-11-17）：按设置里的写法注册一个热键；写坏了就退回默认键，再不行就记一笔。
    // 本机改（2026-11-18）：先查重复 —— 两个动作配成同一组键时，后注册的必然失败。
    // 原来一律报「被别的程序占用」，其实是自己占了自己，用户根本查不出原因。
    private static readonly Dictionary<(uint Mods, uint Key), string> HotkeyTaken = [];

    private void RegisterHotkey(nint handle, int id, string? configured, string fallback, string what, Action<bool> remember)
    {
        var spec = Hotkeys.Parse(string.IsNullOrWhiteSpace(configured) ? fallback : configured);
        if (!spec.IsValid) spec = Hotkeys.Parse(fallback);
        if (!spec.IsValid) { remember(false); Places.Log($"{what}热键写法看不懂：{configured}"); return; }

        var combo = (spec.Modifiers, spec.VirtualKey);
        var shown = Hotkeys.Describe(spec.Modifiers, spec.VirtualKey);
        if (HotkeyTaken.TryGetValue(combo, out var owner))
        {
            remember(false);
            Places.Log($"{what}热键 {shown} 没注册上：和「{owner}」用了同一组键，改掉一个就行。");
            return;
        }

        var ok = RegisterHotKey(handle, id, spec.Modifiers, spec.VirtualKey);
        remember(ok);
        if (ok) HotkeyTaken[combo] = what;
        else Places.Log($"{what}热键 {shown} 没注册上，可能被别的程序占用了。");
    }

    // 本机加（2026-11-15）：Ctrl+Shift+0 —— 在全身和 Q 版之间来回切。
    private const int ShapeHotkeyId = 0x0F1F;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, Vk0 = 0x30;
    private const int WmHotkey = 0x0312;
    private bool shapeHotkey;
    // 本机加（2026-11-15）：甩出去的惯性、最后一次被碰的时间（长时间不理她自己开口用）
    private double slideVX, slideVY;
    private Point slideMark;
    private long slideMarkAt;
    // 本机加（2026-11-15）：按住不动满 8 秒 —— 切换鞋子
    private DateTime longPressAt;
    private bool longPressDone;
    private DateTime lastTouchAt = DateTime.UtcNow;

    private IntPtr ShapeHotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey)
        {
            switch (wParam.ToInt32())
            {
                case ShapeHotkeyId: handled = true; Dispatcher.BeginInvoke(() => ToggleShape()); break;
                case GlassesHotkeyId: handled = true; Dispatcher.BeginInvoke(() => ToggleGlasses()); break;
                case ShoesHotkeyId: handled = true; Dispatcher.BeginInvoke(() => ToggleShoes()); break;
                case SkirtHotkeyId: handled = true; Dispatcher.BeginInvoke(() => ToggleSkirt()); break;
                case PersonaHotkeyId: handled = true; Dispatcher.BeginInvoke(() => CyclePersona()); break;
            case ChatHotkeyId: handled = true; Dispatcher.BeginInvoke(() => ToggleChat()); break;
            }
        }
        return IntPtr.Zero;
    }

    // 本机加（2026-11-15）：Ctrl+B 戴／摘黑框眼镜，Ctrl+C 穿／脱鞋
    private const int GlassesHotkeyId = 0x0F20, ShoesHotkeyId = 0x0F21, SkirtHotkeyId = 0x0F22, PersonaHotkeyId = 0x0F23, ChatHotkeyId = 0x0F24;
    private const uint VkB = 0x42, VkC = 0x43, VkK = 0x4B, VkX = 0x58, VkT = 0x54;
    private bool glassesHotkey, shoesHotkey, skirtHotkey, personaHotkey, chatHotkey;
    // 本机加（2026-11-17）：对话框
    private ChatWindow? chatWindow;

    // 本机加（2026-11-16）：台词热重载——改完 txt 不用重启，1 秒后自动生效
    // 本机加（2026-11-18）：点她的次数计数。上半身和下半身各数各的，满 3 次就道别。
    // 用户要的：「摸上半身与下面3次不是积累，退出」，次数在设置里能改。
    private int upperTaps, lowerTaps;
    private bool sayQuit;
    // 点满的那种强制退出：生完气就关，不再说道别（用户 2026-11-18 要的）
    private bool forceQuit;
    private DateTime lastTapAt;
    // 本机加（2026-11-19）：点满时先提醒一次的时间点。1.5 秒内再点一下才真退。
    private DateTime? quitWarnAt;
    private FileSystemWatcher? lineWatcher;
    private FileSystemWatcher? linesWatcher;
    private System.Timers.Timer? lineReloadTimer;

    private void WatchLines()
    {
        try
        {
            // 本机改（2026-11-17）：台词搬到 lines\<语言>\ 了，两个地方都要盯
              lineWatcher = new FileSystemWatcher(Places.Root, "*.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
              linesWatcher = new FileSystemWatcher(Path.Combine(Places.Root, "lines"), "*.txt")
              { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, IncludeSubdirectories = true };
            lineReloadTimer = new System.Timers.Timer(800) { AutoReset = false };
            lineReloadTimer.Elapsed += (_, _) => Dispatcher.BeginInvoke(() => ReloadLines());
            void Kick(object sender, FileSystemEventArgs e) { try { lineReloadTimer.Stop(); lineReloadTimer.Start(); } catch { } }
            lineWatcher.Changed += Kick;
            lineWatcher.Created += Kick;
            lineWatcher.Renamed += (s, e) => Kick(s, e);
            lineWatcher.EnableRaisingEvents = true;
            Places.Log("台词热重载：盯着 " + Places.Root + " 的 txt");
        }
        catch (Exception error) { Places.Log("热重载没起来：" + error.Message); }
    }

    private void ReloadLines()
    {
        try
        {
            LoadMutterFile();
            LoadTouchFile();
            LoadSkits();
            Places.Log("台词已热重载");
        }
        catch (Exception error) { Places.Log("热重载失败：" + error.Message); }
    }

    // 本机加（2026-11-16）：Ctrl+Alt+X 在四个人格之间轮换
    // 本机改（2026-11-17）：人设表挪到 Personas.cs，这里只引用
    private static readonly string[] PersonaCycle = Personas.Names;
    internal static readonly string[] PersonaNames = PersonaCycle;

    private void CyclePersona()
    {
        int at = Array.IndexOf(PersonaCycle, settings.Persona);
        settings.Persona = PersonaCycle[(at + 1) % PersonaCycle.Length];
        settings.Save();
        Places.Log("人格换成「" + settings.Persona + "」（Ctrl+Alt+X）");
        LoadTouchFile();
        behavior.Show(Behavior.Moods.ContainsKey("得意") ? "得意" : "歪头", DateTime.UtcNow, 1);
        if (settings.Speech) voice.Speak("现在是" + settings.Persona + "模式哦", settings.Persona);
    }

    private void ToggleGlasses()
    {
        if (Outfit.Expression("黑框眼镜") is not { } expression) return;
        Dress(expression, !settings.Outfit.Contains(expression));
    }

    private void ToggleShoes()
    {
        if (Outfit.Expression("脱鞋") is not { } expression) return;
        Dress(expression, !settings.Outfit.Contains(expression));
    }

    private void ToggleSkirt()
    {
        if (Outfit.Expression("裙子切换") is not { } expression) return;
        Dress(expression, !settings.Outfit.Contains(expression));
    }

    // 本机加（2026-11-15）：三档轮着来 —— 全身 → Q 版 → 小只 → 全身
    private static readonly string[] ShapeCycle = ["full", "chibi", "small"];

    private void ToggleShape()
    {
        int at = Array.IndexOf(ShapeCycle, Shape.Id);
        SetShape(ShapeCycle[(at + 1) % ShapeCycle.Length]);
    }

    private void Tick()
    {
        GoWithAgent();
        if (player is not { } current) return;
        if (DateTime.UtcNow >= nextFullscreenLook)
        {
            nextFullscreenLook = DateTime.UtcNow.AddMilliseconds(500);
            UpdateFullscreen(current);
        }
        if (hiddenForFullscreen) return;
        KeepTopmost();
        // 本机加（2026-11-15）：甩出去之后滑一段 —— 松手时留的速度，每帧衰减，撞到屏幕边就停
        if (slideVX != 0 || slideVY != 0)
        {
            Left += slideVX * .033;
            Top += slideVY * .033;
            slideVX *= .90; slideVY *= .90;
            if (Math.Abs(slideVX) < 15 && Math.Abs(slideVY) < 15) { slideVX = slideVY = 0; Clamp(); SavePlace(); }
        }
        // 本机加（2026-11-15）：好久没人碰她了，就自己找你说一句（在看视频、全屏游戏时不打扰）
        if (DateTime.UtcNow - lastTouchAt > TimeSpan.FromMinutes(12) && !speech.IsSaying && !stroking && !dragging && !behavior.Quiet && !BusyForeground())
        {
            lastTouchAt = DateTime.UtcNow;
            SayTouch("主动");
        }
        LookAtForeground();
        FollowHarnessPersona();
        // 本机加（2026-11-15）：按住她不动满 8 秒 —— 切换鞋子（拖走了就不算）
        if (dragging && !dragged && !longPressDone && DateTime.UtcNow - longPressAt > TimeSpan.FromSeconds(8))
        {
            longPressDone = true;
            ToggleShoes();
        }
        // Moving the window pushes her body; physics carries that into her hair, skirt and tail.
        // 拖动时按抓点压低这个整体平移：抓点那一头才贴在手上（用户 2026-11-12）
        double swayScale = dragging ? GrabbedSway : 1;
        double swayX = (Left - lastLeft) * 30 * swayScale, swayY = (Top - lastTop) * 30 * swayScale;
        lastLeft = Left; lastTop = Top;
        if (swayX != 0 || swayY != 0) current.Send(FormattableString.Invariant($"sway {swayX:0} {swayY:0}"));
        UpdateCut();
        UpdateMouth(current);
        // When the last agent goes, nobody is left to say its work ended. A program on the HTTP interface has no
        // connection to lose: what it starts stays until it says it is done.
        int agents = server?.Connections ?? 0;
        if (agents == 0 && agentsBefore > 0) behavior.ClearActivities();
        agentsBefore = agents;
        UpdateBody();
        MusicTick(); // 有音乐就把跳舞带起来（PetWindow.Dance.cs）
        HoverTick();
        BondTick();
        AlarmTick();
        UpdateFilm();
        speech.Tick();
        mutterSpeech.Tick();
        // 哄睡演示：句子说完、动作演完 3 秒后自动归位。
        if (coaxAutoReturn)
        {
            if (speech.IsSaying) coaxSpeechSeen = true;
            else if (coaxSpeechSeen && coaxReturnAt == null) coaxReturnAt = DateTime.UtcNow.AddSeconds(5);
            if (coaxReturnAt is { } at && DateTime.UtcNow >= at) CoaxReturn();
        }
        // Nothing of her own while she says something or still holds the look she said it with.
        if (speech.IsSaying || silent != null) mutterQuietUntil = DateTime.UtcNow.AddSeconds(10); // 说完后嘀咕再等 10 秒
        if (tempProps.Count > 0 && DateTime.UtcNow >= propsUntil) TakeOffProps(); // 临时道具到点自动摘
        if (!frozen) behavior.Tick(DateTime.UtcNow, speech.IsSaying || silent != null || body.Posing || textIdleActive, settings.IdleSeconds); // 嘀咕/小剧场/事件演着的时候，待机抽签和小动作也别插进来（本机修，2026-11-06）
        // 本机加（2026-11-19）：冻结时视线也不跟——不然调参数时她还一直抬头低头，拧好的值看不见效果。
        if (frozen) return;
        GetCursorPos(out var cursor);
        if (cursor.X != lastCursor.X || cursor.Y != lastCursor.Y)
        {
            lastCursor = cursor;
            lastCursorAt = DateTime.UtcNow;
            mouseIdle = false; // 鼠标动了，恢复互动
        }
        else if (!mouseIdle && DateTime.UtcNow - lastCursorAt > TimeSpan.FromSeconds(5))
        {
            mouseIdle = true; // 鼠标停在同一处超过 5 秒，暂时关掉视线跟随
        }
        // 鼠标停住以后的回正（用户 2026-11-12 要的：更慢更自然，眼睛先回正，头再跟着）：先让眼睛回到正中（渲染器里眼睛有自己的目标和速度），
        // 隔一小会儿再让头慢慢转回来。别的原因的回正（关了跟随、摸头、小剧场）还是原来那样一下回去。
        bool slowReturn = settings.FollowMouse && mouseIdle && !test && !stroking && !(settings.SkitPausesFollow && SkitOn);
        if (slowReturn)
        {
            if (returnStage == 0 && (lookX is 9 || (lookX == 0 && lookY == 0))) return; // 本来就是正的
            var at = DateTime.UtcNow;
            if (returnStage == 0)
            {
                returnStage = 1; returnAt = at;
                current.Send(FormattableString.Invariant($"lookrate {HeadReturnRate} {EyeReturnRate}"));
                current.Send(FormattableString.Invariant($"look {lookX:0.00} {lookY:0.00} 0 0")); // 眼睛先回正，头先不动
            }
            else if (returnStage == 1 && (at - returnAt).TotalSeconds >= HeadReturnDelay)
            {
                returnStage = 2; lookX = lookY = 0;
                current.Send("look 0 0 0 0"); // 头再慢慢转回来
            }
            return;
        }
        if (returnStage != 0)
        {
            // 回正做到一半鼠标又动了（或者别的原因打断）：快慢还原，让下面重新发一次目标（眼睛重新跟着头）
            returnStage = 0;
            current.Send("lookrate 10 10");
            lookX = lookY = 9;
        }
        if (!settings.FollowMouse || mouseIdle || test || stroking || (settings.SkitPausesFollow && SkitOn))
        {
            if (lookX == 0 && lookY == 0) return;
            lookX = lookY = 0;
            current.Send("look 0 0");
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        double headX = (Left + Width / 2) * dpi.DpiScaleX, headY = (Top + Height * Shape.HeadAt) * dpi.DpiScaleY;
        double x = Math.Clamp((cursor.X - headX) / 500, -1, 1), y = Math.Clamp((headY - cursor.Y) / 400, -1, 1);
        if (Math.Abs(x - lookX) + Math.Abs(y - lookY) < .01) return;
        lookX = x; lookY = y;
        current.Send(FormattableString.Invariant($"look {x:0.00} {y:0.00}"));
    }

    // She comes and goes with the agent (the owner, 2026-11-03: the agent closed, she was still there). Once an agent
    // has been connected, she bows out after a while with none, long enough for one that reconnects or restarts its
    // servers. A program on the HTTP interface keeps her while it uses her; started by hand with no agent, she stays.
    private static readonly TimeSpan AloneFor = TimeSpan.FromSeconds(5), HttpUse = TimeSpan.FromSeconds(15); // 本机改（2026-11-12）：最后一个 Agent 一退出就走，缓冲从 1 分钟缩到 5 秒，只够 Agent 重启 MCP 重连
    // 桥接正常关掉时（Agent 自己退了）会先说一声再见：那就不用等满一分钟，几秒就跟着退（用户 2026-11-12 要的）。
    private static readonly TimeSpan FarewellFor = TimeSpan.FromSeconds(3), FarewellFresh = TimeSpan.FromSeconds(20);
    private bool hadAgent;
    private DateTime lastAgentLog;
    private int agentsBefore;
    private DateTime? aloneSince;
    private DateTime lastHttp = DateTime.MinValue;

    /// <summary>关窗口时说一句道别。async 版：给 Leaving() 用，不能阻塞 UI 线程。</summary>
    private async Task SayGoodbyeAsync()
    {
        if (!settings.Speech) return;
        try
        {
            if (!touchLines.TryGetValue("退出", out var all) || all.Count == 0)
            {
                Places.Log("道别台词没找到（touchLines 里没有「退出」）");
                return;
            }
            var line = all[random.Next(all.Count)];
            var said = line.Text;
            if (string.IsNullOrWhiteSpace(said)) return;
            voice.Speak(said, settings.Persona);
            mutterSpeech.Say(new Line(said, body.Apply));
            Places.Log("道别：" + said);
            var wait = Math.Clamp(said.Length * 280 + 1000, 2000, 4500);
            await Task.Delay(wait);
        }
        catch (Exception error) { Places.Log("道别没成：" + error.Message); }
    }

    /// <summary>退出前说一句道别。同步等它念完（最多 4 秒），不然窗口一关声音就断了。</summary>
    private void SayGoodbye()
    {
        if (!settings.Speech) return;
        try
        {
            if (!touchLines.TryGetValue("退出", out var all) || all.Count == 0)
            {
                Places.Log("道别台词没找到（touchLines 里没有「退出」）");
                return;
            }
            var line = all[random.Next(all.Count)];
            var said = line.Text;
            if (string.IsNullOrWhiteSpace(said)) return;
            voice.Speak(said, settings.Persona);
            mutterSpeech.Say(new Line(said, body.Apply));
            Places.Log("道别：" + said);
            // 等它念完或者最多 4 秒
            var wait = Math.Clamp(said.Length * 260 + 900, 1800, 4000);
            var until = DateTime.UtcNow.AddMilliseconds(wait);
            while (DateTime.UtcNow < until && speech.IsSaying) Thread.Sleep(80);
            if (DateTime.UtcNow < until) Thread.Sleep(Math.Min(600, (int)(until - DateTime.UtcNow).TotalMilliseconds));
        }
        catch { }
    }

    private void GoWithAgent()
    {
        if (test || exiting) return;
        // 本机改（2026-11-11）：DSH 每次工具调用可能重开一次 MCP，桥接一断就退场的话托盘小图标也会跟着消失。
        // 留一分钟缓冲：真的把 Agent 关了才会走，只是重连或者闲一会儿不算。还有程序在用网页接口时也留着。
        // 本机改（2026-11-18）：MCP 走的是 HTTP 接口，不走那个 pipe，所以 Connections 一直是 0。
        // 光看 Connections 的话 hadAgent 永远 false，她永远不会随控制台退出——这就是「退出时没说道别」的根因。
        // 所以 HTTP 用过一次也算「接过 Agent」。
        if ((server?.Connections ?? 0) > 0 || lastHttp > DateTime.MinValue)
        {
            if (!hadAgent) Places.Log("接过 Agent 了（Connections=" + (server?.Connections ?? 0) + "，HTTP 用过）");
            hadAgent = true;
            aloneSince = null;
            if ((server?.Connections ?? 0) > 0) return;
        }
        // 本机加（2026-11-18）：诊断——她为什么不退
        if (!hadAgent)
        {
            if (DateTime.UtcNow - lastAgentLog > TimeSpan.FromSeconds(30))
            {
                lastAgentLog = DateTime.UtcNow;
                Places.Log($"还没接过 Agent（Connections={server?.Connections ?? 0}），先留着");
            }
            return;
        }
        // HTTP 接口太久没人用，就等于控制台关了（MCP 每轮都会调一次，闲着就是断了）
        if (DateTime.UtcNow - lastHttp > TimeSpan.FromSeconds(45))
        {
            if (aloneSince == null) Places.Log("HTTP 已经 45 秒没人用了，当作控制台关了");
        }
        else { aloneSince = null; return; }
        // 刚说过再见的（20 秒内）：等 3 秒就走，不用等满一分钟
        bool goodbye = server?.FarewellAt is { } said && DateTime.UtcNow - said < FarewellFresh;
        aloneSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - aloneSince < (goodbye ? FarewellFor : AloneFor)) return;
        Places.Log($"要走了（goodbye={goodbye}），先说道别");
        Places.Log(goodbye ? "Agent 正常退出了，她跟着退下" : "接她的 Agent 都断开了，她先退下");

        // 本机加（2026-11-18）：走之前说一句「明天见」。用户要的是退出这个系统时的道别，
        // 不是点几下就道别——点满那是「点满」那一组台词的事。
        SayGoodbye();
        Close();
    }

    // While another program fills the screen she is hidden and her renderer draws nothing; her words still play on,
    // unseen, and she is back on top once it is gone. Not in a test, which is out of sight anyway.
    private void UpdateFullscreen(Player current)
    {
        string who = "";
        bool overlay = !standby && !test && Fullscreen.OverlayInFront(out who); // 截图/录屏浮层：不管开关，她都让开
        bool covered = standby || overlay || (settings.HideOnFullscreen && !test && Fullscreen.InFront());
        if (covered == hiddenForFullscreen) return;
        if (covered && overlay) Places.Log("有铺满全屏的截图/录屏类浮层在最前（" + who + "），她先让开");
        hiddenForFullscreen = covered;
        current.Send($"pause {(covered ? 1 : 0)}");
        bubble.Quiet = covered || test;
        if (covered) { bubble.Hide(); Hide(); return; }
        Show();
        Topmost = false; Topmost = true;
    }

    // 置顶标记可能在她不知情的时候被拿走（别的“总在最前”工具、系统切全屏、换显示器），
    // 一旦没了，普通窗口就能一直压着她。每秒看一眼，掉了就补回来（补过写一行日志，最多每分钟一行）。
    private void KeepTopmost()
    {
        if (test || standby || !IsVisible || exiting) return;
        if (DateTime.UtcNow < nextTopmostLook) return;
        nextTopmostLook = DateTime.UtcNow.AddSeconds(1);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        if (((long)GetWindowLongPtr(handle, -20) & 0x8) != 0)
        {
            // 置顶标记还在，但置顶层里也分先后：别的“总在最前”的程序被点到，就会盖在她上面（本机加，2026-11-13）。
            if (DateTime.UtcNow < nextCoverRaise) return;
            var cover = CoveredBy(handle);
            if (cover == null) return;
            nextCoverRaise = DateTime.UtcNow.AddMilliseconds(800); // 本机改（2026-11-15）：用户要她永远压在最上面，0.8 秒就补一次
            RaiseAboveAll(handle);
            if (DateTime.UtcNow - topmostLostLogged > TimeSpan.FromMinutes(1))
            {
                topmostLostLogged = DateTime.UtcNow;
                Places.Log("她被别的置顶窗口盖住了，已经抬回最前面；压在上面的是 " + cover);
            }
            return;
        }
        RaiseAboveAll(handle);
        if (DateTime.UtcNow - topmostLostLogged > TimeSpan.FromMinutes(1))
        {
            topmostLostLogged = DateTime.UtcNow;
            Places.Log("窗口的置顶被去掉了（别的程序或系统），已经重新置顶" + Above(handle));
        }
    }

    private DateTime nextCoverRaise;
    // 本机加（2026-11-15）：念出来（Windows 自带中文语音），设置里 speech 开关控制
    private readonly Voice voice = new();
    // 本机加（2026-11-16）：调口型时临时打开，正常关着
    private static readonly bool ShowMouthTrace = Environment.GetEnvironmentVariable("FISHPET_MOUTH_TRACE") == "1";
    private DateTime mouthTraceAt;

    // 置顶层里压在她上面、又和她的窗口重叠的，是哪个程序（没有就返回 null）。
    // 她自己的气泡和手、任务栏/开始菜单这些系统外壳、被隐藏（cloaked）的和最小化的不算。
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp",
    };

    private static string? CoveredBy(nint handle)
    {
        if (!GetWindowRect(handle, out var mine)) return null;
        uint self = (uint)Environment.ProcessId;
        var above = handle;
        for (int step = 0; step < 300 && (above = GetWindow(above, 3)) != 0; step++) // GW_HWNDPREV
        {
            if (!IsWindowVisible(above) || IsIconic(above)) continue;
            if (!GetWindowRect(above, out var other)) continue;
            int width = Math.Min(mine.Right, other.Right) - Math.Max(mine.Left, other.Left);
            int height = Math.Min(mine.Bottom, other.Bottom) - Math.Max(mine.Top, other.Top);
            if (width < 24 || height < 24) continue;
            // 只管“正经的程序窗口”：不进任务栏的浮层（截图框、录制条、各种弹出面板）和铺满整屏的浮层，她不去盖，也不抢着压过去
            if (Fullscreen.IsToolWindow(above) || Fullscreen.FillsMonitor(above)) continue;
            if (DwmGetWindowAttribute(above, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue; // DWMWA_CLOAKED
            GetWindowThreadProcessId(above, out uint id);
            if (id == self) continue;
            try
            {
                var name = Process.GetProcessById((int)id).ProcessName;
                if (!ShellProcesses.Contains(name) && !Fullscreen.IsCaptureTool(name)) return name;
            }
            catch { /* 程序刚好退了：下一秒再看 */ }
        }
        return null;
    }

    // 丢置顶的时候，压在她上面的是哪个程序（只写日志，好查出是谁撤的）。
    private static string Above(nint handle)
    {
        var above = GetWindow(handle, 3); // GW_HWNDPREV
        if (above == 0) return "";
        GetWindowThreadProcessId(above, out uint id);
        try { return "；刚才压在上面的是 " + Process.GetProcessById((int)id).ProcessName; }
        catch { return ""; }
    }

    // 她从不抢焦点（WS_EX_NOACTIVATE），点了也不会像普通窗口那样自己浮上来；置顶的一群窗口里又是最后点到的在最上面。
    // 所以用户碰她的时候，手动把她抬到置顶层的最前面。
    private void RaiseAboveAll()
    {
        if (test || standby || !IsVisible || exiting) return;
        var handle = new WindowInteropHelper(this).Handle;
        // 本机改（2026-11-13）：只有被“正经的程序窗口”压着才抬。托盘的隐藏图标面板、开始菜单、截图录屏的浮层
        // 都不算——点她一下就盖到它们上面，会把用户正在用的面板挡住。
        if (handle != 0 && CoveredBy(handle) != null) RaiseAboveAll(handle);
    }

    private static void RaiseAboveAll(nint handle) =>
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);

    // ---- The user's hands on her ----

    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (exiting || e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        RaiseAboveAll();
        dragging = true; dragged = false;
        longPressAt = DateTime.UtcNow; longPressDone = false;
        lastTouchAt = DateTime.UtcNow;
        GetCursorPos(out var p);
        dragStart = new Point(p.X, p.Y);
        windowStart = new Point(Left, Top);
        CaptureMouse();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
        GetCursorPos(out var p);
        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = (p.X - dragStart.X) / dpi.DpiScaleX, dy = (p.Y - dragStart.Y) / dpi.DpiScaleY;
        if (Math.Abs(dx) + Math.Abs(dy) > 4 && !dragged)
        {
            dragged = true;
            behavior.DragStart();
            TouchDragStarted();
            Happened("drag_start", new { });
        }
        if (dragged) { Left = windowStart.X + dx; Top = windowStart.Y + dy; }
        // 本机加（2026-11-15）：记下最近的拖动速度，松手时当作惯性
        long nowMs = Environment.TickCount64;
        if (slideMarkAt != 0 && nowMs > slideMarkAt && nowMs - slideMarkAt < 150)
        {
            double dtms = nowMs - slideMarkAt;
            slideVX = (p.X - slideMark.X) * 1000.0 / dtms;
            slideVY = (p.Y - slideMark.Y) * 1000.0 / dtms;
        }
        slideMark = new Point(p.X, p.Y); slideMarkAt = nowMs;
        e.Handled = true;
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        e.Handled = true;
        dragging = false;
        ReleaseMouseCapture();
        if (dragged) { Dropped(); return; }
        // The window also gets clicks on her faint edges, which count as missing her.
        var at = e.GetPosition(this);
        if (VisualTreeHelper.HitTest(image, at) == null) return;
        // 本机改（2026-11-16）：双击她 —— 掷骰子小游戏（原来只是吓一跳）
        if (e.ClickCount >= 2)
        {
            YieldMutter();
            int dice = random.Next(1, 7);
            var face = dice == 6 ? "开心" : dice == 1 ? "委屈" : "思考";
            behavior.Show(face, DateTime.UtcNow, at.X / Width < .5 ? 1 : -1, force: true);
            var said = Dice.Normalized(dice);
            if (settings.Speech) voice.Speak(said, settings.Persona);
            _ = InTurn(() => { if (!exiting) SayLine(said); return Task.CompletedTask; }, CancellationToken.None);
            Places.Log("掷骰子：" + dice + " → " + said);
            Happened("dice", new { value = dice });
            return;
        }
        var zone = Behavior.Zone(Shape.Id, at.X / Width, at.Y / Height, settings.Outfit);
        // 本机加（2026-11-19）：把点击位置和判出来的部位记下来——用户反馈「点头却说脚的台词」，
        // 有这行就能倒推出是坐标偏了还是区域边界定得不对。
        Places.Log($"点击 zone={zone} 归一化=({at.X / Width:0.00}, {at.Y / Height:0.00}) 体型={Shape.Id}");
        YieldMutter(); // 点她时嘀咕/小剧场让位，别跟触碰台词抢气泡
        behavior.Click(zone, DateTime.UtcNow, at.X / Width < .5 ? 1 : -1);
        BondTouched(behavior.Showing);

        // 本机加（2026-11-18）：数点击。上半身和下半身分开数，满 N 次就道别。
        // 隔了 8 秒以上算新一轮（不然挂机几小时后随便一点就道别了）。
        // 本机改（2026-11-19）：QuitTaps 设成 0 就是不数了——怎么点都不退，范围放到 0-99。
        var limit = settings.QuitTaps;
        if (limit <= 0)
        {
            SayTouch(behavior.Showing);
            Happened("click", new { zone, reaction = behavior.Showing });
            return;
        }
        if ((DateTime.UtcNow - lastTapAt).TotalSeconds > 8)
        {
            upperTaps = lowerTaps = 0;
            quitWarnAt = null;
        }
        lastTapAt = DateTime.UtcNow;

        var lower = zone is "skirt" or "leg" or "foot" or "tail";
        if (lower) lowerTaps++; else upperTaps++;
        var reached = (lower ? lowerTaps : upperTaps) >= limit;

        if (reached)
        {
            // 本机改（2026-11-19）：点满先问一句，别手滑就把她关了。
            // 1.5 秒内再点一下才算数；超时就作废（计数从头数）。
            var recent = quitWarnAt is { } warned && (DateTime.UtcNow - warned).TotalSeconds <= 1.5;
            if (recent)
            {
                upperTaps = lowerTaps = 0;
                quitWarnAt = null;
                sayQuit = true;
                forceQuit = true;      // 说完就退，由 SayTouch 收尾
            }
            else
            {
                quitWarnAt = DateTime.UtcNow;
                if (lower) lowerTaps = limit - 1; else upperTaps = limit - 1; // 下一击就满
                bubble.Say("再点一下就走了");
                Happened("click", new { zone, reaction = "点满提醒" });
                return;
            }
        }
        SayTouch(behavior.Showing);
        Happened("click", new { zone, reaction = behavior.Showing });
    }

    // 点她时除了身体反应，再念一句对应台词（touch.txt）。用嘀咕的小气泡，嘴跟着字动，按字数停一会儿后收起。
    private async void SayTouch(string? reaction)
    {
        if (reaction?.StartsWith("娇羞") == true) reaction = "娇羞"; // 满档戳裙子的三种反应共用 touch.txt 里「娇羞」的台词
        // 本机加（2026-11-18）：点满了就说「退出」那一组，说完收摊
        if (sayQuit)
        {
            sayQuit = false;
            reaction = "点满";
        }
        if (reaction == null) return;
        // 本机加（2026-11-18）：连点的时候别把好几句话叠在一起说。
        // 每新起一句就把上一句的 token 作废，旧的自己会在下一个断点退出。
        mutterToken++;
        // 本机改（2026-11-18）：人设专属的台词优先——七成用专属的，三成才轮到通用那句。
        // 这样「傲娇」不会说出幼童的话，但通用台词也不至于白写。
        List<(string Text, int Level, Condition Cond)>? all = null;
        if (personaTouch.TryGetValue(reaction, out var mine) && mine.Count > 0 && random.NextDouble() < 0.7)
            all = mine;
        else if (touchLines.TryGetValue(reaction, out var common))
            all = common;
        else if (mine is { Count: > 0 })
            all = mine;   // 只有人设专属的，那就用它
        if (all == null) return;
        // 本机改（2026-11-19）：条件写全了才出现——比如 V我50 那批挂了「周四」，别的日子就抽不到
        var now = DateTime.Now;
        var lines = all.Where(line => line.Level <= affection.Level && line.Cond.Matches(now, affection.Level)).ToArray();
        if (lines.Length == 0) return;
        int token = mutterToken; // 有新的触碰台词、或她开口了，就别让旧的一句乱收气泡
        // 摸头彩蛋的五句：抽完一轮再重抽，尽量不连续重复（用户定的，2026-11-11）
        string said;
        if (reaction == "抚摸头4")
            said = NextMeltLine(lines.Select(line => line.Text).ToArray());
        else
        {
            // 级别要求越高的句子权重越大：到了高等级，多半说黏人的那些
            int pick = random.Next(lines.Sum(line => 1 + 2 * line.Level));
            said = lines[^1].Text;
            foreach (var line in lines) { pick -= 1 + 2 * line.Level; if (pick < 0) { said = line.Text; break; } }
        }
        // 本机加（2026-11-15）：配了模型 key 的话，按 mutterChance 的概率把固定台词换成现编的一句；
        // 但模型慢的时候不能让她干等 —— 超过 2.5 秒就直接用原来那句（本机改，2026-11-15）
        string? liveCue = null;
        if (!string.IsNullOrWhiteSpace(settings.LlmApiKey) && random.NextDouble() < settings.MutterChance)
        {
            var asking = Mutter.AskTouch(Http, settings.LlmBaseUrl, settings.LlmApiKey, settings.LlmModel, reaction, said, settings.Persona);
            var first = await Task.WhenAny(asking, Task.Delay(2500));
            if (first == asking)
            {
                try
                {
                    var spoken = await asking;
                    if (spoken != null && token == mutterToken) { said = spoken.Text; liveCue = spoken.Cue; }
                }
                catch { /* 网络不通就用固定台词 */ }
            }
            if (token != mutterToken) return;
        }
        Func<string, double, char, double> act = liveCue == null ? body.Apply : (_, spoken, tone) => body.Apply(liveCue, spoken, tone);
        // 本机加（2026-11-18）：〔黑脸〕这些是动作，不能念出来。
        // 念的时候只读剥掉〔〕之后的文字；气泡那边还是原文，让身体照做动作。
        var spokenOnly = Body.Strip(said);
        if (settings.Speech) voice.Speak(spokenOnly, settings.Persona);
        mutterSpeech.Say(new Line(said, act));
        var wait = Math.Clamp(said.Length * 250 + 1200, 2500, 6000) / 1000.0;
        await Task.Delay(TimeSpan.FromSeconds(wait));
        if (token != mutterToken || speech.IsSaying) return;
        mutterSpeech.Stop();
        // 本机加（2026-11-18）：点满了 —— 这句黑脸的话已经说完，现在退。
        // 放在这里而不是外面并行等，是因为 SayTouch 自己就知道这句话有多长。
        if (forceQuit)
        {
            await Task.Delay(400);
            Places.Log("点满了，话说完了，退下");
            try { Close(); } catch { }
        }
    }

    // 摸头彩蛋五句的抽签袋：五句一轮，每句各出一次；新一轮尽量避免第一句和上一轮最后一句相同。
    private string NextMeltLine(string[] texts)
    {
        meltBag.RemoveAll(text => !texts.Contains(text));
        if (meltBag.Count == 0)
        {
            meltBag.AddRange(texts);
            for (int i = meltBag.Count - 1; i > 0; i--) // Fisher-Yates
            {
                int j = random.Next(i + 1);
                (meltBag[i], meltBag[j]) = (meltBag[j], meltBag[i]);
            }
            if (meltBag.Count > 1 && meltBag[0] == lastMeltLine)
            {
                int j = 1 + random.Next(meltBag.Count - 1);
                (meltBag[0], meltBag[j]) = (meltBag[j], meltBag[0]);
            }
        }
        var said = meltBag[0];
        meltBag.RemoveAt(0);
        lastMeltLine = said;
        return said;
    }

    private void Dropped()
    {
        TouchDragEnded();
        Clamp();
        SavePlace();
        BondSeen();
        lastTouchAt = DateTime.UtcNow;
        // 本机加（2026-11-15）：松手时把拖动速度留给窗口，接着滑一小段
        slideVX = Math.Clamp(slideVX, -3000, 3000);
        slideVY = Math.Clamp(slideVY, -3000, 3000);
        if (Math.Abs(slideVX) + Math.Abs(slideVY) < 250) { slideVX = slideVY = 0; }
        SnapToEdge();
        double level = dizzy;
        bool dazed = level >= DizzyLow;
        behavior.DragEnd(DateTime.UtcNow, dazed);
        dragMeasured = false; dizzy = 0; dizzyAngle = null;
        AimAllZero();
        springK = 120; springC = 11;
        if (dazed) StartDizzy(level); // 转晕了：放下后晕一会儿，时间随晕的程度（PetWindow.Touch.cs）
        Happened("drag_end", new { dizzy = (int)level });
    }

    private void Happened(string kind, object detail)
    {
        var happening = new Happening(++sequence, DateTime.UtcNow, kind, detail);
        happenings.Add(happening);
        Recorder.Event(kind, detail);
        if (happenings.Count > 100) happenings.RemoveAt(0);
        server?.Publish(kind, detail);
    }

    // ---- What she says ----

    // A new line waits for the one before to be played out, or her body would be cut off mid-move. At most as long as
    // its words take and 20 s more, so a long line or recording plays out but nothing stuck holds her up for good.
    private async Task WaitForLine(CancellationToken token)
    {
        var waited = Stopwatch.StartNew();
        // 本机改（2026-11-16）：以前只等字幕走完，可声音比字幕长——
        // 上一句还在念，下一句就跳出来了，听着像「抢话／延迟」。
        // 现在还要等音频真的放完（voice.Playing 变 false），但最多多等 8 秒，卡住也能走。
        while ((silent != null || speech.IsSaying || voice.Playing) &&
               waited.Elapsed.TotalSeconds < 20 + speech.Spoken + 8) await Task.Delay(50, token);
    }

    // In the order they came, each once the one before has been acted out.
    private async Task InTurn(Func<Task> act, CancellationToken token)
    {
        var turn = new TaskCompletionSource();
        var before = lines;
        lines = turn.Task;
        try { await before; await WaitForLine(token); await act(); }
        finally { turn.SetResult(); }
    }

    private void SayLine(string text, double charactersPerSecond = Line.CharactersPerSecond)
    {
        YieldMutter();
        relaxAt = null;
        panelHold = false; // 她自己开口了：控制台点出来的样子不再强留
        var line = new Line(text, body.Apply, charactersPerSecond);
        if (line.Plain.Trim().Length == 0) { PlaySilently(text); return; }
        StartFilming(line.Plain);
        if (settings.Speech) voice.Speak(line.Plain, settings.Persona);
        speech.Say(line);
    }

    // film: false for what she tries in the mirror, which she sees there already.
    private void PlaySilently(string text, bool filmed = true)
    {
        relaxAt = null;
        silent = new Line(text, body.Apply);
        if (filmed) StartFilming(silent.Plain);
        silentClock.Restart();
        silent.Advance(0);
    }

    // Her own look stays a while after her words, then she relaxes; not in the middle of one of her reflexes.
    private void UpdateBody()
    {
        if (silent is { } line)
        {
            line.Advance(silentClock.Elapsed.TotalSeconds);
            if (line.Done) silent = null;
        }
        if (!body.Posing || silent != null || speech.IsSaying) { relaxAt = null; return; }
        if (coaxHoldPose || panelHold || textIdleActive) { relaxAt = null; return; } // 第三层：保持黑脸盯人；控制台点出来的样子保持到点“恢复”；嘀咕/小剧场/事件演着时由它们自己收尾（以前一句停够 3.5 秒就被这里放松掉，爱心眼刚做出来就没了，本机修 2026-11-06）
        relaxAt ??= DateTime.UtcNow.AddSeconds(LingerSeconds);
        if (DateTime.UtcNow < relaxAt || behavior.Showing != null) return;
        body.Relax();
        relaxAt = null;
    }

    // 她的嘴跟着字动（没有录音了，只看在不在说话）。
    private void UpdateMouth(Player current)
    {
        // 本机改（2026-11-16）：以前是个固定正弦波，跟声音没关系，看着像在空嚼。
        // 现在读真实播放振幅（NAudio 算的 RMS），嘴跟着说话的音量张合。
        // 关键：字幕是按字数估时长走的，往往比真实音频早结束——
        // 所以要同时看 NAudio 还在不在出声，不然声音还在响、嘴已经闭上了。
        var live = voice.Level;
        var speaking = speech.IsTalking || mutterSpeech.IsTalking || live > 0.01f;
        double level = speaking && live > 0.015f ? Math.Clamp(.12 + live * .85, 0, .95) : 0;
        // 调试用：每秒记一次，看 level 到底有没有在变（平时不写日志）
        if (ShowMouthTrace && DateTime.UtcNow - mouthTraceAt > TimeSpan.FromSeconds(1))
        {
            mouthTraceAt = DateTime.UtcNow;
            Places.Log($"mouth level={level:0.00} talking={(speech.IsTalking || mutterSpeech.IsTalking)} naudio={voice.Level:0.00}");
        }
        if (level == mouth) return;
        mouth = level;
        current.Send(FormattableString.Invariant($"mouth {level:0.00}"));
    }

    // ---- Her mirror and her photos ----

    // What she looks like right now, head to waist so her face is big enough to read, on white.
    private BitmapSource? Snapshot(int most = MirrorHeight)
    {
        if (image.Source is not BitmapSource frame || frame.PixelWidth == 0) return null;
        int top = (int)(frame.PixelHeight * Math.Max(0, Shape.HeadTop - .03)), span = Math.Min(frame.PixelHeight - top, (int)(frame.PixelHeight * MirrorSpan));
        var upper = new CroppedBitmap(frame, new Int32Rect(0, top, frame.PixelWidth, span));
        double scale = Math.Min(1, most / (double)span);
        int width = (int)Math.Round(frame.PixelWidth * scale), height = (int)Math.Round(span * scale);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            draw.DrawImage(upper, new Rect(0, 0, width, height));
        }
        var flat = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        flat.Render(visual);
        flat.Freeze();
        return flat;
    }

    // Looks in rows of three, left to right and down in the order they were seen, as a PNG data URL.
    private static string Picture(IReadOnlyList<BitmapSource> shots)
    {
        int columns = Math.Min(MirrorColumns, shots.Count), rows = (shots.Count + columns - 1) / columns;
        int cellWidth = shots.Max(shot => shot.PixelWidth), cellHeight = shots.Max(shot => shot.PixelHeight);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(Brushes.White, null, new Rect(0, 0, cellWidth * columns, cellHeight * rows));
            for (int i = 0; i < shots.Count; i++)
                draw.DrawImage(shots[i], new Rect(i % columns * cellWidth, i / columns * cellHeight, shots[i].PixelWidth, shots[i].PixelHeight));
        }
        var strip = new RenderTargetBitmap(cellWidth * columns, cellHeight * rows, 96, 96, PixelFormats.Pbgra32);
        strip.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(strip));
        using var bytes = new MemoryStream();
        encoder.Save(bytes);
        return "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray());
    }

    private static IReadOnlyList<BitmapSource> Moments(IReadOnlyList<BitmapSource> seen) => seen.Count <= MirrorShots ? seen
        : Enumerable.Range(0, MirrorShots).Select(i => seen[(int)Math.Round(i * (seen.Count - 1) / (double)(MirrorShots - 1))]).ToList();

    // She tries something in front of the mirror: done for real, watched all through, six moments given back.
    private async Task<IReadOnlyList<BitmapSource>> TryInMirror(string pose, CancellationToken token)
    {
        var seen = new List<BitmapSource>();
        await InTurn(async () =>
        {
            PlaySilently(pose.Contains('〔') ? pose : "〔" + pose + "〕", filmed: false);
            var line = silent!;
            var clock = Stopwatch.StartNew();
            // Until it has all played and settled a moment, never longer than six seconds.
            while (clock.Elapsed.TotalSeconds < 6 && !(line.Done && clock.Elapsed.TotalSeconds > line.Until + .3))
            {
                if (Snapshot(MirrorGridHeight) is { } shot) seen.Add(shot);
                await Task.Delay(120, token);
            }
        }, token);
        return Moments(seen);
    }

    private void StartFilming(string said)
    {
        FinishFilming();
        film = []; filmSaid = said; filmEnded = null; filmShot = default;
    }

    // A look every FilmEvery while she says it and a moment after; too long a line keeps every other one.
    private void UpdateFilm()
    {
        if (film is not { } shots) return;
        var now = DateTime.UtcNow;
        if (speech.IsSaying || silent != null) filmEnded = null; else filmEnded ??= now;
        if (now - filmShot >= FilmEvery && Snapshot(MirrorGridHeight) is { } shot)
        {
            shots.Add(shot); filmShot = now;
            if (shots.Count >= FilmFrames) { var kept = shots.Where((_, i) => i % 2 == 0).ToList(); shots.Clear(); shots.AddRange(kept); }
        }
        if (filmEnded is { } ended && now - ended >= FilmAfter) FinishFilming();
    }

    private void FinishFilming()
    {
        var shots = film;
        film = null;
        if (shots is not { Count: > 0 }) return;
        photos.Add(new(DateTime.UtcNow, filmSaid, Picture(Moments(shots))));
        TrimPhotos();
    }

    private void TrimPhotos()
    {
        photos.RemoveAll(photo => DateTime.UtcNow - photo.At > PhotoLife);
        if (photos.Count > PhotosKept) photos.RemoveRange(0, photos.Count - PhotosKept);
    }

    // ---- Leaving ----

    // She bows and fades out while still bowed, rather than vanishing halfway down.
    private async void Leaving(object? sender, CancelEventArgs e)
    {
        if (exitReady) return;
        e.Cancel = true;
        if (exiting) return;
        exiting = true;
        // 本机加（2026-11-18）：关窗口时先说「明天见」，说完再 Stop。
        // 原来这里只有动作（behavior.Show("告别")），一句台词都没有。
        // 点满那种强制退出不说（她已经黑脸生过气了）。
        if (!forceQuit) await SayGoodbyeAsync();
        speech.Stop();
        if (player != null && image.Opacity > 0)
        {
            behavior.Show("告别", DateTime.UtcNow);
            await Task.Delay(1600);
            image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(500)));
            await Task.Delay(600);
        }
        await Task.Yield(); // 收起待命时上面一个 await 都没有，不让出的话 Close() 会落在关闭事件里面而报错（本机修，2026-11-06）
        exitReady = true;
        timeEndPeriod(1);
        Close();
        Application.Current.Shutdown();
    }

    // ---- The agent's tools ----

    // A newer plugin found on GitHub rides along with whatever the agent asks next, once, for it to tell the user.
    private async Task<object> Execute(string name, JsonElement args, CancellationToken token)
    {
        var result = await Dispatcher.InvokeAsync(() => Run(name, args, token)).Task.Unwrap();
        return updates.Unannounced() is { } found ? Tools.WithNotice(result, UpdateNotice(found)) : result;
    }

    // What her body could not read in what she said, and while her lines move only before their words, a reminder to
    // think of her body as she speaks (the owner, 2026-11-05). Her last ten lines since she started.
    private readonly List<bool> acting = [];
    private string Heard(IReadOnlyList<string> lines)
    {
        acting.AddRange(lines.Select(Body.ActsWithin));
        if (acting.Count > 10) acting.RemoveRange(0, acting.Count - 10);
        var unread = Body.Unread(string.Join("\n", lines));
        var missed = unread.Count == 0 ? "" : "身体没跟上：" + string.Join("、", unread.Select(item => "〔" + item.Word + "〕" + (item.Like != null ? "（想写的是〔" + item.Like + "〕？）" : ""))) +
            "，" + (unread.Count > 1 ? "这几个" : "这个") + "没做出来。";
        return missed + (Body.NeedsActing(acting) ? "下次说之前，在心里把这几句演一遍：说到哪儿脸和身子怎么变，就在哪儿写〔〕。" : "");
    }

    private static string UpdateNotice(Updates.Found found) =>
        "【插件有新版本】GitHub 上比现在这份多了 " + found.Behind + " 处改动（新的在前）：" + string.Join("；", found.Changes) +
        "。" + (found.Guide ? "这次身体说明也改了，更新完要把你人设里【你的身体】到【你的身体·完】那段换成新版（“更新插件”第 6 步）。" : "") +
        "找个合适的时候，用一两句话告诉用户有更新、大概改了什么，问他要不要更新；他说要，就照插件 AGENTS.md 最后的“更新插件”做。" +
        "还没更新的话，过一天会再提醒一次；get_state 的 update 里随时能看到。";

    private async Task<object> Run(string name, JsonElement args, CancellationToken token)
    {
        if (exiting) throw new InvalidOperationException("你正在告别，收起之后再叫你出来。");
        if (Recorder.Active && name is not ("get_state" or "recent_events" or "look_back" or "recall"))
            Recorder.Event(name, args.ValueKind == JsonValueKind.Object ? args.Clone() : null); // 录制时记下工具调用（say、dress、show……）
        if (name is not ("get_state" or "quit" or "recent_events") && trouble != null && player == null) throw new InvalidOperationException(trouble);
        switch (name)
        {
            case "say":
                Wake(); // 收着时一开口就出场
                YieldMutter(); // 说话优先：收起正在显示的嘀咕，等待排队的这段时间里也不再嘀咕
                var text = Tools.RequiredText(args, "text");
                var bubbles = Line.Split(text);
                // A line each, in turn; the call returns once the first has begun, the rest wait their turn whoever asked.
                await InTurn(() => { SayLine(bubbles[0]); return Task.CompletedTask; }, token);
                foreach (var next in bubbles.Skip(1))
                    _ = InTurn(() => { if (!exiting) SayLine(next); return Task.CompletedTask; }, CancellationToken.None);
                var note = Heard(bubbles);
                // 兜底切过斜杠就说一声：不然模型下次照样写、永远不知道（本机加，2026-11-14）
                if (Line.HasSlashBreak(text)) note += "这句里的“／”或“ / ”我按换行分成了几个气泡；下次一句一行，直接换行，别写斜杠。";
                return Tools.Text(note.Length == 0 ? new { started = true, bubbles = bubbles.Count, said = Body.Strip(text) }
                    : (object)new { started = true, bubbles = bubbles.Count, said = Body.Strip(text), note });
            case "look_in_mirror":
                if (standby) throw new InvalidOperationException("你现在收起着，先用 show 出场再照镜子。");
                if (hiddenForFullscreen) throw new InvalidOperationException("别的程序正全屏，你藏着没在画，镜子里只会是旧样子；等全屏退出再照（或者先 update_settings hide_on_fullscreen 设成 false）。"); // 本机修，2026-11-08
                var pose = Tools.OptionalText(args, "try");
                IReadOnlyList<BitmapSource> shots = pose != null ? await TryInMirror(pose, token) : Snapshot() is { } now ? [now] : [];
                if (shots.Count == 0) throw new InvalidOperationException("镜子里还什么都看不到，等你出现了再照。");
                return Tools.Image(Picture(shots), new { shots = shots.Count, tried = pose });
            case "look_back":
                int which = args.TryGetProperty("which", out var value) && value.TryGetInt32(out var number) ? number : 1;
                if (film != null && !speech.IsSaying && silent == null) FinishFilming();
                TrimPhotos();
                if (photos.Count == 0) throw new InvalidOperationException("这半小时里还没有拍下你说话的样子。");
                if (which < 1 || which > photos.Count) throw new ArgumentException("只留着最近 " + photos.Count + " 句的样子。");
                var photo = photos[^which];
                int Ago(DateTime at) => (int)(DateTime.UtcNow - at).TotalSeconds;
                return Tools.Image(photo.Picture, new { said = photo.Said, secondsAgo = Ago(photo.At),
                    recent = Enumerable.Range(1, photos.Count).Select(n => new { which = n, said = photos[^n].Said, secondsAgo = Ago(photos[^n].At) }).ToArray() });
            // 本机加（2026-11-18）：动作调试台用 —— 直接改一个 Live2D 参数。
            // name 是参数 ID（ParamAngleX 这种），value 是数值；传 reset=true 就松开手让它自己回去。
            case "set_param":
                var param = Tools.RequiredText(args, "name", 80);
                if (args.TryGetProperty("reset", out var reset) && reset.ValueKind == JsonValueKind.True)
                {
                    player?.Send("clear " + param);
                    return Tools.Text(new { name = param, reset = true });
                }
                if (!args.TryGetProperty("value", out var paramValue) || paramValue.ValueKind != JsonValueKind.Number)
                    throw new ArgumentException("要给 value（数字）。");
                var num = paramValue.GetDouble();
                player?.Send(FormattableString.Invariant($"live {param} {num:0.###}"));
                return Tools.Text(new { name = param, value = num });
            // 本机加（2026-11-19）：动作调试台的前置开关。冻住以后她不再自己动（待机、视线跟随、姿势收回全停），
            // 只剩渲染器把 set_param 按住的值画出来；解冻时把姿势收回来。
            // 本机加（2026-11-19）：重新开始——羁绊归零。设置工具那个「重新开始」按钮走这儿。
            case "reset_affection":
                affection.Reset();
                affection.Save(AffectionFile);
                Places.Log("羁绊已归零（重新开始）");
                return Tools.Text(new { points = affection.Points, level = affection.Level, name = affection.LevelName });
            case "set_freeze":
                if (!args.TryGetProperty("on", out var freezeOn) || freezeOn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("要给 on（true 或 false）。");
                frozen = freezeOn.GetBoolean();
                body.Frozen = frozen;
                if (!frozen) body.Relax();
                return Tools.Text(new { frozen });
            case "set_activity":
                var kind = Tools.RequiredText(args, "kind", 40);
                if (!args.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("要给 active（true 或 false）。");
                behavior.Activity(kind, active.GetBoolean());
                return Tools.Text(new { kind, active = active.GetBoolean() });
            // 本机加（2026-11-15）：弹一张表情包在桌面上，停几秒自己收
            case "show_image":
                if (!args.TryGetProperty("path", out var imagePath) || imagePath.ValueKind != JsonValueKind.String) throw new ArgumentException("要给 path。");
                string imageFile = imagePath.GetString()!;
                if (!System.IO.File.Exists(imageFile)) throw new ArgumentException("找不到这张图：" + imageFile);
                string? imageText = args.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                double imageSeconds = args.TryGetProperty("seconds", out var sec) && sec.TryGetDouble(out var s2) ? Math.Clamp(s2, 2, 30) : 6;
                ShowPicture(imageFile, imageText, imageSeconds);
                return Tools.Text(new { shown = System.IO.Path.GetFileName(imageFile), seconds = imageSeconds });
            case "dress":
                foreach (var label in Tools.Texts(args, "wear").Concat(Tools.Texts(args, "remove")))
                    if (Outfit.Expression(label) == null) throw new ArgumentException("没有“" + label + "”。衣服和配饰见 dress 的说明。");
                foreach (var label in Tools.Texts(args, "wear")) Dress(Outfit.Expression(label)!, true);
                foreach (var label in Tools.Texts(args, "remove")) Dress(Outfit.Expression(label)!, false);
                return Tools.Text(new { wearing = Wearing() });
            case "get_state":
                return Tools.Text(State());
            // 托盘底部的「检查更新」按钮走这里（用户 2026-11-12 要的）：不进 MCP 工具表，只给本机控制台用。
            case "check_update":
                var checkedNow = await updates.Check();
                return Tools.Text(new { state = checkedNow.State, behind = checkedNow.Behind, changes = checkedNow.Changes, error = checkedNow.Error });
            case "update_settings":
                if (args.TryGetProperty("fps", out var fps) && fps.TryGetInt32(out var rate))
                {
                    if (!Settings.FpsChoices.Contains(rate)) throw new ArgumentException("fps 只能是 30、60、90、120。");
                    settings.Fps = rate; Send($"fps {rate}");
                }
                if (args.TryGetProperty("scale", out var scale) && scale.TryGetDouble(out var size))
                {
                    settings.Scale = Math.Clamp(size, .5, 2); ApplySize(); Clamp();
                }
                if (args.TryGetProperty("bubble_scale", out var bubbleScale) && bubbleScale.TryGetDouble(out var bubbleSize))
                {
                    settings.BubbleScale = Math.Clamp(bubbleSize, .5, 2); bubble.Scale = settings.BubbleScale;
                }
                if (args.TryGetProperty("bubble_seconds", out var stay) && stay.TryGetInt32(out var stays))
                {
                    settings.BubbleSeconds = Settings.StayFor(stays); speech.Seconds = settings.BubbleSeconds;
                }
                // Taken up at once: hidden behind a fullscreen program she comes back if it is turned off.
                if (args.TryGetProperty("hide_on_fullscreen", out var hide) && hide.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    settings.HideOnFullscreen = hide.GetBoolean(); nextFullscreenLook = default;
                }
                if (args.TryGetProperty("check_updates", out var check) && check.ValueKind is JsonValueKind.True or JsonValueKind.False) settings.CheckUpdates = check.GetBoolean();
                if (args.TryGetProperty("follow_mouse", out var follow) && follow.ValueKind is JsonValueKind.True or JsonValueKind.False) settings.FollowMouse = follow.GetBoolean();
                if (args.TryGetProperty("start_hidden", out var startHidden) && startHidden.ValueKind is JsonValueKind.True or JsonValueKind.False) settings.StartHidden = startHidden.GetBoolean();
                if (args.TryGetProperty("keep_on_screen", out var keepOn) && keepOn.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    settings.KeepOnScreen = keepOn.GetBoolean();
                    if (settings.KeepOnScreen) { Clamp(); settings.Left = Left; settings.Top = Top; }
                }
                if (args.TryGetProperty("birthday", out var birth) && birth.ValueKind == JsonValueKind.String)
                {
                    var day = birth.GetString();
                    if (string.IsNullOrWhiteSpace(day)) settings.Birthday = "";
                    else settings.Birthday = Condition.NormalBirthday(day) ?? throw new ArgumentException("birthday 写成 10-21 这样（月-日），空字符串是清掉。");
                    Condition.Birthday = settings.Birthday;
                }
                if (args.TryGetProperty("coax_sleep", out var sleep) && sleep.ValueKind is JsonValueKind.True or JsonValueKind.False) settings.CoaxSleep = sleep.GetBoolean();
                if (args.TryGetProperty("coax_lunch", out var lunch) && lunch.ValueKind is JsonValueKind.True or JsonValueKind.False) settings.CoaxLunch = lunch.GetBoolean();
                if (args.TryGetProperty("idle_seconds", out var idle) && idle.TryGetInt32(out var seconds)) settings.IdleSeconds = Math.Clamp(seconds, 15, 600);
                // 跟着音乐跳：这两个原本只在工具说明里写着、这里没接，改了不生效（本机修，2026-11-12）
                if (args.TryGetProperty("music_dance", out var dance) && dance.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    bool on = dance.GetBoolean();
                    if (on != settings.MusicDance)
                    {
                        settings.MusicDance = on;
                        if (on) StartMusic(); else StopMusic();
                    }
                }
                if (args.TryGetProperty("music_apps", out var apps) && apps.ValueKind == JsonValueKind.String)
                    settings.MusicApps = (apps.GetString() ?? "").Trim();
                bool texChanged = false;
                if (args.TryGetProperty("tex_scale", out var texScale) && texScale.TryGetDouble(out var tex))
                {
                    var next = tex >= 1 ? 1 : tex >= 0.5 ? 0.5 : 0.25;
                    if (next != settings.TexScale) { settings.TexScale = next; texChanged = true; }
                }
                settings.Save();
                if (args.TryGetProperty("shape", out var shape) && shape.ValueKind == JsonValueKind.String) SetShape(shape.GetString()!);
                if (texChanged) RestartRenderer();
                return Tools.Text(State());
            case "coax":
                return Coax(args);
            case "recent_events":
                long after = args.TryGetProperty("after", out var since) && since.TryGetInt64(out var mark) ? mark : 0;
                return Tools.Text(happenings.Where(item => item.Sequence > after)
                    .Select(item => new { sequence = item.Sequence, secondsAgo = (int)(DateTime.UtcNow - item.At).TotalSeconds, kind = item.Kind, detail = item.Detail }).ToArray());
            case "quit":
                _ = Dispatcher.BeginInvoke(new Action(Close));
                return Tools.Text(new { leaving = true });
            case "show":
                coaxWoke = false;
                bool came = Wake(greet: true);
                if (!came) RaiseAboveAll(); // 已经在场：把压在她上面的置顶窗口让开
                return Tools.Text(new { hidden = false, came });
            case "hide":
                _ = HideAway();
                return Tools.Text(new { hiding = !standby });
            case "focus":
                return FocusCommand(Tools.RequiredText(args, "action", 10));
            case "remember": case "recall": case "forget": // 记忆小本本（本机加，2026-11-08）
                return MemoryCommand(name, args);
            case "event": // Claude Code hooks 等外面的事件（本机加）：收着不叫她出场，演不演由 events.txt 和她当下忙不忙决定
                return Event(args);
            case "panel":
                Wake(); // 控制台点互动时，收着也出场（和说话一样）；空动作（reset）不会误唤出，因此下面单独处理
                return Panel(args);
            default:
                throw new ArgumentException("没有这个工具。");
        }
    }

    // ───────── 外面发来的事件（本机加，2026-11-06）─────────
    // POST /call/event {"name": "授权"}：Claude Code 的 hooks 一类的外部程序告诉她“发生了什么”，她按 events.txt 演一小下。
    // 不进工具列表（Agent 看不到，也不用 Agent 调）；不花 token；她收着、被全屏盖住、在说话、在演别的时候就不演（返回里写原因）。
    private readonly EventBook events = new();

    private object Event(JsonElement args)
    {
        string name = Tools.RequiredText(args, "name", 20);
        // auto：定时任务自己开的会话发来的（hook 脚本认出来的），不算用户本人在，不记动静、不加亲密度
        bool auto = args.TryGetProperty("auto", out var autoValue) && autoValue.ValueKind == JsonValueKind.True;
        bool restDue = BondEvent(name, auto);
        events.Load(Path.Combine(Places.Root, "events.txt"));
        if (!events.TryGet(name, out var def)) return Tools.Text(new { played = false, reason = "events.txt 里没有这个事件：" + name });
        var now = DateTime.UtcNow;
        string? why =
            standby || hiding || hiddenForFullscreen || test ? "她收着或被全屏盖住，不叫她出场" :
            focus.Now == FocusTimer.Phase.Work && !def.Priority && !FocusEvent(name) ? "在专注，不打扰" :
            events.Cooling(def, now) ? "冷却中" :
            // 在说话、放语音、哄睡演出、被面板摆着姿势：谁都让位
            exiting || speech.IsSaying || silent != null || coaxAutoReturn || (panelHold && body.Posing) ? "她在说话或被摆着姿势" :
            // 平常的事件只在她闲着时演；“优先”的可以打断闲时的嘀咕、小剧场、被碰的反应
            !def.Priority && (MutterBlocked || textIdleActive || behavior.Showing != null) ? "她在忙别的" : null;
        if (why != null) return Tools.Text(new { played = false, reason = why });
        // 连续工作满 2 小时：这一下换成提醒起来动一动
        if (restDue && PlayOwn("该休息")) { streak.Reminded(now); return Tools.Text(new { played = true, instead = "该休息" }); }
        events.Played(def, now);
        PlayEvent(events.Choose(def, random));
        return Tools.Text(new { played = true });
    }

    private async void PlayEvent(EventLine line)
    {
        YieldMutter(); // 收掉正在演的嘀咕/小剧场、摘临时道具，并让后面 10 秒别再冒新的
        int token = ++mutterToken;
        textIdleActive = true;
        if (line.Cue != null) body.Apply(line.Cue);
        if (line.Text.Length > 0) { if (settings.Speech) voice.Speak(line.Text, settings.Persona); mutterSpeech.Say(new Line(line.Text, body.Apply)); }
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(line.Seconds, line.Text.Length * 0.12 + 0.4)));
        if (token != mutterToken || exiting) return; // 中途被说话打断了，收尾已经做过
        textIdleActive = false;
        mutterSpeech.Stop();
        body.Relax();
    }

    // ───────── 托盘控制台的互动（本机加，2026-11-06）─────────
    // 控制台（FishTray）点表情、动作、待机小动作时走这里：直接做出来，不经过“说话”，也不进工具列表（Agent 看不到）。
    // 做出来的样子一直保持，直到点“恢复”或她自己开口说话（SayLine 里放开）。
    private bool panelHold;
    private readonly HashSet<string> panelRaw = [];
    private readonly HashSet<string> poseOverlays = []; // 组合神情临时加的脸上表情，get_state 里先不算，免得托盘高亮跟着乱跳
    // 模型自带、但词表里没有对应的原版表情和动作：加载（脸上转圈的小图标）、前倾（满）、竖食指开；脱捕（循环的原版动作）。
    private static readonly HashSet<string> RawExpressions = ["加载", "前倾", "竖食指开"];
    private static readonly HashSet<string> RawMotions = ["脱捕"];

    private object Panel(JsonElement args)
    {
        string op = Tools.RequiredText(args, "op", 20);
        BondSeen(); // 点托盘也算用户在
        switch (op)
        {
            case "focus": // 番茄钟：开始 / 提前结束（本机加，2026-11-08）
                return FocusCommand(Tools.RequiredText(args, "action", 10));
            case "cue":
                var cue = Tools.RequiredText(args, "cue", 120);
                if (!Body.CueReadable(cue, 12)) throw new ArgumentException("读不懂这几个动作词：" + cue);
                Wake(); YieldMutter(); relaxAt = null; panelHold = true;
                body.Apply(cue);
                break;
            case "pose": // 组合神情：临时摆一下，约 3.5 秒自己恢复，不影响脸上表情开关（本机加 2026-11-06）
                var pose = Tools.RequiredText(args, "cue", 120);
                if (!Body.CueReadable(pose, 12)) throw new ArgumentException("读不懂这几个动作词：" + pose);
                Wake(); YieldMutter(); relaxAt = null;
                var kept = body.Overlays.ToArray();
                body.Apply(pose);
                foreach (var overlay in body.Overlays.Except(kept)) poseOverlays.Add(overlay);
                AutoRelaxPose();
                break;
            case "idle": // 待机小动作、被碰时的反应（Behavior.Moods）
                var mood = Tools.RequiredText(args, "name", 20);
                if (!Behavior.Moods.ContainsKey(mood)) throw new ArgumentException("没有这个小动作：" + mood);
                Wake(); YieldMutter(); relaxAt = null;
                behavior.Show(mood, DateTime.UtcNow, random.Next(2) == 0 ? 1 : -1, force: true);
                break;
            case "exp": // 原版表情开关
                var face = Tools.RequiredText(args, "name", 20);
                if (!RawExpressions.Contains(face)) throw new ArgumentException("没有这个原版表情：" + face);
                bool on = args.TryGetProperty("on", out var wanted) && wanted.ValueKind == JsonValueKind.True;
                Wake(); relaxAt = null; panelHold = true;
                if (on) panelRaw.Add(face); else panelRaw.Remove(face);
                Send($"exp {face} {(on ? 1 : 0)}");
                break;
            case "motion": // 原版动作
                var motion = Tools.RequiredText(args, "name", 20);
                if (!RawMotions.Contains(motion)) throw new ArgumentException("没有这个原版动作：" + motion);
                Wake(); YieldMutter();
                Send($"motion {motion}");
                break;
            case "reset": // 恢复平常：身体、原版表情、正在演的小剧场/嘀咕都收起来
                YieldMutter();
                foreach (var face2 in panelRaw) Send($"exp {face2} 0");
                panelRaw.Clear(); panelHold = false; relaxAt = null;
                body.Relax();
                break;
            default:
                throw new ArgumentException("没有这个操作：" + op);
        }
        return Tools.Text(new { ok = true, overlays = body.Overlays.Except(poseOverlays).ToArray(), raw = panelRaw.ToArray(), hand = body.HandUp, held = body.HeldWords.ToArray() });
    }

    // 组合神情临时摆一下的收尾：只收回这次临时加的脸上表情和会保持的动作，之前开着的脸上表情不动。
    private async void AutoRelaxPose()
    {
        await Task.Delay(3600);
        if (exiting) return;
        foreach (var overlay in poseOverlays) body.Apply("-" + overlay);
        poseOverlays.Clear();
        body.RelaxChannels();
    }

    private void Dress(string expression, bool worn)
    {
        settings.Outfit = worn ? settings.Outfit.Append(expression).Distinct().ToArray() : settings.Outfit.Where(item => item != expression).ToArray();
        Send($"exp {expression} {(worn ? 1 : 0)}");
        behavior.UpdateGlasses();
        settings.Save();
    }

    private string[] Wearing() => Outfit.Clothes.Concat(Outfit.Accessories).Where(item => settings.Outfit.Contains(item.Expression)).Select(item => item.Label).ToArray();

    private object State() => new
    {
        shape = settings.Shape, shapeLabel = Shape.Label, wearing = Wearing(), fps = settings.Fps, scale = settings.Scale, tex_scale = settings.TexScale, bubble_scale = settings.BubbleScale, bubble_seconds = settings.BubbleSeconds,
        hide_on_fullscreen = settings.HideOnFullscreen, hidden_for_fullscreen = hiddenForFullscreen && !standby, hidden = standby, start_hidden = settings.StartHidden, keep_on_screen = settings.KeepOnScreen, follow_mouse = settings.FollowMouse,
        check_updates = settings.CheckUpdates, music_dance = settings.MusicDance, coax_sleep = settings.CoaxSleep, coax_lunch = settings.CoaxLunch, birthday = settings.Birthday,
        update = updates.Latest is { } found ? new { current = found.Current, latest = found.Latest, behind = found.Behind, changes = found.Changes, guide = found.Guide } : null,
        idle_seconds = settings.IdleSeconds, position = new { Left, Top }, saying = speech.IsSaying, persona = settings.Persona, reaction = behavior.Showing, trouble,
        clothes = Outfit.Clothes.Select(item => item.Label).ToArray(), accessories = Outfit.Accessories.Select(item => item.Label).ToArray(),
        overlays = body.Overlays.Except(poseOverlays).ToArray(), raw = panelRaw.ToArray(), hand = body.HandUp, held = body.HeldWords.ToArray(), // 托盘控制台用：脸上开着哪些表情、原版表情、手举着没有
        affection = AffectionState(), focus = FocusState(), memory = MemoryState(), // 亲密度、番茄钟、记忆条数（本机加，2026-11-08）
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    private static nint NoWindowSizeLimit(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0024) return 0; // WM_GETMINMAXINFO
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxSize = new NativePoint { X = 16384, Y = 16384 };
        info.MaxTrackSize = new NativePoint { X = 16384, Y = 16384 };
        Marshal.StructureToPtr(info, lParam, true);
        // 本机修（2026-11-07）：不能 handled = true。WPF 自己也处理这条消息，并从里面读出最大尺寸，排版时用它限制窗口内容的大小。
        // 拦掉以后，系统窗口确实放大了，但 WPF 排版仍按屏幕高度封顶，画面被 Stretch.Fill 压进较矮的区域，看起来就扁了
        // （小只 2 倍、150% 缩放、1600 高的屏幕上约扁 12%）。放行后，WPF 读到的是这里改过的上限。
        return 0;
    }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    // 重新置顶（本机加，2026-11-11）：不改位置大小、不抢焦点，只把窗口放回置顶层的最上面。
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoMove = 0x0001, SwpNoSize = 0x0002, SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out CoverRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct CoverRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    // 摸头的手要更高帧（本机加，2026-11-11）：把计时精度提到 1ms，关闭时恢复
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
    // 本机加（2026-11-15）：Ctrl+Shift+0 切换体型
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
