// 重写版（2026-11-17）：读写 settings.json。
// 只写认识的字段，不认识的（比如桌宠自己存的坐标）原样保留。

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SettingTool;

internal sealed class SettingsStore
{
    internal static string Root => Program.Root;
    internal static string Path => System.IO.Path.Combine(Root, "settings.json");

    private JsonObject data = new();

    internal void Load()
    {
        try
        {
            data = File.Exists(Path)
                ? JsonNode.Parse(File.ReadAllText(Path)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch
        {
            data = new JsonObject();
        }
    }

    internal void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = data.ToJsonString(options);

        // 本机改（2026-11-17）：先写临时文件、写完整了再整体替换。
        // 以前是 File.WriteAllText 直接覆盖——正好在写的时候被关掉/断电，
        // settings.json 就只剩半截，桌宠下次启动读不出来。
        // 编码写死 UTF-8 无 BOM：带 BOM 的 JSON 交给 JSON.parse 这类解析器会直接报错。
        var temp = Path + ".tmp";
        File.WriteAllText(temp, json, new System.Text.UTF8Encoding(false));

        try
        {
            if (File.Exists(Path))
            {
                // 留一份上一版：新配置要是把桌宠搞出毛病，把 settings.json.bak 改名回去就能恢复
                try { File.Copy(Path, Path + ".bak", true); } catch { }

                try
                {
                    File.Replace(temp, Path, null);
                }
                catch (IOException)
                {
                    // Replace 要求目标文件没被占用；桌宠正占着的时候退回到覆盖移动
                    File.Move(temp, Path, true);
                }
            }
            else
            {
                File.Move(temp, Path, true);
            }
        }
        catch
        {
            // 失败就把临时文件清掉，别在插件根目录留垃圾
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    // ── 读
    internal bool Flag(string key, bool fallback = false)
        => data[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    internal int Int(string key, int fallback)
        => data[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : fallback;

    internal double Number(string key, double fallback)
        => data[key] is JsonValue v && v.TryGetValue<double>(out var n) ? n : fallback;

    internal string Text(string key, string fallback = "")
        => data[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    internal string[] List(string key)
    {
        if (data[key] is not JsonArray array) return [];
        return array.Select(node => node?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToArray();
    }

    // ── 写
    internal void Set(string key, bool value) => data[key] = value;
    internal void Set(string key, int value) => data[key] = value;
    internal void Set(string key, double value) => data[key] = value;
    internal void Set(string key, string value) => data[key] = value;
    internal void Set(string key, IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var one in values) array.Add(one);
        data[key] = array;
    }
}
