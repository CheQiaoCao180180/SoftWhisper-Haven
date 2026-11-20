// 大肥鱼设置 · 界面 v2（2026-11-17）
// 颜色、字体、间距、圆角、阴影，全在这里。别的地方一律不许写死颜色。

using System.Drawing.Drawing2D;

namespace SettingTool;

internal static class Theme
{
    // ── 底（淡粉白打底，卡片纯白，靠阴影分层）—— 本机改（2026-11-19）：往二次元那套调
    internal static readonly Color PageFlat = Color.FromArgb(253, 247, 251);
    internal static readonly Color Card = Color.White;
    internal static readonly Color Side = Color.FromArgb(255, 250, 253);
    internal static readonly Color CardHover = Color.FromArgb(255, 248, 252);

    // ── 线
    internal static readonly Color Line = Color.FromArgb(240, 228, 238);         // 普通描边
    internal static readonly Color LineSoft = Color.FromArgb(248, 240, 246);     // 极浅分隔

    // ── 主色（樱紫打底，樱粉做强调，渐变只出现在「选中 / 主操作」）
    internal static readonly Color Blue = Color.FromArgb(155, 140, 255);
    internal static readonly Color BlueDeep = Color.FromArgb(122, 107, 224);
    internal static readonly Color BlueSoft = Color.FromArgb(201, 191, 255);
    internal static readonly Color BluePale = Color.FromArgb(243, 239, 255);

    internal static readonly Color Pink = Color.FromArgb(255, 179, 201);
    // 本机加（2026-11-18）：日历里「自己记的日程」用这个色
    internal static readonly Color Orange = Color.FromArgb(255, 178, 122);
    // 本机加（2026-11-18）：农历节日用这个（不像正红那么刺眼）
    internal static readonly Color Red = Color.FromArgb(242, 112, 125);
    internal static readonly Color PinkDeep = Color.FromArgb(255, 143, 174);

    // ── 文字
    internal static readonly Color Ink = Color.FromArgb(59, 51, 80);             // 正文
    internal static readonly Color InkSoft = Color.FromArgb(133, 124, 155);      // 说明
    internal static readonly Color InkFaint = Color.FromArgb(179, 170, 198);     // 极弱

    // ── 状态
    internal static readonly Color Warn = Color.FromArgb(255, 162, 107);
    internal static readonly Color Good = Color.FromArgb(111, 211, 174);

    // ── 兼容旧名（页面里还在用）
    internal static readonly Color PageTop = PageFlat;
    internal static readonly Color PageBottom = PageFlat;

    // ── 字体
    internal static readonly Font Title = new("Microsoft YaHei UI", 12.5f, FontStyle.Bold);
    internal static readonly Font Section = new("Microsoft YaHei UI", 10f, FontStyle.Bold);
    internal static readonly Font Body = new("Microsoft YaHei UI", 9.5f);
    internal static readonly Font Small = new("Microsoft YaHei UI", 8.5f);
    // 本机加（2026-11-18）：日历里的农历小字
    internal static readonly Font Tiny = new("Microsoft YaHei UI", 7.2f);
    internal static readonly Font Mono = new("Consolas", 9.5f);

    /// <summary>
    /// 本机改（2026-11-17）：GDI+ 的 DrawString 默认走灰度抗锯齿，
    /// 在浅灰底上就显脏、发虚。自绘文字统一开 ClearType。
    /// </summary>
    internal static void Text(System.Drawing.Graphics g)
    {
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    // ── 尺寸（本机改 2026-11-19：圆角放大，更像二次元那套卡片）
    internal const int RadiusCard = 20;
    internal const int RadiusButton = 14;
    internal const int RadiusField = 12;
    internal const int NavWidth = 194;
    internal const int FooterHeight = 70;
    internal const int TitleHeight = 56;
    internal const int Gap = 14;

    /// <summary>圆角矩形路径。</summary>
    internal static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 1) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 本机加（2026-11-19）：窗口背景图——assets\bg-winter.jpg（雪景、小火炉、白饭）。
    /// 图本身已经模糊+盖过一层白纱，所以能拉满当底纹，不抢字。文件不在就退回纯色。
    /// 注意：不能在静态构造里就加载——那时 Program.Root 可能还没准备好，会拿到 null。
    /// </summary>
    private static Image? backdrop;
    private static bool backdropTried;

    /// <summary>给页面容器用的背景图（没有就 null）。</summary>
    internal static Image? Backdrop()
    {
        if (backdropTried) return backdrop;
        backdropTried = true;
        try
        {
            var path = Path.Combine(Program.Root, "assets", "bg-winter.jpg");
            if (!File.Exists(path)) return backdrop = null;
            // 坑（本机踩过 2026-11-19）：Image.FromStream 出来的图**依赖那个流**，
            // using 一关流图就废了（不报错，画出来是空白）。所以要复制成独立位图。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var temp = Image.FromStream(stream);
            return backdrop = new Bitmap(temp);
        }
        catch { return backdrop = null; }
    }

    /// <summary>
    /// 页面底色（本机改 2026-11-19）：竖向渐变——顶部淡粉，往下慢慢变白。
    /// 原来试过铺图片背景，但这套面板是层层不透明的（页面自己刷底、卡片自己刷底），
    /// 图根本露不出来；渐变是直接画在页面上的，不会被打掉。
    /// </summary>
    internal static void PaintPage(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var brush = new LinearGradientBrush(
            bounds, Color.FromArgb(253, 247, 251), Color.FromArgb(255, 253, 255), LinearGradientMode.Vertical);
        g.FillRectangle(brush, bounds);
    }

    /// <summary>柔和投影：一圈圈由内向外变淡的描边叠出来，比纯白卡片有厚度感。</summary>
    internal static void Shadow(Graphics g, Rectangle rect, int radius, Color tint, int spread = 7, int offsetY = 2, int alpha = 46)
    {
        if (spread <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (var i = spread; i >= 1; i--)
        {
            var box = Rectangle.Inflate(rect, i, i);
            box.Offset(0, offsetY);
            if (box.Width <= 2 || box.Height <= 2) continue;
            var a = Math.Max(1, alpha - i * (alpha / (spread + 1)));
            using var pen = new Pen(Color.FromArgb(a, tint), 1f);
            using var path = Rounded(box, radius + i);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>一条横向渐变，用在一级强调（选中导航、主按钮）。</summary>
    internal static LinearGradientBrush Accent(Rectangle rect) =>
        new(rect, Pink, Blue, LinearGradientMode.Horizontal);

    internal static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    internal static Color Lighten(Color c, float t) => Mix(c, Color.White, t);
    internal static Color Darken(Color c, float t) => Mix(c, Color.FromArgb(20, 26, 38), t);

    /// <summary>侧栏的小星星和光点（2026-11-19）。侧栏是不透明面板，装饰只能画在它自己身上。</summary>
    internal static void PaintNavStars(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var stars = new (float X, float Y, float R, int Alpha)[]
        {
            (0.30f, 0.60f, 8f, 62), (0.66f, 0.70f, 5f, 46), (0.40f, 0.80f, 6f, 50),
            (0.74f, 0.88f, 4f, 40), (0.24f, 0.92f, 5f, 34), (0.56f, 0.52f, 3f, 36),
        };
        foreach (var (fx, fy, r, alpha) in stars)
        {
            var cx = bounds.Width * fx;
            var cy = bounds.Height * fy;
            if (cx < 8 || cy < 8 || cx > bounds.Width - 8 || cy > bounds.Height - 8) continue;
            using var path = new GraphicsPath();
            var t = r * 0.28f;
            path.AddPolygon(
            [
                new PointF(cx, cy - r), new PointF(cx + t, cy - t),
                new PointF(cx + r, cy), new PointF(cx + t, cy + t),
                new PointF(cx, cy + r), new PointF(cx - t, cy + t),
                new PointF(cx - r, cy), new PointF(cx - t, cy - t),
            ]);
            using var brush = new SolidBrush(Color.FromArgb(alpha, PinkDeep));
            g.FillPath(brush, path);
        }

        var dots = new (float X, float Y, int R, int Alpha)[]
        {
            (0.18f, 0.66f, 2, 90), (0.80f, 0.62f, 2, 80), (0.46f, 0.74f, 1, 85),
            (0.62f, 0.94f, 2, 70), (0.32f, 0.86f, 1, 75),
        };
        foreach (var (fx, fy, r, alpha) in dots)
        {
            var cx = bounds.Width * fx;
            var cy = bounds.Height * fy;
            if (cx < 4 || cy < 4 || cx > bounds.Width - 4 || cy > bounds.Height - 4) continue;
            using var brush = new SolidBrush(Color.FromArgb(alpha, Blue));
            g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
        }
    }

    /// <summary>背景上飘的气泡、星星和光点（很淡，不抢字）。</summary>
    internal static void PaintBackdrop(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 本机改（2026-11-19）：加小星星和光点——位置写死，不随重绘乱跳。
        // 注意：卡片是不透明的白，画在内容区会被全部盖住；所以只放在侧栏下半段的空白里，
        // 以及标题栏标题右边那一小条。
        var stars = new (float X, float Y, float R, int Alpha)[]
        {
            (0.045f, 0.72f, 7f, 60), (0.115f, 0.80f, 5f, 46), (0.065f, 0.88f, 4f, 40),
            (0.145f, 0.66f, 4f, 38), (0.10f, 0.945f, 6f, 30),
            (0.315f, 0.045f, 5f, 42), (0.355f, 0.062f, 3f, 34),
        };
        foreach (var (fx, fy, r, alpha) in stars)
        {
            var cx = bounds.Width * fx;
            var cy = bounds.Height * fy;
            if (cx < 8 || cy < 8 || cx > bounds.Width - 8 || cy > bounds.Height - 8) continue;
            using var path = new GraphicsPath();
            var t = r * 0.28f;
            path.AddPolygon(
            [
                new PointF(cx, cy - r), new PointF(cx + t, cy - t),
                new PointF(cx + r, cy), new PointF(cx + t, cy + t),
                new PointF(cx, cy + r), new PointF(cx - t, cy + t),
                new PointF(cx - r, cy), new PointF(cx - t, cy - t),
            ]);
            using var brush = new SolidBrush(Color.FromArgb(alpha, PinkDeep));
            g.FillPath(brush, path);
        }

        // 光点
        var dots = new (float X, float Y, int R, int Alpha)[]
        {
            (0.03f, 0.78f, 2, 90), (0.16f, 0.90f, 2, 80), (0.07f, 0.68f, 1, 85),
            (0.13f, 0.75f, 1, 70), (0.09f, 0.84f, 2, 65), (0.335f, 0.055f, 1, 80),
        };
        foreach (var (fx, fy, r, alpha) in dots)
        {
            var cx = bounds.Width * fx;
            var cy = bounds.Height * fy;
            if (cx < 4 || cy < 4 || cx > bounds.Width - 4 || cy > bounds.Height - 4) continue;
            using var brush = new SolidBrush(Color.FromArgb(alpha, Blue));
            g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
        }

        using var bubble = new SolidBrush(Color.FromArgb(16, Pink));
        var spots = new (int X, int Y, int R)[]
        {
            (60, 560, 26), (120, 640, 14), (36, 700, 18),
        };
        foreach (var (x, y, r) in spots)
        {
            if (x < 0 || y < 0 || x + r * 2 > bounds.Width || y + r * 2 > bounds.Height) continue;
            g.FillEllipse(bubble, x, y, r * 2, r * 2);
        }
    }
}
