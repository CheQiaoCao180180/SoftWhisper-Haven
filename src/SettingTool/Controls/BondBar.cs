// 本机加（2026-11-19）：羁绊进度条——一条轨道、五级刻度、当前那一段填满。
using System.Drawing.Drawing2D;

namespace SettingTool;

internal sealed class BondBar : Control
{
    private const int Max = 1000;
    // 和 Affection.cs 的 Levels 一致
    private static readonly (int From, string Name)[] Levels =
        [(0, "初识"), (100, "磨合"), (300, "同频"), (550, "默契"), (800, "羁绊")];

    private int points;

    // 本机改（2026-11-19）：重绘时别再每次 new 画笔和字体——滚动、拖动时一秒几十次，
    // 每次都造五六个 GDI 对象，久了就明显发滞。这些都跟颜色/字号无关，做成共享的。
    private static readonly StringFormat Center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
    private static readonly StringFormat Small = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };
    private static readonly SolidBrush InkBrush = new(Theme.InkSoft);
    private static readonly SolidBrush DeepBrush = new(Theme.Ink);
    private static readonly Pen TickPen = new(Color.FromArgb(198, 214, 232), 1f);
    private static readonly SolidBrush TrackBrush = new(Color.FromArgb(233, 240, 249));
    private static readonly SolidBrush KnobBrush = new(Theme.BlueDeep);

    internal int Points
    {
        get => points;
        set { points = Math.Clamp(value, 0, Max); }
    }

    internal BondBar(int points, int width)
    {
        Points = points;
        Width = width;
        Height = 74;
        BackColor = Theme.Card;
        Margin = new Padding(0, 2, 0, 4);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        // 本机改（2026-11-19）：五级平分成五格——名字放格子正中，分数小字放名字上面。
        // 早先按分数线性排、后来按刻度点排，都会让首尾两格贴边被裁；平分最稳。
        const int left = 6;
        var trackWidth = Width - left * 2;
        const int trackY = 30;
        var track = new Rectangle(left, trackY, trackWidth, 10);
        var cell = trackWidth / (float)Levels.Length;

        using (var path = Theme.Rounded(track, 5))
            g.FillPath(TrackBrush, path);

        // 当前处在哪一级、这一级里走了多少
        var level = 0;
        for (var i = 0; i < Levels.Length; i++) if (Points >= Levels[i].From) level = i;
        var from = Levels[level].From;
        var to = level < Levels.Length - 1 ? Levels[level + 1].From : Max;
        var ratio = to > from ? Math.Clamp((Points - from) / (float)(to - from), 0f, 1f) : 1f;
        var fillWidth = (int)Math.Round(track.X + cell * (level + ratio) - track.X);

        if (fillWidth > 2)
        {
            using var path = Theme.Rounded(new Rectangle(track.X, track.Y, fillWidth, track.Height), 5);
            using var brush = Theme.Accent(new Rectangle(track.X, track.Y, Math.Max(fillWidth, 1), 1));
            g.FillPath(brush, path);
        }

        using var ink = new SolidBrush(Theme.InkSoft);
        using var deep = new SolidBrush(Theme.Ink);
        using var line = new Pen(Color.FromArgb(198, 214, 232), 1f);
        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        var small = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };

        for (var i = 0; i < Levels.Length; i++)
        {
            var (start, name) = Levels[i];
            var cellLeft = track.X + i * cell;

            // 分界刻度线（除了最左边那条，由轨道自己表示）
            if (i > 0) g.DrawLine(TickPen, cellLeft, track.Bottom + 2, cellLeft, track.Bottom + 7);

            var reached = Points >= start;
            g.DrawString(name, Theme.Tiny, reached ? DeepBrush : InkBrush,
                new RectangleF(cellLeft, track.Bottom + 9, cell, 18), Center);
            g.DrawString(start.ToString(), Theme.Tiny, InkBrush,
                new RectangleF(cellLeft, 2, cell, 14), Small);
        }

        // 当前分值的小圆点
        if (Points > 0)
        {
            var x = track.X + cell * (level + ratio);
            g.FillEllipse(KnobBrush, x - 4, track.Y + 1, 8, 8);
        }
    }
}
