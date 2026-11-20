// 界面 v2（2026-11-17）：一套自绘控件，颜色全走 Theme。

using System.Drawing.Drawing2D;

namespace SettingTool;

// ───────────────────────────────────── 基础容器
/// <summary>带双缓冲的面板：滚动时整片重绘不再闪、也不再一卡一卡。</summary>
internal sealed class BufferedPanel : Panel
{
    internal BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}

/// <summary>带双缓冲的流容器，页面和卡片内部都用它。</summary>
internal sealed class BufferedFlow : FlowLayoutPanel
{
    internal BufferedFlow()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}

// ───────────────────────────────────── 卡片
/// <summary>圆角白卡片，外面一圈柔和投影。里面是一个从下往上排的流容器，往里 Add 就行。</summary>
internal sealed class Card : Panel
{
    private const int Inset = 3;          // 给投影留的边
    private readonly string? caption;
    private readonly FlowLayoutPanel body;

    internal Card(string? caption = null, int width = 680)
    {
        this.caption = caption;
        Width = width;
        BackColor = Theme.PageFlat;
        Margin = new Padding(0, 0, 0, Theme.Gap);
        // 本机改（2026-11-17）：不再让 AutoSize 自己算。
        // AutoSize 的高度是懒更新的，最后一行说明文字经常画到卡片外面去；
        // 改成按内容首选高度显式设 Height，宽度用最小/最大钉死。
        AutoSize = false;
        MinimumSize = new Size(width, 0);
        MaximumSize = new Size(width, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        body = new BufferedFlow
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Card,
            Location = new Point(18 + Inset, (caption == null ? 16 : 48) + Inset),
            Width = width - 36 - Inset * 2,
        };
        Controls.Add(body);
        body.Layout += (_, _) => Fit();
        Resize += (_, _) => Fit();
        Fit();
    }

    /// <summary>控件的容器，往里 Add。</summary>
    internal FlowLayoutPanel Body => body;

    internal void AddControl(Control control)
    {
        control.BackColor = Theme.Card;
        if (control is FlowLayoutPanel row) row.BackColor = Theme.Card;
        body.Controls.Add(control);
        Fit();
    }

    internal void Clear()
    {
        body.Controls.Clear();
        Fit();
    }

    /// <summary>本机加（2026-11-17）：外面改了宽度之后叫一声，内部按新宽度重排。</summary>
    internal void Refit()
    {
        // 本机改（2026-11-17）：不要在这里重设 MaximumSize，外面已经放开过了
        Fit();
    }

    private bool fitting;

    // 投影缓存：9 圈抗锯齿描边每帧重画太贵，尺寸不变就复用同一张位图
    private Bitmap? shadow;
    private Size shadowSize;

    private void Fit()
    {
        if (Width <= 0 || fitting) return;
        fitting = true;
        try
        {
            var bodyWidth = Width - 36 - Inset * 2;
            if (body.Width != bodyWidth) body.Width = bodyWidth;
            var top = caption == null ? 16 : 48;
            var bottom = caption == null ? 16 : 14;

            // 高度直接按子控件逐个累加，不用 body.PreferredSize：
            // 后者的高度要等一轮布局才更新，算出来常比实际内容矮一行，卡片下边的说明文字就被切了
            var content = 0;
            foreach (Control child in body.Controls)
            {
                if (!child.Visible) continue;
                content += child.Height + child.Margin.Top + child.Margin.Bottom;
            }

            var need = content + top + bottom + Inset * 2;
            if (need != Height) Height = need;

            Padding = new Padding(0, 0, 0, bottom + Inset);
            SyncTableWidths();
        }
        finally
        {
            fitting = false;
        }
    }

    // 表格、按钮网格这类「要占满一行」的控件：宽度跟着卡片内宽走
    // （它们的宽度在页面构造时就按当时的卡宽算死了，窗口一变就溢出被卡片裁掉）
    private void SyncTableWidths()
    {
        if (body == null) return;   // 构造期间 OnResize 会先到，body 还没建
        var inner = Width - Inset * 2 - 36;
        if (inner < 120) return;
        foreach (Control child in body.Controls)
        {
            if (child is NeoTable table)
            {
                if (table.Width != inner) table.Width = inner;
            }
            else if (child is FlowLayoutPanel flow && child.Tag as string == "wide")
            {
                if (flow.Width != inner) flow.Width = inner;
                // MaximumSize 每次赋值都会让 body 重新布局，所以必须先比再赋
                if (flow.MaximumSize.Width != inner) flow.MaximumSize = new Size(inner, flow.MaximumSize.Height);
            }
            else if (child is NeoField field && child.Tag as string == "wide")
            {
                if (field.Width != inner) field.Width = inner;
            }
            else if (child is HintLabel hint)
            {
                hint.SetMaxWidth(inner);
            }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SyncTableWidths();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.PageFlat);

        // 注意：这里绝对不能调 SyncTableWidths。
        // 它会改子控件宽度 → 子控件 Invalidate → 触发父级重绘 → 又一次 Sync → 死循环，
        // 界面就表现为一拖动、一悬停就卡。同步只在 OnResize / Fit 里做。

        var card = new Rectangle(Inset, Inset, Width - Inset * 2 - 1, Height - Inset * 2 - 1);
        if (shadow == null || shadowSize != Size)
        {
            shadow?.Dispose();
            shadow = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using (var gs = Graphics.FromImage(shadow))
            {
                gs.Clear(Color.Transparent);
                Theme.Shadow(gs, card, Theme.RadiusCard, Color.FromArgb(88, 104, 132), 5, 2, 30);   // 本机改：9 圈太贵，5 圈看不出区别
            }
            shadowSize = Size;
        }
        g.DrawImageUnscaled(shadow, 0, 0);

        using var path = Theme.Rounded(card, Theme.RadiusCard);
        using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Line, 1f)) g.DrawPath(pen, path);

        if (caption == null) return;

        // 标题左边一根粉→蓝的小竖条
        var chip = new Rectangle(card.X + 18, card.Y + 17, 4, 15);
        using (var accent = Theme.Accent(new Rectangle(chip.X, chip.Y, chip.Width + 8, chip.Height)))
        using (var chipPath = Theme.Rounded(chip, 2))
            g.FillPath(accent, chipPath);

        Theme.Text(g);
        using var brush = new SolidBrush(Theme.Ink);
        g.DrawString(caption, Theme.Section, brush, card.X + 30, card.Y + 14);

        using var line = new Pen(Theme.LineSoft, 1f);
        g.DrawLine(line, card.X + 18, card.Y + 42, card.Right - 18, card.Y + 42);
    }
}

// ───────────────────────────────────── 按钮
internal enum ButtonKind { Solid, Outline, Ghost }

/// <summary>圆角胶囊按钮：实心（主操作）、描边（次操作）、幽灵（弱操作）。</summary>
internal sealed class NeoButton : Control
{
    private bool hover, down;
    private string caption;

    internal ButtonKind Kind { get; set; } = ButtonKind.Outline;
    internal Color Tint { get; set; } = Theme.Blue;

    internal NeoButton(string text, ButtonKind kind = ButtonKind.Outline, int width = 0)
    {
        caption = text;
        Kind = kind;
        Height = 40;
        Width = width > 0 ? width : Measure(text);
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        Margin = new Padding(0, 0, 10, 0);
        BackColor = Theme.Card;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => caption;
        set { caption = value ?? string.Empty; Invalidate(); }
    }

    private static int Measure(string text)
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)g.MeasureString(text, Theme.Body).Width + 44;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 2);
        if (down) rect.Offset(0, 1);
        using var path = Theme.Rounded(rect, Math.Min(Theme.RadiusButton + 8, rect.Height / 2));

        // 禁用态：灰底灰字。以前不动，只是淡一点的粉色，看着像还能点。
        if (!Enabled)
        {
            using var fill = new SolidBrush(Color.FromArgb(241, 245, 251));
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Line, 1f);
            g.DrawPath(pen, path);

            var disabled = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var inkFaint = new SolidBrush(Theme.InkFaint);
            g.DrawString(caption, Font, inkFaint, new RectangleF(0, 0, Width, Height - 2), disabled);
            return;
        }

        Color ink;
        if (Kind == ButtonKind.Solid)
        {
            // 实心：带一点同色投影，按下去往下沉 1px
            if (!down) Theme.Shadow(g, rect, rect.Height / 2, Tint, 6, 2, 66);

            // 本机改（2026-11-19）：从上到下的浅→深渐变，顶部再压一道白光，更像二次元那套果冻按钮
            var top = down ? Theme.Darken(Tint, .22f) : Theme.Lighten(Tint, .34f);
            var bottom = down ? Theme.Darken(Tint, .06f) : Theme.Lighten(Tint, .04f);
            using var fill = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical);
            g.FillPath(fill, path);
            // 顶部一条高光，塑料感更轻
            using (var glow = new Pen(Color.FromArgb(120, Color.White), 1.4f))
            {
                var shine = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 5, rect.Height / 2);
                using var shinePath = Theme.Rounded(shine, Math.Min(rect.Height / 2, 12));
                g.DrawPath(glow, shinePath);
            }
            ink = Color.White;
        }
        else if (Kind == ButtonKind.Ghost)
        {
            if (hover || down)
            {
                using var fill = new SolidBrush(down ? Theme.BluePale : Theme.CardHover);
                g.FillPath(fill, path);
            }
            ink = hover ? Theme.Blue : Theme.InkSoft;
        }
        else
        {
            using var fill = new SolidBrush(down ? Theme.BluePale : hover ? Theme.CardHover : Color.White);
            g.FillPath(fill, path);
            using var pen = new Pen(hover ? Theme.Blue : Theme.Line, hover ? 1.4f : 1f);
            g.DrawPath(pen, path);
            ink = hover ? Theme.Blue : Theme.Ink;
        }

        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(ink);
        g.DrawString(caption, Font, brush, new RectangleF(0, 0, Width, Height - 2), format);
    }
}

// ───────────────────────────────────── 开关
/// <summary>滑动开关。</summary>
internal sealed class NeoToggle : CheckBox
{
    private bool hover;

    internal NeoToggle(string text, int width = 560)
    {
        Text = text;
        AutoSize = false;
        Width = width;
        Height = 32;
        Font = Theme.Body;
        ForeColor = Theme.Ink;
        BackColor = Theme.Card;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 0, 2);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        var track = new Rectangle(Width - 46, (Height - 22) / 2, 42, 22);
        using var trackPath = Theme.Rounded(track, 11);

        if (Checked)
        {
            using var fill = Theme.Accent(track);
            g.FillPath(fill, trackPath);
            Theme.Shadow(g, track, 11, Theme.PinkDeep, 4, 1, 40);
        }
        else
        {
            using var fill = new SolidBrush(hover ? Color.FromArgb(226, 236, 248) : Color.FromArgb(235, 242, 250));
            g.FillPath(fill, trackPath);
            using var pen = new Pen(Theme.Line, 1f);
            g.DrawPath(pen, trackPath);
        }

        var knobX = Checked ? track.Right - 20 : track.Left + 2;

        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, knobX + 1, track.Top + 3, 16, 16);
        using (var ring = new Pen(Color.FromArgb(Checked ? 90 : 40, Theme.BlueDeep), 1f))
            g.DrawEllipse(ring, knobX + 1, track.Top + 3, 16, 16);

        var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var brush = new SolidBrush(Theme.Ink);
        g.DrawString(Text, Font, brush, new RectangleF(0, 0, Width - 56, Height), format);
    }
}

// ───────────────────────────────────── 页面大标题
internal sealed class PageHead : Control
{
    private readonly string subtitle;

    internal PageHead(string title, string subtitle, int width = 680)
    {
        Text = title;
        this.subtitle = subtitle;
        Width = width;
        Height = 58;
        Margin = new Padding(0, 0, 0, 10);
        BackColor = Theme.PageFlat;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        var bar = new Rectangle(0, 10, 4, 22);
        using (var accent = Theme.Accent(new Rectangle(0, 10, bar.Width + 10, bar.Height)))
        using (var path = Theme.Rounded(bar, 2))
            g.FillPath(accent, path);

        using var brush = new SolidBrush(Theme.Ink);
        g.DrawString(Text, Theme.Title, brush, 14, 6);

        if (string.IsNullOrEmpty(subtitle)) return;
        using var soft = new SolidBrush(Theme.InkSoft);
        g.DrawString(subtitle, Theme.Small, soft, 14, 32);
    }
}
