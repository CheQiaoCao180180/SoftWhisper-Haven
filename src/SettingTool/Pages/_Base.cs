// 重写版（2026-11-17，第二次）：页面骨架。
//
// 这一版把布局交给 TableLayoutPanel，不再靠 Padding 猜：
//
//   页面（FlowLayoutPanel，Dock=Top，AutoSize 管高度）
//     └ 每张卡片外面包一层 Lane：三列 [弹性 | 固定宽度 | 弹性]
//         卡片放中列，两边自动等宽——窗口怎么拉都居中。
//
// 为什么不用 FlowLayoutPanel 的 Padding：它跟 AutoSize / Dock=Top 配合时
// 右内边距会被内容宽度吃掉，算出来的留白永远对不上。

namespace SettingTool;

internal sealed partial class MainForm
{
    /// <summary>卡片最舒服的宽度，窗口够宽就用这个。</summary>
    private const int CardIdeal = 920;

    /// <summary>再窄就放不下下拉和输入框了。</summary>
    private const int CardFloor = 620;

    /// <summary>
    /// 当前该用多宽：按内容区实际可用宽度算（不是窗口宽度），
    /// 这样最大化、拉宽时卡片会跟着长，不会被固定值卡住。
    /// </summary>
    private int CardWidth => Math.Max(CardFloor, Math.Min(CardIdeal, content.ClientSize.Width - 120));

    /// <summary>页面四周留多少。</summary>
    private const int PagePad = 22;

    /// <summary>页面宽度跟着内容区走（否则居中通道的左右两列会塌成 0，卡片永远贴在左边）。</summary>
    private void SyncPageWidths()
    {
        var width = Math.Max(640, content.ClientSize.Width - 1);
        foreach (var (_, page) in pages)
        {
            if (page.MinimumSize.Width != width) page.MinimumSize = new Size(width, 0);
        }
    }

    /// <summary>窗口大小变了之后，把每页的卡片重新套进居中通道里。</summary>
    private void Relayout()
    {
        SyncPageWidths();
        foreach (var (_, page) in pages)
        {
            // 只有当前显示着的页面需要重排，隐藏的页面等切过去时再说，
            // 否则拖窗口时 12 页一起算，一帧就要几十毫秒
            if (!page.Visible) continue;
            if (page is not FlowLayoutPanel flow) continue;
            foreach (Control child in flow.Controls)
            {
                if (child is not Lane lane) continue;
                lane.Fit(CardWidth);
            }
        }
    }

    // ── 一页
    private FlowLayoutPanel NewPage()
    {
        var page = new BufferedFlow
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.PageTop,
            Padding = new Padding(0, 8, 0, 24),
        };
        page.Paint += (_, e) => Theme.PaintPage(e.Graphics, page.ClientRectangle);
        page.ControlAdded += (_, e) =>
        {
            // 卡片一律走居中通道
            if (e.Control is Lane) return;
        };
        return page;
    }

    /// <summary>把一张卡片包进居中通道。所有 Build*Page 都用这个装卡片。</summary>
    private void Mount(FlowLayoutPanel page, Control card)
    {
        var lane = new Lane();
        lane.Mount(card, CardWidth);
        page.Controls.Add(lane);
    }

    // ── 文本框
    private static NeoField TextField(string value, int width = 420)
    {
        var box = new TextBox
        {
            Text = value,
            Font = Theme.Mono,
            ForeColor = Theme.Ink,
            BackColor = Color.White,
        };
        return new NeoField(box, width);
    }

    // ── 数字框
    private static NeoField NumField(decimal value, decimal min, decimal max, decimal step, int width = 110)
    {
        // 顺序有讲究：先给范围再给值，不然默认的 0 会越界
        var num = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            DecimalPlaces = step < 1 ? 2 : 0,
            Font = Theme.Mono,
            ForeColor = Theme.Ink,
            BackColor = Color.White,
        };
        num.Value = Math.Clamp(value, min, max);
        return new NeoField(num, width);
    }

    // ── 下拉
    private static NeoField ComboField(string[] items, int width = 200, string? selected = null)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.Body,
            ForeColor = Theme.Ink,
            BackColor = Color.White,
            Height = 24,
        };
        combo.Items.AddRange(items);
        if (items.Length > 0)
        {
            var at = selected == null ? 0 : Array.IndexOf(items, selected);
            combo.SelectedIndex = at >= 0 ? at : 0;
        }
        return new NeoField(combo, width);
    }

    // ── 横向一行
    private static FlowLayoutPanel Row(params Control[] items)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Card,
            Margin = new Padding(0, 3, 0, 3),
        };
        foreach (var one in items) row.Controls.Add(one);
        return row;
    }

    // ── 行首文字
    // 本机改（2026-11-17）：加 new——这个名字和 Control.Tag（存放任意数据的属性）撞了，
    // 不加会静默隐藏父类的 Tag，以后 `control.Tag = x` 会莫名其妙编译不过。
    private new static Label Tag(string text, int width = 96)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 34,
            ForeColor = Theme.Ink,
            Font = Theme.Body,
            BackColor = Theme.Card,
            Margin = new Padding(0, 4, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    // ── 说明文字
    // 用自绘的 HintLabel：量和画走同一个 TextRenderer，行数才对得上
    // （原生 Label 的 AutoSize 高度对中文换行经常少算一行，最后一行被卡片下沿切掉）
    private HintLabel Hint(string text) => new(text, Math.Max(400, CardWidth - 100));

    // ── 往卡片里加东西（自动重排高度）
    private static void Put(Card card, Control control)
    {
        card.AddControl(control);
    }
}

/// <summary>
/// 居中通道：三列 [弹性 | 固定宽 | 弹性]，卡片放中间。
/// 窗口怎么拉，卡片都保持固定宽度并居中——不用算 padding，交给 TableLayoutPanel。
/// </summary>
internal sealed class Lane : TableLayoutPanel
{
    private Control? card;

    internal Lane()
    {
        ColumnCount = 3;
        // 双缓冲：窗口拉动时这一层要反复重排，不缓冲会明显卡顿
        DoubleBuffered = true;
        RowCount = 1;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.PageTop;
        Margin = new Padding(0);
        Padding = new Padding(0);
    }

    internal void Mount(Control content, int width)
    {
        // 以前这里是 Controls.Clear() 再全部重加，等于把卡片从窗口上摘下来再挂回去，
        // 拖窗口时每变一次宽度就要重排整页，一帧要两百毫秒——这就是卡顿的来源。
        // 现在只在第一次挂载时建结构，之后只改中间那列的宽度。
        var first = Controls.Count == 0 || card != content;
        card = content;

        if (first)
        {
            ColumnStyles.Clear();
            RowStyles.Clear();
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));

            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0, 0, 0, Theme.Gap);
            Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);
            Controls.Add(content, 1, 0);
            Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) }, 2, 0);
        }
        else if ((int)ColumnStyles[1].Width != width)
        {
            ColumnStyles[1].Width = width;
        }

        // 卡片的宽/高在构造时就被钉死过一次，之后换宽度必须在这里同步，
        // 否则卡片会一直是构造时的老宽度、被父容器裁掉右边（表格第三列就是这样丢的）
        if (content.MinimumSize.Width != width)
        {
            content.MinimumSize = new Size(width, 0);
            content.MaximumSize = new Size(width, 0);
        }
    }

    /// <summary>卡片宽度变了就重套一遍。</summary>
    internal void Fit(int width)
    {
        if (card == null) return;
        if (ColumnStyles.Count >= 2 && (int)ColumnStyles[1].Width == width) return;
        Mount(card, width);
    }
}
/// <summary>
/// 说明文字：自己量高、自己画的文本框。
/// 用 TextRenderer 量 + 用 TextRenderer 画，换行行数和高度保证一致，不会被父容器切掉最后一行。
/// </summary>
internal sealed class HintLabel : Panel
{
    private int maxWidth;

    internal HintLabel(string text, int maxWidth)
    {
        this.maxWidth = maxWidth;
        BackColor = Theme.Card;
        Margin = new Padding(0, 0, 0, 8);
        Tag = "wide";
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Text = text;
        Relayout();
    }

    /// <summary>换文字（用于状态提示），会顺带重算高度。</summary>
    internal void SetText(string text)
    {
        Text = text;
        Relayout();
        Invalidate();
    }

    /// <summary>卡片宽度变了，重新给宽度并重算行数。</summary>
    internal void SetMaxWidth(int width)
    {
        if (width == maxWidth) return;
        maxWidth = width;
        Relayout();
    }

    private void Relayout()
    {
        Width = maxWidth;
        var measured = TextRenderer.MeasureText(
            Text, Theme.Small, new Size(maxWidth - 4, int.MaxValue),
            TextFormatFlags.WordBreak);
        Height = Math.Max(18, measured.Height + 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Text(e.Graphics);
        TextRenderer.DrawText(
            e.Graphics, Text, Theme.Small, new Rectangle(0, 0, Width, Height), Theme.InkSoft,
            TextFormatFlags.WordBreak);
    }
}