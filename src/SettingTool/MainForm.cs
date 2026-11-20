// 重写版（2026-11-17）：主窗口。只负责拼装：标题栏 + 侧栏 + 页面 + 底栏。
// 具体每页的内容都在 Pages\ 下各自的文件里。

using System.Drawing.Drawing2D;

namespace SettingTool;

internal sealed partial class MainForm : Form
{
    private readonly SettingsStore store = new();
    private readonly List<(NavItem Item, Control Page)> pages = [];
    private readonly BufferedPanel content = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.PageTop };
    private readonly Label status = new();

    private Point dragFrom;
    private bool dragging;

    internal MainForm()
    {
        Text = "大肥鱼设置";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        // 本机加（2026-11-19）：记住上次的窗口大小——拖一次，以后启动就是这个尺寸。
        Size = LoadWindowSize() ?? new Size(1000, 680);
        MinimumSize = new Size(880, 620);
        BackColor = Theme.PageFlat;
        DoubleBuffered = true;
        Font = Theme.Body;

        Application.AddMessageFilter(new WheelToScroll());

        store.Load();
        ApplyWindowIcon();
        Build();
        LoadIntoPages();

        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => RefreshPetLabel();
        timer.Start();
        RefreshPetLabel();
    }

    // ───────────────────────────────────── 窗口大小记忆（本机加 2026-11-19）

    private static string WindowSizeFile => System.IO.Path.Combine(Program.Root, "cache", "window.json");

    private static Size? LoadWindowSize()
    {
        try
        {
            if (!File.Exists(WindowSizeFile)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(WindowSizeFile));
            var w = doc.RootElement.GetProperty("w").GetInt32();
            var h = doc.RootElement.GetProperty("h").GetInt32();
            if (w < 880 || h < 620 || w > 4000 || h > 3000) return null;
            return new Size(w, h);
        }
        catch { return null; }
    }

    /// <summary>窗口图标（就是 exe 自己的图标）。取不到就返回 null，标题栏会退化成画出来的小鱼。</summary>
    private static Image? LoadLogo()
    {
        var icon = LoadAppIcon();
        return icon?.ToBitmap();
    }

    /// <summary>
    /// 本机加（2026-11-17）：单文件发布时 ExtractAssociatedIcon 常常拿不到图标，
    /// 任务栏就显示成默认的空白页。这里先试 exe 旁边（tools\大肥鱼设置.ico），
    /// 再试 exe 自己，最后试嵌进程序里的那份。
    /// </summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "大肥鱼设置.ico");
            if (File.Exists(beside)) return new Icon(beside, 32, 32);
        }
        catch { }

        try
        {
            var fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (fromExe != null) return fromExe;
        }
        catch { }

        try
        {
            var stream = typeof(MainForm).Assembly.GetManifestResourceStream("SettingTool.AppIcon.ico");
            if (stream != null) return new Icon(stream);
        }
        catch { }

        return null;
    }

    private readonly Image? logo = LoadLogo();

    /// <summary>本机加（2026-11-17）：窗口 Icon 不设的话，任务栏显示的是默认图标。</summary>
    private void ApplyWindowIcon()
    {
        try
        {
            var icon = LoadAppIcon();
            if (icon != null) Icon = icon;
        }
        catch { }
    }

    // 本机加（2026-11-17）：Win11 的圆角窗口。取不到就算了，不影响用。
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var round = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }
        catch { }
    }

    private void Build()
    {
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PageFlat };
        shell.Paint += (_, e) =>
        {
            Theme.PaintPage(e.Graphics, shell.ClientRectangle);
            Theme.PaintBackdrop(e.Graphics, shell.ClientRectangle);
        };
        Controls.Add(shell);

        // 先加 Fill，再加 Bottom / Left / Top，WinForms 的停靠顺序就对了
        shell.Controls.Add(content);
        shell.Controls.Add(BuildFooter());
        shell.Controls.Add(BuildNav());
        shell.Controls.Add(BuildTitleBar());

        // 本机改（2026-11-19）：这里不再挂 BackgroundImage——试过，被页面和卡片的不透明底
        // 层层盖住，图根本看不见。页面底色交给 Theme.PaintPage 画渐变。
        content.Paint += (_, e) => Theme.PaintPage(e.Graphics, content.ClientRectangle);
    }

    // ───────────────────────── 标题栏
    private Panel BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = Theme.TitleHeight, BackColor = Theme.Side };
        bar.MouseDown += (_, e) => { dragging = true; dragFrom = e.Location; };
        bar.MouseMove += (_, e) => { if (dragging) Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); };
        bar.MouseUp += (_, _) => dragging = false;
        bar.Paint += (_, e) => PaintTitleBar(e.Graphics, bar.Width, bar.Height);

        // 右边三个按钮（用流容器排，别手算坐标）
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 124, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Theme.Side, Padding = new Padding(0, 14, 14, 0),
        };
        var min = new CaptionButton("min");
        min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var max = new CaptionButton("max");
        max.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        var close = new CaptionButton("close") { Danger = true };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(min);
        buttons.Controls.Add(max);
        buttons.Controls.Add(close);
        bar.Controls.Add(buttons);
        return bar;
    }

    private void PaintTitleBar(Graphics g, int width, int height)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(Theme.Side);

        // 底下一条很淡的线，和内容区隔开
        using (var line = new Pen(Theme.LineSoft, 1f))
            g.DrawLine(line, 0, height - 1, width, height - 1);

        var box = new Rectangle(18, (height - 30) / 2, 30, 30);
        if (logo != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, box);
            g.InterpolationMode = InterpolationMode.Default;
        }
        else
        {
            using var body = new SolidBrush(Theme.Blue);
            g.FillEllipse(body, box.X, box.Y + 6, box.Width, box.Height - 12);
            using var tail = new SolidBrush(Theme.PinkDeep);
            g.FillPolygon(tail, new[]
            {
                new PointF(box.Right - 4, box.Y + 9), new PointF(box.Right + 5, box.Y + 3),
                new PointF(box.Right + 5, box.Y + 19), new PointF(box.Right - 4, box.Y + 15),
            });
        }

        using var title = new SolidBrush(Theme.Ink);
        g.DrawString("大肥鱼 · 设置", Theme.Title, title, 58, 12);

        var titleWidth = g.MeasureString("大肥鱼 · 设置", Theme.Title).Width;
        using var sub = new SolidBrush(Theme.InkFaint);
        // 本机改（2026-11-19）：这里显示版本号——发布以后有人反馈问题，先问「你那个角落写的是几点几」
        // 用四段完整显示（1.0.0.2），别只取前三段
        var version = typeof(MainForm).Assembly.GetName().Version;
        g.DrawString("v" + version, Theme.Mono, sub, 62 + titleWidth, 19);
    }

    // ───────────────────────── 侧栏
    private Panel BuildNav()
    {
        var nav = new Panel { Dock = DockStyle.Left, Width = Theme.NavWidth, BackColor = Theme.Side };
        // 本机加（2026-11-19）：侧栏是小星星唯一露得出来的地方——它自己也是不透明面板，
        // 画在外层背景上会被它盖掉，只能挂在它自己的 Paint 上。
        nav.Paint += (_, e) => Theme.PaintNavStars(e.Graphics, nav.ClientRectangle);
        nav.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.LineSoft, 1f);
            e.Graphics.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height);
        };

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Theme.Side, AutoScroll = false, Padding = new Padding(0, 8, 0, 8),
        };

        AddGroup(list, "桌宠");
        AddPage(list, "语音", Glyph.Voice, BuildVoicePage());
        AddPage(list, "外观", Glyph.Look, BuildLookPage());
        AddPage(list, "行为", Glyph.Behavior, BuildBehaviorPage());
        AddPage(list, "模型", Glyph.Model, BuildModelPage());


        AddGroup(list, "互动");
        AddPage(list, "对话", Glyph.Chat, BuildChatPage());
        AddPage(list, "音乐", Glyph.Music, BuildMusicPage());
        AddPage(list, "小剧场", Glyph.Skit, BuildSkitPage());
        AddPage(list, "录 GIF", Glyph.Record, BuildRecordPage());

        AddGroup(list, "系统");
        AddPage(list, "快捷键", Glyph.Hotkey, BuildHotkeyPage());
        AddPage(list, "日历", Glyph.Hotkey, BuildCalendarPage());
        AddPage(list, "其它", Glyph.Other, BuildOtherPage());

        nav.Controls.Add(list);
        // 本机加（2026-11-18）：SETTINGTOOL_PAGE 指定启动时停在哪一页（0 起）。
        // 以前没实现，截图永远停在第一页，只能靠鼠标点。
        var start = 0;
        var env = Environment.GetEnvironmentVariable("SETTINGTOOL_PAGE");
        if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var at)) start = at;
        if (pages.Count > 0) Select(pages[Math.Clamp(start, 0, pages.Count - 1)]);
        return nav;
    }

    private static void AddGroup(FlowLayoutPanel nav, string title) => nav.Controls.Add(new NavGroup(title));

    private void AddPage(FlowLayoutPanel nav, string title, Glyph glyph, Control page)
    {
        var item = new NavItem(title, glyph);
        item.Click += (_, _) => Select((item, page));
        nav.Controls.Add(item);
        pages.Add((item, page));
        content.Controls.Add(page);
        page.Visible = false;
    }

    private void Select((NavItem Item, Control Page) pick)
    {
        foreach (var (item, page) in pages)
        {
            item.Active = item == pick.Item;
            page.Visible = page == pick.Page;
        }
        pick.Page.BringToFront();

        // 隐藏着的页面不再跟着窗口重排（那是拖窗口卡顿的主因），
        // 所以切过去的时候要补一次——只排这一页，几毫秒的事。
        if (pick.Page is FlowLayoutPanel flow)
        {
            foreach (Control child in flow.Controls)
                if (child is Lane lane) lane.Fit(CardWidth);
        }
    }

    // ───────────────────────── 底栏
    private Panel BuildFooter()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.FooterHeight, BackColor = Theme.Side };
        bar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Side);

            // 上方一点点投影，让底栏和内容区有层次
            for (var i = 1; i <= 6; i++)
            {
                using var pen = new Pen(Color.FromArgb(16 - i * 2 > 0 ? 16 - i * 2 : 1, Theme.BlueDeep), 1f);
                g.DrawLine(pen, 0, i - 1, bar.Width, i - 1);
            }
            using var line = new Pen(Theme.LineSoft, 1f);
            g.DrawLine(line, 0, 0, bar.Width, 0);
        };

        // 本机改（2026-11-17）：原来是「固定 780 宽的按钮列 + 右侧 180 宽状态」，
        // 窗口一窄两者就叠在一起（"打开目录"被"桌宠已启动"压在下面）。
        // 改成两列网格：左列按内容自适应，右列吃掉剩余宽度，永远不会重叠。
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Theme.Side, Padding = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Theme.Side, Padding = new Padding(20, 15, 8, 0),
        };

        var save = new NeoButton("保存") { Width = 92 };   // 用户要：底部按钮一律普通白底
        save.Click += (_, _) => { SaveAll(); Flash(save, "已保存"); };

        var reload = new NeoButton("重新读取") { Width = 104 };
        reload.Click += (_, _) => { store.Load(); LoadIntoPages(); Note("已重新读取"); };

        var restart = new NeoButton("保存并重启") { Width = 116 };   // 原来是紫实心，跟「保存」抢主次；改成白底
        restart.Click += (_, _) => { SaveAll(); PetHost.Restart(); Note("已重启桌宠"); };

        var pet = new NeoButton("桌宠") { Width = 104 };
        pet.Click += (_, _) =>
        {
            if (PetHost.Running) { PetHost.Stop(); Note("桌宠已停止"); }
            else { SaveAll(); PetHost.Start(); Note("桌宠已启动"); }
            RefreshPetLabel();
        };
        petToggle = pet;

        var folder = new NeoButton("打开目录") { Width = 104 };   // 原来是 Ghost，淡到看不见
        folder.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Program.Root) { UseShellExecute = true }); } catch { }
        };

        row.Controls.Add(save);
        row.Controls.Add(reload);
        row.Controls.Add(restart);
        row.Controls.Add(pet);
        row.Controls.Add(folder);

        status.Dock = DockStyle.Fill;
        status.AutoSize = false;
        status.TextAlign = ContentAlignment.MiddleRight;
        status.ForeColor = Theme.InkSoft;
        status.Font = Theme.Small;
        status.BackColor = Theme.Side;
        status.Padding = new Padding(0, 0, 22, 0);

        grid.Controls.Add(row, 0, 0);
        grid.Controls.Add(status, 1, 0);
        bar.Controls.Add(grid);
        return bar;
    }

    private NeoButton? petToggle;

    private void RefreshPetLabel()
    {
        if (petToggle == null) return;
        petToggle.Text = PetHost.Running ? "停掉桌宠" : "启动桌宠";
    }

    internal void Note(string text) => status.Text = text;

    internal void Flash(NeoButton button, string text)
    {
        var back = button.Text;
        button.Text = text;
        Note(text);
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); button.Text = back; };
        timer.Start();
    }

    // 防止窗口按钮拖动时报错
    // 本机加（2026-11-17）：关窗时把音乐停掉，别留个后台在响
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopPlayback();
        try { player?.Dispose(); } catch { }
        // 本机加（2026-11-19）：关窗时记下大小，下次启动照这个开
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowSizeFile)!);
            File.WriteAllText(WindowSizeFile, $"{{\"w\":{Width},\"h\":{Height}}}");
        }
        catch { }
        base.OnFormClosing(e);
    }

    // 本机加（2026-11-18）：FormBorderStyle.None 时系统压根不给缩放，得自己把
    // WS_THICKFRAME 加回去（它只加可缩放能力，不会画系统边框）。
    private const int WsThickFrame = 0x00040000;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= WsThickFrame;
            return cp;
        }
    }

    // 本机加（2026-11-18）：无边框窗口默认拖不了边。这里自己回 WM_NCHITTEST，
    // 鼠标落在窗口外圈 6px 就交给系统当缩放热区（左右上下四个边 + 四个角）。
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13;
    private const int HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HtClient)
            {
                // 低 16 位是 x，高 16 位是 y（屏幕坐标，可能是负数，所以要按 short 取）
                var screen = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                var local = PointToClient(screen);
                const int grip = 6;
                var left = local.X <= grip;
                var right = local.X >= ClientSize.Width - grip;
                var top = local.Y <= grip;
                var bottom = local.Y >= ClientSize.Height - grip;

                if (top && left) m.Result = HtTopLeft;
                else if (top && right) m.Result = HtTopRight;
                else if (bottom && left) m.Result = HtBottomLeft;
                else if (bottom && right) m.Result = HtBottomRight;
                else if (left) m.Result = HtLeft;
                else if (right) m.Result = HtRight;
                else if (top) m.Result = HtTop;
                else if (bottom) m.Result = HtBottom;
            }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // 本机加（2026-11-17）：窗口拉大以后卡片还是 680 宽，右边空一大片。
        // 注意两点：
        //   1. 构造里设 Size 也会走到这儿，那时句柄还没建，BeginInvoke 会直接抛异常；
        //   2. resize 事件里子容器的 ClientSize 还是旧值，当场量会算错——所以要再排一次队。
        if (!IsHandleCreated) return;
        Relayout();
        try { BeginInvoke(new Action(Relayout)); } catch { }
    }
}

/// <summary>把滚轮转给鼠标底下能滚的容器（WinForms 的 Panel 默认收不到滚轮）。</summary>
internal sealed class WheelToScroll : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        var control = Control.FromHandle(WindowFromPoint(Cursor.Position));
        while (control != null && control is not ScrollableControl { AutoScroll: true })
            control = control.Parent;
        if (control is not ScrollableControl scrollable) return false;

        var notches = (short)((long)m.WParam >> 16);
        if (notches == 0) return false;

        // 本机改（2026-11-17）：以前一格滚轮发 3 次 WM_VSCROLL，每次都重绘整页，
        // 滚起来一顿一顿的。改成一次把位置设到位——只重绘一次。
        var visible = scrollable.ClientSize.Height;
        var step = Math.Max(48, visible / 4);          // 一格大约四分之一屏，太快容易晕
        var current = -scrollable.AutoScrollPosition.Y; // AutoScrollPosition 是负值
        var target = Math.Clamp(current - notches * step, 0, scrollable.DisplayRectangle.Height);
        scrollable.AutoScrollPosition = new Point(0, target);
        return true;
    }
}
