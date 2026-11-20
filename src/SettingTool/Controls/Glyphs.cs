// 界面 v2（2026-11-17）：左侧导航的小图标。全部用 GDI+ 现画，不依赖图片资源和字体。
// 每个图标都按 18×18 的画框设计，缩放到别的尺寸也能用。

using System.Drawing.Drawing2D;

namespace SettingTool;

internal enum Glyph { Voice, Look, Behavior, Model, Draw, Chat, Music, Skit, Record, Hotkey, Other }

internal static class Glyphs
{
    /// <summary>把一个 18×18 的图元画到指定的方框里。</summary>
    internal static void Draw(Graphics g, Glyph kind, Rectangle box, Color color)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // 统一按 18 格设计，再整体缩放到目标框
        var save = g.Save();
        g.TranslateTransform(box.X, box.Y);
        var s = box.Width / 18f;
        g.ScaleTransform(s, s);

        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);

        switch (kind)
        {
            case Glyph.Voice:
                {
                    using var path = new GraphicsPath();
                    path.AddPolygon(new[]
                    {
                        new PointF(3, 7.2f), new PointF(6, 7.2f), new PointF(9.6f, 3.4f),
                        new PointF(9.6f, 14.6f), new PointF(6, 10.8f), new PointF(3, 10.8f),
                    });
                    path.CloseFigure();
                    g.FillPath(brush, path);
                    g.DrawArc(pen, 10.6f, 5.4f, 5.2f, 7.2f, -70, 140);
                    g.DrawArc(pen, 12.6f, 3.2f, 6.4f, 11.6f, -62, 124);
                    break;
                }
            case Glyph.Look:
                {
                    using var eye = new GraphicsPath();
                    eye.AddBezier(2, 9, 5.2f, 4.4f, 12.8f, 4.4f, 16, 9);
                    eye.AddBezier(16, 9, 12.8f, 13.6f, 5.2f, 13.6f, 2, 9);
                    eye.CloseFigure();
                    g.DrawPath(pen, eye);
                    g.FillEllipse(brush, 7f, 6.8f, 4f, 4f);
                    break;
                }
            case Glyph.Behavior:
                {
                    g.FillEllipse(brush, 6.2f, 7.6f, 5.6f, 5.2f);          // 掌心
                    foreach (var (x, y, d) in new[] { (3.4f, 5.4f, 3.2f), (6.4f, 3.2f, 3.2f), (9.8f, 3.4f, 3.2f), (12f, 6.2f, 3.2f) })
                        g.FillEllipse(brush, x, y, d, d);                  // 四个脚趾
                    break;
                }
            case Glyph.Model:
                {
                    using var cube = new GraphicsPath();
                    cube.AddPolygon(new[]
                    {
                        new PointF(9, 2.2f), new PointF(15.4f, 5.8f), new PointF(15.4f, 12.4f),
                        new PointF(9, 16f), new PointF(2.6f, 12.4f), new PointF(2.6f, 5.8f),
                    });
                    cube.CloseFigure();
                    g.DrawPath(pen, cube);
                    g.DrawLine(pen, 9, 2.2f, 9, 9.6f);
                    g.DrawLine(pen, 9, 9.6f, 2.6f, 5.8f);
                    g.DrawLine(pen, 9, 9.6f, 15.4f, 5.8f);
                    break;
                }
            case Glyph.Draw:
                {
                    // 笔杆 + 笔尖
                    g.DrawLine(pen, 6.2f, 15.2f, 13.6f, 4.6f);
                    using var nib = new GraphicsPath();
                    nib.AddPolygon(new[] { new PointF(3.2f, 16.4f), new PointF(6.2f, 15.2f), new PointF(4.4f, 13.4f) });
                    nib.CloseFigure();
                    g.FillPath(brush, nib);
                    g.DrawLine(pen, 11.4f, 3.6f, 13.6f, 4.6f);
                    break;
                }
            case Glyph.Chat:
                {
                    using var bubble = new GraphicsPath();
                    bubble.AddArc(2.2f, 3.2f, 13.6f, 11.4f, 180, 180);
                    bubble.AddLine(15.8f, 9f, 15.8f, 12.8f);
                    bubble.AddLine(15.8f, 12.8f, 11.6f, 15.2f);
                    bubble.AddLine(11.6f, 15.2f, 8.6f, 16.2f);
                    bubble.AddLine(8.6f, 16.2f, 8.6f, 13.2f);
                    bubble.AddArc(2.2f, 6f, 13.6f, 11.4f, 60, 60);
                    bubble.CloseFigure();
                    g.DrawPath(pen, bubble);
                    foreach (var x in new[] { 5.6f, 8.4f, 11.2f })
                        g.FillEllipse(brush, x, 9f, 2f, 2f);
                    break;
                }
            case Glyph.Music:
                {
                    using var head = new GraphicsPath();
                    head.AddEllipse(3f, 11.4f, 6f, 4.6f);
                    g.FillPath(brush, head);
                    g.DrawLine(pen, 8.4f, 13.2f, 8.4f, 3.6f);
                    using var flag = new GraphicsPath();
                    flag.AddBezier(8.4f, 3.6f, 11.2f, 4.4f, 13f, 6f, 13f, 8.2f);
                    g.DrawPath(pen, flag);
                    break;
                }
            case Glyph.Skit:
                {
                    // 幕布：两个上翘的帘子 + 舞台线
                    using var curtain = new GraphicsPath();
                    curtain.AddBezier(3f, 4.2f, 6f, 2.2f, 12f, 2.2f, 15f, 4.2f);
                    g.DrawPath(pen, curtain);
                    g.DrawLine(pen, 3.4f, 4.6f, 5.6f, 14f);
                    g.DrawLine(pen, 14.6f, 4.6f, 12.4f, 14f);
                    g.DrawLine(pen, 2.6f, 15.4f, 15.4f, 15.4f);
                    g.FillPolygon(brush, new[] { new PointF(9, 6f), new PointF(10.1f, 8.8f), new PointF(13f, 8.9f), new PointF(10.7f, 10.6f), new PointF(11.4f, 13.4f), new PointF(9, 11.8f), new PointF(6.6f, 13.4f), new PointF(7.3f, 10.6f), new PointF(5f, 8.9f), new PointF(7.9f, 8.8f) });
                    break;
                }
            case Glyph.Record:
                {
                    // 摄像机：机身 + 镜头 + 上面那个小圆点
                    using var body = RoundedBox(2.6f, 6.2f, 10.8f, 7.6f, 2.2f);
                    g.DrawPath(pen, body);
                    using var lens = new GraphicsPath();
                    lens.AddLine(13.2f, 8.4f, 16.4f, 6.4f);
                    lens.AddLine(16.4f, 13.6f, 13.2f, 11.6f);
                    lens.CloseFigure();
                    g.DrawPath(pen, lens);
                    g.FillEllipse(brush, 4.4f, 8.6f, 2.6f, 2.6f);
                    break;
                }
            case Glyph.Hotkey:
                {
                    using var body = RoundedBox(2.2f, 4.6f, 13.6f, 9f, 2.6f);
                    g.DrawPath(pen, body);
                    foreach (var x in new[] { 4.8f, 7.4f, 10f, 12.6f })
                        g.FillEllipse(brush, x, 7f, 1.6f, 1.6f);
                    g.DrawLine(pen, 5.4f, 11.2f, 12.6f, 11.2f);
                    break;
                }
            case Glyph.Other:
                {
                    foreach (var y in new[] { 4.4f, 9f, 13.6f })
                        g.DrawLine(pen, 2.4f, y, 15.6f, y);
                    foreach (var (x, y) in new[] { (6.2f, 4.4f), (11.8f, 9f), (7.6f, 13.6f) })
                    {
                        using var knob = new SolidBrush(Theme.Card);
                        g.FillEllipse(knob, x - 2f, y - 2f, 4f, 4f);
                        g.DrawEllipse(pen, x - 2f, y - 2f, 4f, 4f);
                    }
                    break;
                }
        }

        g.Restore(save);
        g.SmoothingMode = old;
    }

    private static GraphicsPath RoundedBox(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
