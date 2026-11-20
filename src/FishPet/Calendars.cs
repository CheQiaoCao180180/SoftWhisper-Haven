// 本机加（2026-11-18）：和日历对应的节日判断。
// 农历用 .NET 自带的 ChineseLunisolarCalendar —— 和设置工具里日历页那张表同一套。
// 结果是个「节日名」，去 lines\<语言>\festivals.txt 里找同名那一段。

using System.Globalization;

namespace FishPet;

internal static class Calendars
{
    // 农历节日的固定日子（月, 日）；除夕单独算（腊月最后一天）
    private static readonly (int Month, int Day, string Name)[] Lunar =
    [
        (1, 1, "春节"), (1, 15, "元宵"), (2, 2, "龙抬头"),
        (5, 5, "端午"), (7, 7, "七夕"), (7, 15, "中元"),
        (8, 15, "中秋"), (9, 9, "重阳"), (10, 1, "寒衣"),
        (12, 8, "腊八"), (12, 23, "小年"),
    ];

    // 公历的
    private static readonly (int Month, int Day, string Name)[] Solar =
    [
        (1, 1, "元旦"), (2, 14, "情人节"), (12, 25, "圣诞"),
    ];

    /// <summary>今天是哪个节日；没有就返回 null。</summary>
    internal static string? Today(DateTime now)
    {
        foreach (var (month, day, name) in Solar)
            if (now.Month == month && now.Day == day) return name;

        var lunar = new ChineseLunisolarCalendar();
        int lunarYear, lunarMonth, lunarDay;
        try
        {
            lunarYear = lunar.GetYear(now);
            lunarMonth = lunar.GetMonth(now);
            lunarDay = lunar.GetDayOfMonth(now);
        }
        catch { return null; }

        // 闰月会让月份号往后偏，真正用的时候要减掉
        var leap = lunar.GetLeapMonth(lunarYear);
        var actual = leap > 0 && lunarMonth >= leap ? lunarMonth - 1 : lunarMonth;

        foreach (var (month, day, name) in Lunar)
            if (actual == month && lunarDay == day) return name;

        // 除夕：腊月的最后一天（闰月时腊月是第 13 个）
        try
        {
            var lastMonth = leap == 13 ? 13 : 12;
            var lastDay = lunar.GetDaysInMonth(lunarYear, lastMonth);
            if (lunarMonth == lastMonth && lunarDay == lastDay) return "除夕";
        }
        catch { }

        return null;
    }

    /// <summary>清明按节气算，不是农历 —— 单独判断（公历 4 月 4~6 日）。</summary>
    internal static bool IsQingming(DateTime now)
    {
        if (now.Month != 4) return false;
        var y = now.Year % 100;
        var day = (int)(y * 0.2422 + 4.81) - (y / 4);
        return now.Day == Math.Clamp(day, 4, 6);
    }
}
