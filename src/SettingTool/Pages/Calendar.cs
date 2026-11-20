// 本机加（2026-11-18）：日历页。
// 看这个月有什么日子——生日、纪念日（记忆本里带 date 的）。
// 数据从 settings.json 的 Birthday 和 memory.json 的 items[].date 来。

using System.Text.Json;

namespace SettingTool;

internal sealed partial class MainForm
{
    private NeoCalendar? calendar;
    private Label? calendarInfo;
    // 本机加（2026-11-18）：那天自己记的事，存在 schedule.json
    private TextBox? scheduleBox;
    private NeoButton? scheduleSave;
    private static readonly string ScheduleFile = "schedule.json";
    // 本机加（2026-11-18）：快速跳转 + 查找
    private TextBox? jumpBox;
    private TextBox? findBox;
    private Label? findResult;

    private FlowLayoutPanel BuildCalendarPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("日历", "看看这个月有什么日子。生日、纪念日会标出来。", CardWidth));

        var card = new Card("月历", CardWidth);
        calendar = new NeoCalendar(CardWidth - 60, 318) { Tag = "wide" };
        calendar.DayPicked += (_, _) => ShowDayInfo();
        card.AddControl(calendar);

        var bar = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = Theme.Card, Margin = new Padding(0, 8, 0, 4),
        };
        var prev = new NeoButton("上月") { Width = 70, Height = 32 };
        prev.Click += (_, _) => { calendar.Flip(-1); RefreshCalendar(); };
        var today = new NeoButton("今天", ButtonKind.Outline) { Width = 70, Height = 32 };
        today.Click += (_, _) => { calendar.GoToday(); RefreshCalendar(); };
        var next = new NeoButton("下月") { Width = 70, Height = 32 };
        next.Click += (_, _) => { calendar.Flip(1); RefreshCalendar(); };
        bar.Controls.Add(prev);
        bar.Controls.Add(today);
        bar.Controls.Add(next);

        card.AddControl(bar);

        // 本机改（2026-11-18）：第二行 —— 跳转 + 查找放一起，别挤在翻月那行
        var toolRow = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = Theme.Card, Margin = new Padding(0, 2, 0, 4),
        };

        // 快速跳转：输入 2026-03 或 2026/3 或 202603
        jumpBox = new TextBox
        {
            Width = 96, Font = Theme.Mono, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White, ForeColor = Theme.Ink,
            PlaceholderText = "2026-03", Margin = new Padding(0, 4, 6, 0),
        };
        var jumpGo = new NeoButton("跳过去", ButtonKind.Outline) { Width = 72, Height = 30, Margin = new Padding(0, 4, 18, 0) };
        jumpGo.Click += (_, _) => JumpTo();
        jumpBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { JumpTo(); e.SuppressKeyPress = true; } };
        toolRow.Controls.Add(jumpBox);
        toolRow.Controls.Add(jumpGo);

        // 查找：在日程和记忆里搜
        var searchRow = toolRow;
        findBox = new TextBox
        {
            Width = 180, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White, ForeColor = Theme.Ink,
            PlaceholderText = "搜日程 / 记忆里的字", Margin = new Padding(0, 4, 6, 0),
        };
        var findGo = new NeoButton("查找", ButtonKind.Outline) { Width = 62, Height = 30, Margin = new Padding(0, 4, 6, 0) };
        findGo.Click += (_, _) => RunFind();
        findBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { RunFind(); e.SuppressKeyPress = true; } };
        var findClear = new NeoButton("清空") { Width = 62, Height = 30, Margin = new Padding(0, 4, 0, 0) };
        findClear.Click += (_, _) => { if (findBox != null) findBox.Text = ""; if (findResult != null) findResult.Text = ""; if (calendar != null) { calendar.ClearHighlights(); calendar.Invalidate(); } };
        searchRow.Controls.Add(findBox);
        searchRow.Controls.Add(findGo);
        searchRow.Controls.Add(findClear);
        card.AddControl(searchRow);

        findResult = new Label
        {
            Text = "", AutoSize = false, Width = CardWidth - 60, Tag = "wide", Height = 46,
            Font = Theme.Small, ForeColor = Theme.InkSoft, BackColor = Theme.Card,
            TextAlign = ContentAlignment.TopLeft, Margin = new Padding(0, 2, 0, 2),
        };
        card.AddControl(findResult);

        calendarInfo = new Label
        {
            Text = "", AutoSize = false, Width = CardWidth - 60, Tag = "wide", Height = 60,
            Font = Theme.Small, ForeColor = Theme.InkSoft, BackColor = Theme.Card,
            TextAlign = ContentAlignment.TopLeft, Margin = new Padding(0, 6, 0, 0),
        };
        card.AddControl(calendarInfo);

        // 本机加（2026-11-18）：给这一天记点事
        card.AddControl(Hint("给这天记点事（一行一件，存到 schedule.json）："));
        scheduleBox = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
            Width = CardWidth - 60, Tag = "wide", Height = 90,
            Font = Theme.Body, BackColor = Color.White, ForeColor = Theme.Ink,
            Margin = new Padding(0, 4, 0, 4),
        };
        card.AddControl(scheduleBox);
        scheduleSave = new NeoButton("存到这天") { Width = 100 };
        scheduleSave.Click += (_, _) => SaveSchedule();
        card.AddControl(Row(scheduleSave));
        Mount(page, card);

        var legend = new Card("图例", CardWidth);
        legend.AddControl(Hint(
            "● 生日 —— settings.json 里的生日，一年一次\n" +
            "● 纪念日 —— 记忆本里登记过日期的条目\n" +
            "点某一天看那天有什么；没标的日子也能点，只是空白。"));
        Mount(page, legend);

        RefreshCalendar();
        return page;
    }

    /// <summary>把 settings 和记忆本里的日期喂给月历。</summary>
    private void RefreshCalendar()
    {
        if (calendar == null) return;
        calendar.ClearMarks();

        // 生日
        var birthday = store.Text("Birthday", "");
        if (!string.IsNullOrWhiteSpace(birthday))
        {
            var parts = birthday.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var d))
                calendar.Mark(m, d, "生日", Theme.Pink);
        }

        // 记忆本里带日期的（生日那条跳过，上面已经标了）
        try
        {
            var path = Path.Combine(Program.Root, "memory.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("items", out var items))
                {
                    foreach (var one in items.EnumerateArray())
                    {
                        if (!one.TryGetProperty("date", out var dv)) continue;
                        var text = dv.GetString() ?? "";
                        if (text.Length < 5) continue;
                        var bits = text.Split('-');
                        if (bits.Length < 2) continue;
                        if (!int.TryParse(bits[^2], out var mm) || !int.TryParse(bits[^1], out var dd)) continue;
                        // 生日那条已经标过，别再叠一次
                        if (bits[^2] == partsOr(birthday, 0) && bits[^1] == partsOr(birthday, 1)) continue;
                        var what = one.TryGetProperty("content", out var cv)
                            ? (cv.GetString() ?? "") : "";
                        if (what.Length > 16) what = what[..16];
                        calendar.Mark(mm, dd, what, Theme.Blue);
                    }
                }
            }
        }
        catch { }

        // 农历传统节日（红点）—— 只标显示中那一年
        MarkFestivals(calendar.Year);

        // 自己记的日程（橙色点）
        foreach (var (key, list) in ReadSchedule())
        {
            var bits = key.Split('-');
            if (bits.Length < 3) continue;
            if (!int.TryParse(bits[1], out var mm) || !int.TryParse(bits[2], out var dd)) continue;
            calendar.Mark(mm, dd, list.Count + " 件安排", Theme.Orange);
        }

        ShowDayInfo();
        calendar.Invalidate();
    }

    /// <summary>读 schedule.json：{ "2026-11-18": ["买米", "开会"] }</summary>
    private static Dictionary<string, List<string>> ReadSchedule()
    {
        var result = new Dictionary<string, List<string>>();
        try
        {
            var path = Path.Combine(Program.Root, ScheduleFile);
            if (!File.Exists(path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var one in doc.RootElement.EnumerateObject())
            {
                var list = new List<string>();
                foreach (var v in one.Value.EnumerateArray())
                    if (v.GetString() is { Length: > 0 } text) list.Add(text);
                if (list.Count > 0) result[one.Name] = list;
            }
        }
        catch { }
        return result;
    }

    private void SaveSchedule()
    {
        if (calendar == null || scheduleBox == null) return;
        var key = calendar.Picked.ToString("yyyy-MM-dd");
        var all = ReadSchedule();
        var lines = scheduleBox.Text.Split('\n')
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (lines.Count == 0) all.Remove(key);
        else all[key] = lines;

        try
        {
            var path = Path.Combine(Program.Root, ScheduleFile);
            var obj = new System.Text.Json.Nodes.JsonObject();
            foreach (var (k, v) in all.OrderBy(x => x.Key))
            {
                var arr = new System.Text.Json.Nodes.JsonArray();
                foreach (var text in v) arr.Add(text);
                obj[k] = arr;
            }
            File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(false));
            Note($"已存 {key} 的 {lines.Count} 件事");
            RefreshCalendar();
        }
        catch (Exception error)
        {
            Note("保存失败：" + error.Message);
        }
    }

    // 本机加（2026-11-18）：中国传统节日。农历换算用 .NET 自带的 ChineseLunisolarCalendar。
    // (农历月, 农历日, 名字)；除夕单独处理（腊月最后一天），清明按节气近似。
    private static readonly (int Month, int Day, string Name)[] LunarFestivals =
    [
        (1, 1, "春节"), (1, 15, "元宵"), (2, 2, "龙抬头"),
        (5, 5, "端午"), (7, 7, "七夕"), (7, 15, "中元"),
        (8, 15, "中秋"), (9, 9, "重阳"), (10, 1, "寒衣"),
        (12, 8, "腊八"), (12, 23, "小年"),
    ];

    /// <summary>把某一年的农历节日换算成公历，喂给月历。</summary>
    private void MarkFestivals(int year)
    {
        if (calendar == null) return;
        var lunar = new System.Globalization.ChineseLunisolarCalendar();
        foreach (var (month, day, name) in LunarFestivals)
        {
            try
            {
                var date = lunar.ToDateTime(year, month, day, 0, 0, 0, 0);
                calendar.Mark(date.Month, date.Day, name, Theme.Red);
            }
            catch { }
        }

        // 除夕：腊月的最后一天。农历腊月是第 12 个（闰月会让月份号偏移）
        try
        {
            var leap = lunar.GetLeapMonth(year);
            var lastMonth = leap == 13 ? 13 : 12;   // 腊月
            var lastDay = lunar.GetDaysInMonth(year, lastMonth);
            var eve = lunar.ToDateTime(year, lastMonth, lastDay, 0, 0, 0, 0);
            calendar.Mark(eve.Month, eve.Day, "除夕", Theme.Red);
        }
        catch { }

        // 清明：公历 4 月 4~6 之间，按年份算（这里用近似公式）
        try
        {
            var y = year % 100;
            var qingmingDay = (int)(y * 0.2422 + 4.81) - (y / 4);
            calendar.Mark(4, Math.Clamp(qingmingDay, 4, 6), "清明", Theme.Red);
        }
        catch { }
    }

    /// <summary>「2026-03」「2026/3」「202603」都认。</summary>
    private void JumpTo()
    {
        if (jumpBox == null || calendar == null) return;
        var raw = new string(jumpBox.Text.Where(char.IsDigit).ToArray());
        if (raw.Length < 4) { Note("写成 2026-03 这样"); return; }
        var year = int.Parse(raw[..4]);
        var month = raw.Length >= 6 ? int.Parse(raw.Substring(4, 2)) : (raw.Length == 5 ? int.Parse(raw[4..]) : 1);
        if (month < 1 || month > 12) { Note("月份要 1~12"); return; }
        calendar.GoTo(year, month);
        RefreshCalendar();
        Note($"跳到 {year} 年 {month} 月");
    }

    /// <summary>在 schedule.json 和 memory.json 里搜字。</summary>
    private void RunFind()
    {
        if (findBox == null || findResult == null || calendar == null) return;
        var what = findBox.Text.Trim();
        calendar.ClearHighlights();
        if (what.Length == 0) { findResult.Text = ""; calendar.Invalidate(); return; }

        var hits = new List<string>();
        // 搜日程
        foreach (var (key, list) in ReadSchedule().OrderBy(x => x.Key))
        {
            foreach (var one in list)
                if (one.Contains(what, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add($"{key}  {one}");
                    if (DateTime.TryParse(key, out var date)) calendar.Highlight(date.Month, date.Day);
                }
        }
        // 搜记忆
        try
        {
            var path = Path.Combine(Program.Root, "memory.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("items", out var items))
                    foreach (var one in items.EnumerateArray())
                    {
                        var text = one.TryGetProperty("content", out var cv) ? cv.GetString() ?? "" : "";
                        if (!text.Contains(what, StringComparison.OrdinalIgnoreCase)) continue;
                        var date = one.TryGetProperty("date", out var dv) ? dv.GetString() ?? "" : "";
                        hits.Add((date.Length > 0 ? date + "  " : "") + text);
                        if (date.Length >= 5)
                        {
                            var bits = date.Split('-');
                            if (bits.Length >= 2 && int.TryParse(bits[^2], out var mm) && int.TryParse(bits[^1], out var dd))
                                calendar.Highlight(mm, dd);
                        }
                    }
                }
            }
        catch { }

        calendar.Invalidate();
        findResult.Text = hits.Count == 0
            ? $"没找到「{what}」"
            : $"找到 {hits.Count} 条：\n" + string.Join("\n", hits.Take(4)) + (hits.Count > 4 ? $"\n…还有 {hits.Count - 4} 条" : "");
    }

    private static string partsOr(string birthday, int index)
    {
        var p = birthday.Split('-');
        return index < p.Length ? p[index] : "";
    }

    /// <summary>底部那行：选中的那天有什么。</summary>
    private void ShowDayInfo()
    {
        if (calendar == null || calendarInfo == null) return;
        var marks = calendar.MarksOnPicked();
        var title = calendar.Picked.ToString("M 月 d 日 dddd");

        // 那天的安排填进输入框
        var key = calendar.Picked.ToString("yyyy-MM-dd");
        var mine = ReadSchedule().TryGetValue(key, out var list) ? list : [];
        if (scheduleBox != null) scheduleBox.Text = string.Join("\r\n", mine);
        if (marks.Count == 0)
        {
            calendarInfo.Text = title + "\n那天没有标记的日子。";
            return;
        }
        calendarInfo.Text = title + "\n" + string.Join("\n", marks.Select(m => "● " + m));
    }
}

/// <summary>
/// 自绘月历。7 列 × 6 行，点一天就选中；有标记的日子画个点。
/// </summary>
internal sealed class NeoCalendar : Control
{
    private static readonly string[] Headers = ["日", "一", "二", "三", "四", "五", "六"];
    private DateTime shown = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime picked = DateTime.Today;
    private readonly Dictionary<(int Month, int Day), List<(string What, Color Tint)>> marks = new();
    private readonly HashSet<(int Month, int Day)> highlights = new();

    private const int TitleH = 34;
    private const int HeadH = 30;
    private const int CellH = 44;

    internal event EventHandler? DayPicked;

    internal DateTime Picked => picked;
    internal int Year => shown.Year;

    internal NeoCalendar(int width, int height)
    {
        Width = width;
        Height = height;
        BackColor = Theme.Card;
        Font = Theme.Body;
        Margin = new Padding(0, 2, 0, 4);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    // 改个名，别跟 Control.Move 撞（CS0108）
    internal void Flip(int months) { shown = shown.AddMonths(months); picked = new DateTime(shown.Year, shown.Month, 1); }
    internal void GoTo(int year, int month)
    {
        shown = new DateTime(year, month, 1);
        picked = shown;
    }

    /// <summary>搜索结果里要标的日子（黄框）。</summary>
    internal void Highlight(int month, int day) => highlights.Add((month, day));
    internal void ClearHighlights() => highlights.Clear();

    internal void GoToday() { shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); picked = DateTime.Today; }
    internal void ClearMarks() => marks.Clear();
    internal void Mark(int month, int day, string what, Color tint)
    {
        var key = (month, day);
        if (!marks.TryGetValue(key, out var list)) marks[key] = list = [];
        list.Add((what, tint));
    }

    internal List<string> MarksOnPicked()
    {
        if (!marks.TryGetValue((picked.Month, picked.Day), out var list)) return [];
        return [.. list.Select(m => m.What)];
    }

    // 本机加（2026-11-18）：农历小字。这里自己算，不依赖外部日历。
    private static readonly string[] LunarDays =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十",
    ];

    /// <summary>这一天的农历文字：初一显示月名（正月 / 八月），其余显示日名。</summary>
    private static string LunarLabel(DateTime date)
    {
        try
        {
            var lunar = new System.Globalization.ChineseLunisolarCalendar();
            var day = lunar.GetDayOfMonth(date);
            if (day == 1)
            {
                // 初一显示月份：正月 / 二月 / …… / 腊月
                var month = lunar.GetMonth(date);
                var leap = lunar.GetLeapMonth(lunar.GetYear(date));
                var actual = leap > 0 && month >= leap ? month - 1 : month;
                return actual switch
                {
                    1 => "正月", 11 => "冬月", 12 => "腊月",
                    _ => MonthNames[(actual - 1) % 10],
                };
            }
            return day >= 1 && day <= 30 ? LunarDays[day - 1] : "";
        }
        catch { return ""; }
    }

    private static readonly string[] MonthNames = ["二", "三", "四", "五", "六", "七", "八", "九", "十", "十一"];

    // 这个月的网格：从周日开始，前面补空
    private DateTime? CellAt(int index)
    {
        var first = new DateTime(shown.Year, shown.Month, 1);
        var offset = (int)first.DayOfWeek;
        var day = index - offset + 1;
        if (day < 1 || day > DateTime.DaysInMonth(shown.Year, shown.Month)) return null;
        return new DateTime(shown.Year, shown.Month, day);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var cols = 7;
        var usable = Width;
        if (Parent != null && Parent.ClientSize.Width > 100) usable = Math.Min(usable, Parent.ClientSize.Width);
        var w = Math.Max(1, usable / cols);
        var col = e.X / w;
        var row = (e.Y - TitleH - HeadH) / CellH;
        if (row < 0 || col < 0) return;
        var cell = CellAt(row * cols + col);
        if (cell == null) return;
        picked = cell.Value;
        Invalidate();
        DayPicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Theme.Text(g);
        g.Clear(BackColor);

        var cols = 7;
        // 用实际能画到的宽度，别用 Width —— 卡片里的容器会把它吃掉一截
        var usable = Width;
        if (Parent != null && Parent.ClientSize.Width > 100) usable = Math.Min(usable, Parent.ClientSize.Width);
        var w = Math.Max(1, usable / cols);

        // 月份标题单独一行（原来叠在表头右边，把「六」挡住了）
        using (var title = new SolidBrush(Theme.Ink))
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            g.DrawString(shown.ToString("yyyy 年 M 月"), Theme.Section, title,
                new RectangleF(2, 0, usable - 4, TitleH), fmt);
        }

        // 表头：日 一 二 三 四 五 六
        using (var head = new SolidBrush(Theme.BluePale))
            g.FillRectangle(head, 0, TitleH, usable, HeadH);
        using (var ink = new SolidBrush(Theme.InkSoft))
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (var i = 0; i < 7; i++)
                g.DrawString(Headers[i], Theme.Small, ink, new RectangleF(i * w, TitleH, w, HeadH), fmt);
        }

        // 格子
        var fmtCell = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (var index = 0; index < 42; index++)
        {
            var row = index / cols;
            var col = index % cols;
            var rect = new Rectangle(col * w, TitleH + HeadH + row * CellH, w, CellH);

            var cell = CellAt(index);
            if (cell == null) continue;

            var isPicked = cell.Value.Date == picked.Date;
            var isToday = cell.Value.Date == DateTime.Today;
            var hasMark = marks.TryGetValue((cell.Value.Month, cell.Value.Day), out var list);

            // 搜索命中的日子描一圈
            if (highlights.Contains((cell.Value.Month, cell.Value.Day)))
            {
                using var pen = new Pen(Theme.Orange, 2f);
                using var path = Theme.Rounded(new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8), 8);
                g.DrawPath(pen, path);
            }

            if (isPicked)
            {
                using var fill = new SolidBrush(Theme.BluePale);
                using var path = Theme.Rounded(new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8), 8);
                g.FillPath(fill, path);
            }

            using var ink = new SolidBrush(isToday ? Theme.Blue : Theme.Ink);
            // 1 号用粗一点的字体标出来；不能 using——Theme 里的是共享字体，Dispose 掉整个界面都会废
            var dayFont = cell.Value.Day is 1 ? Theme.Section : Font;
            g.DrawString(cell.Value.Day.ToString(), dayFont, ink,
                new RectangleF(rect.X, rect.Y + 1, rect.Width, rect.Height - 22), fmtCell);

            // 本机加（2026-11-18）：数字下面写农历（节日那天写节日名，红的）
            var lunar = LunarLabel(cell.Value);
            if (hasMark && marks[(cell.Value.Month, cell.Value.Day)].Any(m => m.Tint == Theme.Red))
                lunar = marks[(cell.Value.Month, cell.Value.Day)].First(m => m.Tint == Theme.Red).What;
            if (lunar.Length > 0)
            {
                using var sub = new SolidBrush(isToday ? Theme.Blue : Theme.InkSoft);
                g.DrawString(lunar, Theme.Tiny, sub,
                    new RectangleF(rect.X, rect.Y + rect.Height - 19, rect.Width, 12), fmtCell);
            }

            // 有标记画点（本机修 2026-11-19）：
            // 原来三个点全画在同一个 y 上（dotY -= 0 等于没动），叠成一坨，还压在农历字上。
            // 现在横向排开、贴着格子底边，最多三颗。
            if (hasMark)
            {
                var dots = list!.Take(3).ToList();
                var dotSize = 5;
                var gap = 3;
                var total = dots.Count * dotSize + (dots.Count - 1) * gap;
                var startX = rect.X + (rect.Width - total) / 2;
                var dotY = rect.Y + rect.Height - dotSize - 2;
                for (var i = 0; i < dots.Count; i++)
                {
                    using var brush = new SolidBrush(dots[i].Tint);
                    g.FillEllipse(brush, startX + i * (dotSize + gap), dotY, dotSize, dotSize);
                }
            }
        }
    }
}
