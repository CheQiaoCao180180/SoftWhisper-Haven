// 界面 v2（2026-11-17）：输入类控件 + 滑块 + 侧边导航 + 标题栏按钮。
// 全部自绘，颜色只走 Theme。

using System.Drawing.Drawing2D;

namespace SettingTool;

// ───────────────────────────────────── 输入框外壳
/// <summary>圆角输入框外壳，把 TextBox / NumericUpDown / ComboBox 包进去。
/// 对外转发 Text / Value / Items / SelectedIndex，页面代码照常用。</summary>
internal sealed class NeoField : Panel
{
    private bool focused;
    private readonly Control inner;
    private readonly bool isCombo;
    private readonly bool isNumeric;

    internal NeoField(Control inner, int width)
    {
        this.inner = inner;
        Width = width;
        Height = 40;
        BackColor = Theme.Card;
        Margin = new Padding(0, 2, 12, 6);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        inner.BackColor = Color.White;

        if (inner is ComboBox combo)
        {
            // 本机改（2026-11-17）：下拉框自带那个灰扑扑的箭头按钮，FlatStyle 也去不掉。
            // 办法是把 combo 放得比外壳宽，多出来的部分（正好是箭头）被父容器裁掉，
            // 箭头改由外壳自己画一个细的 V。
            isCombo = true;
            combo.FlatStyle = FlatStyle.Flat;
            var host = new Panel { BackColor = Color.White, Left = 12, Top = 4, Width = Width - 42, Height = Height - 8 };
            combo.SetBounds(-2, Math.Max(0, (host.Height - combo.PreferredHeight) / 2), host.Width + 36, combo.PreferredHeight);
            host.Controls.Add(combo);
            Controls.Add(host);
            combo.GotFocus += (_, _) => { focused = true; Invalidate(); };
            combo.LostFocus += (_, _) => { focused = false; Invalidate(); };
            combo.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
            Click += (_, _) => { combo.Focus(); combo.DroppedDown = true; };
            return;
        }

        switch (inner)
        {
            case TextBox box: box.BorderStyle = BorderStyle.None; break;
        }

        if (inner is NumericUpDown spin)
        {
            // 数字框同理：自带的上下小箭头也去不掉，裁掉，右边画一对细箭头自己点
            isNumeric = true;
            spin.BorderStyle = BorderStyle.None;
            var host = new Panel { BackColor = Color.White, Left = 12, Top = 4, Width = Width - 46, Height = Height - 8 };
            spin.SetBounds(0, Math.Max(0, (host.Height - spin.PreferredHeight) / 2), host.Width + 36, spin.PreferredHeight);
            host.Controls.Add(spin);
            Controls.Add(host);
            spin.GotFocus += (_, _) => { focused = true; Invalidate(); };
            spin.LostFocus += (_, _) => { focused = false; Invalidate(); };
            spin.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
            MouseClick += (_, e) =>
            {
                if (e.X > Width - 36)
                {
                    if (e.Y < Height / 2) spin.UpButton(); else spin.DownButton();
                }
                else spin.Focus();
            };
            return;
        }

        inner.Location = new Point(13, (Height - inner.Height) / 2);
        inner.Width = Width - 28;
        inner.GotFocus += (_, _) => { focused = true; Invalidate(); };
        inner.LostFocus += (_, _) => { focused = false; Invalidate(); };
        inner.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        Controls.Add(inner);
        Click += (_, _) => inner.Focus();
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => inner.Text;
        set => inner.Text = value ?? string.Empty;
    }

    internal Control Inner => inner;

    internal decimal Value
    {
        get => inner is NumericUpDown num ? num.Value : 0;
        set { if (inner is NumericUpDown num) num.Value = value; }
    }

    internal event EventHandler? ValueChanged
    {
        add { if (inner is NumericUpDown num) num.ValueChanged += value; }
        remove { if (inner is NumericUpDown num) num.ValueChanged -= value; }
    }

    internal ComboBox.ObjectCollection Items =>
        inner is ComboBox combo ? combo.Items : throw new InvalidOperationException("不是下拉框");

    internal int SelectedIndex
    {
        get => inner is ComboBox combo ? combo.SelectedIndex : -1;
        set { if (inner is ComboBox combo) combo.SelectedIndex = value; }
    }

    internal object? SelectedItem
    {
        get => inner is ComboBox combo ? combo.SelectedItem : null;
        set { if (inner is ComboBox combo) combo.SelectedItem = value; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(rect, Theme.RadiusField);
        using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);

        if (focused)
        {
            // 聚焦时描一圈粉→蓝的渐变，比单纯换色更活
            using var accent = Theme.Accent(rect);
            using var pen = new Pen(accent, 1.8f);
            g.DrawPath(pen, path);
        }
        else
        {
            using var pen = new Pen(Theme.Line, 1f);
            g.DrawPath(pen, path);
        }

        if (isCombo)
        {
            // 自己画的小箭头（原生那个被上面的 host 裁掉了）
            using var chev = new Pen(focused ? Theme.Blue : Theme.InkSoft, 1.5f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var cx = Width - 21f;
            var cy = Height / 2f - 1f;
            g.DrawLine(chev, cx - 4, cy - 1.5f, cx, cy + 2.5f);
            g.DrawLine(chev, cx, cy + 2.5f, cx + 4, cy - 1.5f);
        }
        else if (isNumeric)
        {
            // 上／下两个小箭头
            var ink = Theme.InkSoft;
            using var up = new SolidBrush(ink);
            using var down = new SolidBrush(ink);
            var cx = Width - 20f;
            var mid = Height / 2f;
            g.FillPolygon(up, new[]
            {
                new PointF(cx - 4, mid - 3), new PointF(cx + 4, mid - 3), new PointF(cx, mid - 9),
            });
            g.FillPolygon(down, new[]
            {
                new PointF(cx - 4, mid + 3), new PointF(cx + 4, mid + 3), new PointF(cx, mid + 9),
            });
        }
    }
}

// ───────────────────────────────────── 滑块
/// <summary>粉←→蓝的轨道滑块。</summary>
internal sealed class NeoSlider : Control
{
    private int value;
    private bool dragging, hover;

    internal int Minimum { get; set; }
    internal int Maximum { get; set; } = 100;
    internal event EventHandler? ValueChanged;

    internal int Value
    {
        get => value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == this.value) return;
            this.value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal NeoSlider(int width = 300)
    {
        Width = width;
        Height = 32;
        Cursor = Cursors.Hand;
        BackColor = Theme.Card;
        Margin = new Padding(0, 2, 0, 6);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
    }

    private int TrackLeft => 13;
    private int TrackWidth => Math.Max(1, Width - 26);

    private void Pick(int x)
    {
        var ratio = Math.Clamp((x - TrackLeft) / (double)TrackWidth, 0, 1);
        Value = Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Pick(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Pick(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var mid = Height / 2;
        using (var track = new Pen(Color.FromArgb(234, 241, 249), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(track, TrackLeft, mid, TrackLeft + TrackWidth, mid);

        var ratio = Maximum > Minimum ? (value - Minimum) / (double)(Maximum - Minimum) : 0;
        var x = TrackLeft + (int)(TrackWidth * ratio);
        if (x > TrackLeft + 2)
        {
            using var fill = new LinearGradientBrush(
                new Rectangle(TrackLeft, mid - 3, Math.Max(2, TrackWidth), 6), Theme.Pink, Theme.Blue, LinearGradientMode.Horizontal);
            using var part = new Pen(fill, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(part, TrackLeft, mid, x, mid);
        }

        var knob = hover || dragging ? 19 : 17;
        var left = x - knob / 2;
        var top = mid - knob / 2;
        // 投影
        using (var shadow = new SolidBrush(Color.FromArgb(38, Theme.PinkDeep)))
            g.FillEllipse(shadow, left, top + 1, knob, knob);
        using (var face = new SolidBrush(Color.White))
            g.FillEllipse(face, left, top, knob, knob);
        using (var ring = new Pen(Theme.PinkDeep, 2f))
            g.DrawEllipse(ring, left, top, knob, knob);
        using (var dot = new SolidBrush(Color.FromArgb(200, Theme.Pink)))
            g.FillEllipse(dot, left + knob / 2 - 2, top + knob / 2 - 2, 4, 4);
    }
}

// ───────────────────────────────────── 侧边导航
/// <summary>左侧导航：小图标 + 文字，选中是粉→蓝的胶囊。</summary>
internal sealed class NavItem : Control
{
    private bool hover, active;
    private readonly Glyph glyph;

    internal bool Active
    {
        get => active;
        set { active = value; Invalidate(); }
    }

    internal NavItem(string text, Glyph glyph)
    {
        Text = text;
        this.glyph = glyph;
        Height = 46;
        Width = Theme.NavWidth;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        Margin = new Padding(0);
        BackColor = Theme.Side;
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
        g.Clear(Theme.Side);

        var pill = new Rectangle(10, 4, Width - 20, Height - 8);
        using var path = Theme.Rounded(pill, 11);

        if (active)
        {
            Theme.Shadow(g, pill, 11, Theme.PinkDeep, 5, 2, 34);
            using var fill = Theme.Accent(pill);
            g.FillPath(fill, path);
        }
        else if (hover)
        {
            using var fill = new SolidBrush(Theme.BluePale);
            g.FillPath(fill, path);
        }

        var iconBox = new Rectangle(pill.X + 12, pill.Y + (pill.Height - 18) / 2, 18, 18);
        var iconInk = active ? Color.White : hover ? Theme.Blue : Theme.InkSoft;
        Glyphs.Draw(g, glyph, iconBox, iconInk);

        var format = new StringFormat { LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(active ? Color.White : Theme.Ink);
        g.DrawString(Text, active ? Theme.Section : Font, brush,
            new RectangleF(pill.X + 40, pill.Y, pill.Width - 44, pill.Height), format);
    }
}

/// <summary>侧栏里的分组小标题。</summary>
internal sealed class NavGroup : Control
{
    internal NavGroup(string text)
    {
        Text = text;
        Height = 30;
        Width = Theme.NavWidth;
        Margin = new Padding(0, 8, 0, 0);
        BackColor = Theme.Side;
        Font = Theme.Small;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Side);
        using var brush = new SolidBrush(Theme.InkFaint);
        var format = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(Text, Font, brush, new RectangleF(22, 6, Width - 22, Height - 6), format);
    }
}

// ───────────────────────────────────── 窗口按钮
/// <summary>标题栏右边的三个小按钮。符号自己画，不依赖字符。</summary>
internal sealed class CaptionButton : Control
{
    private bool hover, down;
    private readonly string kind;

    internal bool Danger { get; set; }

    internal CaptionButton(string kind)
    {
        this.kind = kind;
        Size = new Size(32, 28);
        Cursor = Cursors.Hand;
        Margin = new Padding(4, 0, 0, 0);
        BackColor = Theme.Side;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(rect, 9);

        if (down) { using var f = new SolidBrush(Danger ? Theme.PinkDeep : Theme.BlueSoft); g.FillPath(f, path); }
        else if (hover) { using var f = new SolidBrush(Danger ? Color.FromArgb(255, 231, 237) : Theme.BluePale); g.FillPath(f, path); }

        var ink = hover ? (Danger ? Color.White : Theme.Blue) : Theme.InkSoft;
        using var pen = new Pen(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float cx = Width / 2f, cy = Height / 2f;
        if (kind == "min") g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
        else if (kind == "max") g.DrawRectangle(pen, cx - 4.5f, cy - 4.5f, 9, 9);
        else
        {
            g.DrawLine(pen, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
            g.DrawLine(pen, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
        }
    }
}
