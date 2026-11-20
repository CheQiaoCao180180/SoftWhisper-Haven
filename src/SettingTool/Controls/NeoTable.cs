// 新增（2026-11-17）：自绘的只读表格。
//
// 为什么不用 DataGridView：它自带系统表头（灰色渐变）、系统滚动条、系统选中蓝条，
// 和这套卡片风格放在一起特别突兀——那是整个界面里唯一"露馅"的地方。
// 这里只做只读展示，所以自己画：浅蓝表头 + 斑马行 + 细滚动条。

namespace SettingTool;

internal sealed class NeoTable : Control
{
    private const int HeaderHeight = 34;
    private const int RowHeight = 30;
    private const int PadX = 14;

    private readonly string[] headers;
    private readonly List<string[]> rows = new();
    private int scroll;          // 已滚动的像素
    private int hoverRow = -1;

    internal NeoTable(string[] headers, int width, int height)
    {
        this.headers = headers;
        Width = width;
        Height = height;
        BackColor = Theme.Card;
        Font = Theme.Small;
        Margin = new Padding(0, 2, 0, 4);
        Cursor = Cursors.Default;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    internal void ClearRows()
    {
        rows.Clear();
        scroll = 0;
        Invalidate();
    }

    internal void AddRow(string a, string b, string c)
    {
        rows.Add([a, b, c]);
        Invalidate();
    }

    // ── 滚动
    private int BodyHeight => Math.Max(0, Height - HeaderHeight);
    private int ContentHeight => rows.Count * RowHeight;
    private int MaxScroll => Math.Max(0, ContentHeight - BodyHeight);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (MaxScroll <= 0) return;
        scroll = Math.Clamp(scroll - e.Delta / 3, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var row = e.Y < HeaderHeight ? -1 : (e.Y - HeaderHeight + scroll) / RowHeight;
        if (row >= rows.Count) row = -1;
        if (row != hoverRow) { hoverRow = row; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverRow != -1) { hoverRow = -1; Invalidate(); }
    }

    // ── 绘制
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        // 卡片里这块可能比可见区宽一点（AutoSize 容器的手工宽度会被冲掉），
        // 一切按「实际可见宽度」算，免得右边一列被裁掉。
        var W = Width;
        if (Parent != null && Parent.ClientSize.Width > 40) W = Math.Min(W, Parent.ClientSize.Width);

        var body = new Rectangle(0, HeaderHeight, W, Math.Max(0, Height - HeaderHeight));
        var columns = ColumnWidths(W);

        // 表头
        using (var head = new SolidBrush(Theme.BluePale))
        using (var headPath = Theme.Rounded(new Rectangle(0, 0, W, HeaderHeight + 12), 10))
        {
            g.SetClip(new Rectangle(0, 0, W, HeaderHeight));
            g.FillPath(head, headPath);
            g.ResetClip();
        }

        var ink = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var headText = new SolidBrush(Theme.Ink);
        for (var i = 0; i < headers.Length; i++)
        {
            g.DrawString(headers[i], Theme.Section, headText,
                new RectangleF(columns[i].X + PadX, 0, columns[i].Width - PadX * 2, HeaderHeight), ink);
        }

        // 行
        var clip = g.Clip;
        g.SetClip(body);
        for (var index = 0; index < rows.Count; index++)
        {
            var y = HeaderHeight + index * RowHeight - scroll;
            if (y > Height || y + RowHeight < HeaderHeight) continue;

            var rowRect = new Rectangle(0, y, W, RowHeight);
            if (rowRect.Left == 0 && rowRect.Right > 0)
            {
                if (index == hoverRow)
                {
                    using var hover = new SolidBrush(Theme.CardHover);
                    g.FillRectangle(hover, rowRect);
                }
                else if (index % 2 == 1)
                {
                    using var zebra = new SolidBrush(Color.FromArgb(250, 252, 255));
                    g.FillRectangle(zebra, rowRect);
                }
            }

            using var rowInk = new SolidBrush(Theme.Ink);
            for (var c = 0; c < rows[index].Length && c < headers.Length; c++)
            {
                g.DrawString(rows[index][c], c == 0 ? Theme.Mono : Font, rowInk,
                    new RectangleF(columns[c].X + PadX, y, columns[c].Width - PadX * 2, RowHeight), ink);
            }

            using var line = new Pen(Theme.LineSoft, 1f);
            g.DrawLine(line, 0, y + RowHeight - 1, W, y + RowHeight - 1);
        }

        g.Clip = clip;
        g.ResetClip();

        // 表头和正文之间一条稍重的线
        using (var stroke = new Pen(Theme.Line, 1f))
            g.DrawLine(stroke, 0, HeaderHeight, W, HeaderHeight);

        // 细滚动条
        if (MaxScroll > 0)
        {
            var track = new Rectangle(W - 6, HeaderHeight + 4, 4, BodyHeight - 8);
            using (var back = new SolidBrush(Theme.LineSoft)) g.FillRectangle(back, track);

            var ratio = (float)BodyHeight / ContentHeight;
            var thumbHeight = Math.Max(24, (int)(track.Height * ratio));
            var thumbY = track.Y + (int)((track.Height - thumbHeight) * ((float)scroll / MaxScroll));
            using var thumb = new SolidBrush(Theme.BlueSoft);
            using var thumbPath = Theme.Rounded(new Rectangle(track.X, thumbY, track.Width, thumbHeight), 2);
            g.FillPath(thumb, thumbPath);
        }
    }

    private Rectangle[] ColumnWidths(int total)
    {
        // 三列：40% / 36% / 24%

        var first = (int)(total * 0.4f);
        var second = (int)(total * 0.36f);
        return
        [
            new Rectangle(0, 0, first, HeaderHeight),
            new Rectangle(first, 0, second, HeaderHeight),
            new Rectangle(first + second, 0, total - first - second, HeaderHeight),
        ];
    }
}
