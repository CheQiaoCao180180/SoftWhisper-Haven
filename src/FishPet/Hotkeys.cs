// 本机加（2026-11-17）：快捷键可自定义。
// 以前这几个键写死在 PetWindow 里，现在从 settings.json 读，设置工具里也能录。
// 格式："Ctrl+Alt+X" / "Ctrl+Shift+0" / "Alt+F5" 这样，修饰键 + 一个主键。

using System;
using System.Collections.Generic;
using System.Linq;

namespace FishPet;

internal static class Hotkeys
{
    /// <summary>描述一个热键：修饰键位 + 虚拟键码。绑不上就返回 null。</summary>
    internal readonly record struct Spec(uint Modifiers, uint VirtualKey)
    {
        internal bool IsValid => VirtualKey != 0;
    }

    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008;

    // 主键名 → 虚拟键码
    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E,
    };

    /// <summary>把 "Ctrl+Alt+X" 这样的写法转成热键。写不对就返回一个无效的。</summary>
    internal static Spec Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;

        uint modifiers = 0;
        uint key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; continue;
                case "alt": modifiers |= ModAlt; continue;
                case "shift": modifiers |= ModShift; continue;
                case "win" or "windows": modifiers |= ModWin; continue;
            }

            key = VirtualKey(raw);
            if (key == 0) return default;
        }

        return key == 0 ? default : new Spec(modifiers, key);
    }

    private static uint VirtualKey(string name)
    {
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c >= 'A' && c <= 'Z') return c;                  // A–Z
            if (c >= '0' && c <= '9') return (uint)c;            // 0–9
            if (c == ' ') return 0x20;
        }

        if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[1..], out var fn) && fn >= 1 && fn <= 24)
            return (uint)(0x70 + fn - 1);                        // F1–F24

        return Named.TryGetValue(name, out var code) ? code : 0;
    }

    /// <summary>反过来：把热键写成字符串（给设置工具显示用）。</summary>
    internal static string Describe(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");

        parts.Add(key switch
        {
            >= 0x30 and <= 0x39 => ((char)key).ToString(),
            >= 0x41 and <= 0x5A => ((char)key).ToString(),
            >= 0x70 and <= 0x87 => "F" + (key - 0x70 + 1),
            _ => Named.FirstOrDefault(pair => pair.Value == key).Key ?? ("0x" + key.ToString("X2")),
        });

        return string.Join("+", parts);
    }

    // ── 五个默认键（用户没设过就用这些）
    internal const string DefaultShape = "Ctrl+Shift+0";
    internal const string DefaultPersona = "Ctrl+Alt+X";
    internal const string DefaultGlasses = "Ctrl+Alt+B";
    internal const string DefaultShoes = "Ctrl+Alt+C";
    internal const string DefaultSkirt = "Ctrl+Alt+K";
    internal const string DefaultChat = "Ctrl+Alt+T";
}
