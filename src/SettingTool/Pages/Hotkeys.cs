// 重写版（2026-11-17）：快捷键页 —— 可以自己改键。
// 点一下键帽就进入「按键捕获」，按下新的组合就换掉；写进 settings.json 后重启桌宠生效。

namespace SettingTool;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, KeyCap> hotkeyCaps = [];
    // 本机加（2026-11-18）：注册失败的热键（从 errors.log 读）
    private Dictionary<string, string> hotkeyConflicts = [];

    // 本机加（2026-11-18）：最后一列是「这个键要靠哪些表情」——
    // 换了别人的模型，这些表情名多半不存在，按了就是没反应，得标出来。
    private static readonly (string Key, string Title, string What, string Default, string[] Needs)[] HotkeyDefs =
    [
        ("HotkeyShape", "换体型", "小只 → Q 版 → 全身，循环", "Ctrl+Shift+0", ["体型1", "体型2"]),
        ("HotkeyPersona", "换人格", "人妻 → 傲娇 → 童真 → 病娇，循环", "Ctrl+Alt+X", []),
        ("HotkeyGlasses", "换眼镜", "黑框 → 单片 → 不戴，循环", "Ctrl+Alt+B", ["黑框眼镜", "单片眼镜"]),
        ("HotkeyShoes", "换鞋", "脱鞋 / 穿鞋", "Ctrl+Alt+C", ["脱鞋"]),
        ("HotkeySkirt", "换裙子", "长裙 / 短裙", "Ctrl+Alt+K", ["裙子切换"]),
        ("HotkeyChat", "对话框", "打开 / 收起聊天窗口", "Ctrl+Alt+T", []),
    ];

    private FlowLayoutPanel BuildHotkeyPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("快捷键", "点键帽可以改成别的组合；改完点「保存并重启」才生效。", CardWidth));

        // 本机加（2026-11-18）：先读日志，看哪几个键被别的软件占了
        hotkeyConflicts = ReadHotkeyConflicts();
        var expressions = ModelExpressions();
        if (hotkeyConflicts.Count > 0)
        {
            var warn = new Card("有键没能注册上", CardWidth);
            warn.AddControl(Hint("下面这几个组合被别的软件占用了 —— 桌宠按不到。换一个组合就好：\n"
                + string.Join("\n", hotkeyConflicts.Select(x => "  · " + x.Key + "  " + x.Value))));
            Mount(page, warn);
        }

        var keys = new Card("桌宠热键", CardWidth);
        keys.AddControl(Hint("点一下键帽 → 按下想要的组合（要带 Ctrl / Alt / Shift）→ 松手就记下。按 Esc 取消。"));

        foreach (var (key, title, what, fallback, needs) in HotkeyDefs)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Card,
                Margin = new Padding(0, 4, 0, 4),
            };

            var cap = new KeyCap(fallback, 190) { Capturable = true };
            cap.Bind(title, fallback);
            // 本机加（2026-11-18）：这个键注册失败过就标红
            if (hotkeyConflicts.TryGetValue(fallback, out var why)) cap.Conflict = why;
            hotkeyCaps[key] = cap;
            row.Controls.Add(cap);
            row.Controls.Add(Tag(title, 90));
            // 本机加（2026-11-18）：当前模型没这些表情的话，说明一下
            var missing = needs.Length > 0 && !needs.Any(n => expressions.Contains(n));
            row.Controls.Add(new Label
            {
                Text = missing ? what + "　（当前模型没有这个动作）" : what,
                AutoSize = false,
                Width = 420,
                Height = 34,
                ForeColor = missing ? Theme.Red : Theme.InkSoft,
                Font = Theme.Small,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 0, 0),
            });

            keys.AddControl(row);
        }

        var reset = new NeoButton("恢复默认键");
        reset.Click += (_, _) =>
        {
            foreach (var (key, _, _, fallback, _) in HotkeyDefs)
                if (hotkeyCaps.TryGetValue(key, out var cap)) cap.Text = fallback;
            Note("已经恢复成默认键，记得保存");
        };
        keys.AddControl(Row(reset));
        Mount(page, keys);

        var tips = new Card("注意", CardWidth);
        tips.AddControl(Hint("· 一定要带修饰键（Ctrl / Alt / Shift），不然会把普通打字吃掉\n· 被别的软件占用的组合按了不会有反应，换一个就行\n· 同一组键别给两个动作用"));
        Mount(page, tips);

        return page;
    }

    /// <summary>当前模型有哪些表情（读 Exp3\*.exp3.json 的文件名）。</summary>
    private static HashSet<string> ModelExpressions()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var settings = Path.Combine(Program.Root, "settings.json");
            if (!File.Exists(settings)) return names;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings));
            var model = doc.RootElement.TryGetProperty("ModelPath", out var mv) ? mv.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(model)) return names;
            // ModelPath 是相对 model\ 的（比如 "DeepSeek\\大肥鱼.model3.json"）
            var full = Path.Combine(Program.Root, "model", model.Replace('/', Path.DirectorySeparatorChar));
            var folder = Path.GetDirectoryName(full);
            if (folder == null) return names;
            var expDir = Path.Combine(folder, "Exp3");
            if (!Directory.Exists(expDir)) return names;
            foreach (var path in Directory.GetFiles(expDir, "*.exp3.json"))
            {
                var name = Path.GetFileName(path);
                names.Add(name[..^".exp3.json".Length]);
            }
        }
        catch { }
        return names;
    }

    /// <summary>从 errors.log 里找「<名字>热键 <组合> 没注册上」，返回 组合 → 说明。</summary>
    private static Dictionary<string, string> ReadHotkeyConflicts()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var log = Path.Combine(Program.Root, "errors.log");
            if (!File.Exists(log)) return found;
            // 只读最后 400 行，日志可能很长
            var lines = File.ReadLines(log).TakeLast(400).ToArray();
            foreach (var line in lines.Reverse())
            {
                // 只看最后一次启动之后的：遇到「台词热重载」说明是新的一轮
                if (line.Contains("台词热重载")) break;
                // 两种文案都认：
                //   「换鞋热键 Ctrl+Alt+N 没注册上，可能被别的程序占用了。」← 真被占了
                //   「换鞋热键 Ctrl+Alt+N 没注册上：和「换人格」用了同一组键，改掉一个就行。」← 自己重复
                var match = System.Text.RegularExpressions.Regex.Match(line, @"(\S+)热键\s+(\S+)\s+没注册上([:：].*)?");
                if (!match.Success) continue;
                var key = match.Groups[2].Value.Trim();
                var who = match.Groups[1].Value.Trim();
                var why = match.Groups[3].Success && match.Groups[3].Value.Length > 2
                    ? match.Groups[3].Value.TrimStart(':', '：', ' ')
                    : "这个键被别的软件占了";
                if (!found.ContainsKey(key)) found[key] = why;
            }
        }
        catch { }
        return found;
    }
}

// ───────────────────────────────────── 可点的键帽
/// <summary>画成键盘键的样子；Capturable 时可以点进去捕获新组合。</summary>
internal sealed class KeyCap : Control
{
    private string caption;
    private string title = "";
    private bool capturing;
    private bool hover;
    // 本机加（2026-11-18）：进捕获前的值，取消时恢复
    private string? beforeCapture;
    // 本机加（2026-11-18）：捕获超时 —— 不按键的话 10 秒自己退出
    private System.Windows.Forms.Timer? CaptureWatch;

    internal bool Capturable { get; set; }
    // 本机加（2026-11-18）：这个键注册失败过（被别的软件占了），画红提醒
    internal string? Conflict { get; set; }

    internal KeyCap(string text, int width = 180)
    {
        caption = text;
        Width = width;
        Height = 34;
        Margin = new Padding(0, 2, 10, 0);
        BackColor = Theme.Card;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
    }

    internal void Bind(string name, string fallback)
    {
        title = name;
        caption = fallback;
    }

    // 本机改（2026-11-17）：与基类 Control.Text 的可空性标注对齐，消掉 CS8765
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => caption;
        set { caption = value ?? string.Empty; Invalidate(); }
    }

    internal string Caption => caption;

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Capturable)
        {
            if (!capturing)
            {
                beforeCapture = caption;
                StartCaptureWatch();
            }
            capturing = true;
            Focus();
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    /// <summary>本机加（2026-11-18）：捕获时的兜底 —— 失焦 / 鼠标离开 3 秒 / 总共 10 秒，任一条到了就取消。</summary>
    private void StartCaptureWatch()
    {
        CaptureWatch?.Stop();
        CaptureWatch?.Dispose();
        var elapsed = 0;
        var away = 0;
        CaptureWatch = new System.Windows.Forms.Timer { Interval = 500 };
        CaptureWatch.Tick += (_, _) =>
        {
            if (!capturing) { CaptureWatch?.Stop(); return; }
            elapsed += 500;
            if (!hover) away += 500;
            else away = 0;
            // 10 秒没结果，或者鼠标移开 3 秒
            if (elapsed >= 10000 || away >= 3000) CancelCapture();
        };
        CaptureWatch.Start();
    }

    /// <summary>本机加（2026-11-18）：取消捕获，值恢复成原来的。</summary>
    private void CancelCapture()
    {
        capturing = false;
        if (beforeCapture != null) caption = beforeCapture;
        CaptureWatch?.Stop();
        CaptureWatch?.Dispose();
        CaptureWatch = null;
        Invalidate();
    }

    // 本机加（2026-11-18）：点了别处（失焦）也取消，别一直挂着「按下新的组合…」
    protected override void OnLostFocus(EventArgs e)
    {
        if (capturing) CancelCapture();
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!capturing) { base.OnKeyDown(e); return; }

        // 单按 Esc 取消
        if (e.KeyCode == Keys.Escape)
        {
            CancelCapture();
            base.OnKeyDown(e);
            return;
        }

        // 只按修饰键不算，等主键
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            Invalidate();
            return;
        }

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        if (parts.Count == 0)
        {
            // 没有修饰键就不收，免得把普通打字吃了
            caption = "要带 Ctrl / Alt / Shift";
            Invalidate();
            return;
        }

        parts.Add(KeyName(e.KeyCode));
        caption = string.Join("+", parts);
        capturing = false;
        CaptureWatch?.Stop();
        CaptureWatch?.Dispose();
        CaptureWatch = null;
        Invalidate();
        base.OnKeyDown(e);
    }

    private static string KeyName(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z) return key.ToString();
        if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (key >= Keys.F1 && key <= Keys.F24) return key.ToString();
        return key switch
        {
            Keys.Space => "Space",
            Keys.Enter => "Enter",
            Keys.Tab => "Tab",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            _ => key.ToString(),
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 3);
        using var path = Theme.Rounded(rect, 8);

        using (var shade = new SolidBrush(Theme.Line))
        using (var shadePath = Theme.Rounded(new Rectangle(0, 2, Width - 1, Height - 2), 8))
            g.FillPath(shade, shadePath);

        var bad = Conflict != null;
        var fill = capturing ? Theme.BluePale : bad ? Color.FromArgb(255, 245, 245) : hover && Capturable ? Theme.CardHover : Color.White;
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(capturing ? Theme.Pink : bad ? Theme.Red : hover && Capturable ? Theme.Blue : Theme.Line, bad ? 1.6f : capturing ? 1.8f : 1.2f))
            g.DrawPath(pen, path);

        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var ink = new SolidBrush(bad ? Theme.Red : capturing ? Theme.PinkDeep : Theme.Ink);
        var text = capturing ? "按下新的组合…" : caption;
        g.DrawString(text, Theme.Mono, ink, new RectangleF(0, 0, Width, Height - 3), format);
    }
}
