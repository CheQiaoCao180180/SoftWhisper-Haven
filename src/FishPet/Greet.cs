using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FishPet;

// 开机／每次启动的问候（本机加，2026-11-15）：按启动时的钟点挑一组，随机抽一句。
// 台词在 greet.txt：时段|台词|动作。一天之内同一句不会重复（记在 cache\greet-once.txt）。
internal static class Greet
{
    // 现在属于哪个时段
    private static string Slot(DateTime now) => now.Hour switch
    {
        >= 5 and < 11 => "早上",
        >= 11 and < 14 => "中午",
        >= 14 and < 18 => "下午",
        >= 18 and < 21 => "傍晚",
        >= 21 or < 0 => "晚上",
        _ => "深夜",
    };

    // 挑一句；没有可说的就返回 null
    internal static (string Text, string? Cue)? Pick(DateTime now, Random random, string persona = "", string language = "zh")
    {
        var slot = Slot(now);
        string[] lines;
        try
        {
            // 本机改（2026-11-17）：从 lines\<语言>\greet.txt 读，没有就退回中文
            var file = LineFiles.Pick(language, "greet.txt");
            if (!File.Exists(file)) return null;
            lines = File.ReadAllLines(file);
        }
        catch { return null; }

        var said = SaidToday(now);
        var pool = new List<(string Text, string? Cue)>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            // 本机加（2026-11-16）：时段名可以带人格前缀「病娇.早上」，只在那个人格时用
            var slotName = parts[0].Trim();
            int dot = slotName.IndexOf('.');
            if (dot > 0)
            {
                if (slotName[..dot] != persona) continue;
                slotName = slotName[(dot + 1)..];
            }
            if (slotName != slot) continue;
            var text = parts[1].Trim();
            if (text.Length == 0 || said.Contains(text)) continue;
            var cue = parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : null;
            pool.Add((text, cue));
        }
        if (pool.Count == 0) return null;
        var picked = pool[random.Next(pool.Count)];
        Remember(now, picked.Text);
        return picked;
    }

    private static string SaidFile => Path.Combine(Places.Cache, "greet-once.txt");

    private static HashSet<string> SaidToday(DateTime now)
    {
        var set = new HashSet<string>();
        try
        {
            if (!File.Exists(SaidFile)) return set;
            foreach (var line in File.ReadAllLines(SaidFile))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2 && parts[0] == now.ToString("yyyy-MM-dd")) set.Add(parts[1]);
            }
        }
        catch { }
        return set;
    }

    private static void Remember(DateTime now, string text)
    {
        try
        {
            Directory.CreateDirectory(Places.Cache);
            var keep = File.Exists(SaidFile)
                ? File.ReadAllLines(SaidFile).Where(line => line.StartsWith(now.ToString("yyyy-MM-dd") + "\t", StringComparison.Ordinal))
                : [];
            File.WriteAllLines(SaidFile, keep.Concat([now.ToString("yyyy-MM-dd") + "\t" + text]));
        }
        catch { }
    }
}
