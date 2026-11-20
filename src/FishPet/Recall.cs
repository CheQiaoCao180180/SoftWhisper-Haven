using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FishPet;

// 记忆连续性（本机加，2026-11-16）：她会自己提起小本本里的事，不是只等人问。
// 每天最多一次；只念「事实」这类，纪念日交给生日/节日那套，别重复。
internal static class Recall
{
    // 把一条记忆说出口的几种说法
    private static readonly string[] Forms =
    [
        "对了，{0}……我记着呢",
        "我还记得哦，{0}",
        "{0}——这件事我一直没忘",
        "忽然想起来，{0}",
    ];

    private static string SaidFile => Path.Combine(Places.Cache, "recall-once.txt");

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    // 今天说过没有
    internal static bool SaidToday()
    {
        try { return File.Exists(SaidFile) && File.ReadAllText(SaidFile).Trim() == Today; }
        catch { return false; }
    }

    private static void MarkSaid()
    {
        try
        {
            Directory.CreateDirectory(Places.Cache);
            File.WriteAllText(SaidFile, Today);
        }
        catch { }
    }

    // 挑一条可以提的旧事，套上说法；没有就返回 null
    internal static string? Line(Random random)
    {
        if (SaidToday()) return null;
        List<string> facts = [];
        try
        {
            var file = Path.Combine(Places.Root, "memory.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) continue;
                var category = item.TryGetProperty("category", out var cat) ? cat.GetString() ?? "" : "";
                if (category is "纪念日") continue; // 生日节日有专门的台词
                var text = (content.GetString() ?? "").Trim();
                if (text.Length < 4 || text.Length > 40) continue;
                facts.Add(text);
            }
        }
        catch { return null; }
        if (facts.Count == 0) return null;
        var picked = facts[random.Next(facts.Count)];
        var form = Forms[random.Next(Forms.Length)];
        MarkSaid();
        return string.Format(form, picked);
    }
}
