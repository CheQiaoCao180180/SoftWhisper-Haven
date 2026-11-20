using System;

namespace FishPet;

// 掷骰子小游戏（本机加，2026-11-16）：双击她一次，掷一颗六面骰。
internal static class Dice
{
    private static readonly string[] Faces = ["一", "二", "三", "四", "五", "六"];

    // 台词跟着点数走：6 得意、1 委屈、其他随口
    internal static string Normalized(int value)
    {
        var face = Faces[Math.Clamp(value, 1, 6) - 1];
        string[] lines = value switch
        {
            6 => ["六点！今天运气不错哦", "满点！看来该去买彩票的是你", "六、六！我赢了！"],
            5 => ["五点，还行吧", "五点～不算差"],
            4 => ["四点，普普通通", "四点，中不溜"],
            3 => ["三点……有点小", "三点，刚好一半"],
            2 => ["两点，再来一次？", "两点呀……手气不太好"],
            1 => ["一点……我重新掷一下好不好", "一点！这也太惨了", "一点，别看了别看了"],
            _ => ["掷出来了"],
        };
        var pick = lines[Random.Shared.Next(lines.Length)];
        return "〔微点头〕" + pick + "（" + face + "点）";
    }
}
