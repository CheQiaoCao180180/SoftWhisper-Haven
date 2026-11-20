// 本机加（2026-11-18）：一键整理 Live2D 模型目录。
// 别人下载的模型大多有两个毛病：表情平铺在根目录（渲染器看不见）、model3.json 里 Expressions 是空的。
// 这个类把 tools\fix_model.py 的逻辑用 C# 重写一遍，设置工具里点一下就整理好，不用装 Python。

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SettingTool;

internal static class ModelFixer
{
    /// <summary>整理结果，给界面显示用。</summary>
    internal sealed class Result
    {
        internal bool Ok;
        internal string Model = "";
        internal int MovedExpressions;
        internal int MovedMotions;
        internal int Expressions;
        internal int Motions;
        internal string Lipsync = "";
        internal string Blink = "";
        internal string Message = "";
        internal readonly List<string> Notes = [];
    }

    // 参数名是从这些里挑的，越前面越标准
    private static readonly string[] LipsyncFirst = ["ParamMouthOpenY", "ParamMouthOpen", "ParamJawOpen", "ParamMouth", "ParamA"];
    private static readonly (string Left, string Right)[] BlinkPairs =
    [
        ("ParamEyeLOpen", "ParamEyeROpen"),
        ("ParamEyeLClose", "ParamEyeRClose"),
        ("ParamEyeOpen", "ParamEyeOpen"),
    ];
    private static readonly string[] BlinkSingle = ["ParamEyeOpen", "ParamEyeLOpen"];

    /// <summary>整理一个模型目录。dryRun 时只看不改。</summary>
    internal static Result Fix(string folder, bool dryRun)
    {
        var result = new Result();
        if (!Directory.Exists(folder))
        {
            result.Message = "没有这个目录：" + folder;
            return result;
        }

        // 找 model3.json
        var models = Directory.GetFiles(folder, "*.model3.json");
        if (models.Length == 0)
        {
            result.Message = "这个目录里没有 *.model3.json";
            return result;
        }
        Array.Sort(models, (a, b) => Path.GetFileName(a).Length.CompareTo(Path.GetFileName(b).Length));
        var modelFile = models[0];
        result.Model = Path.GetFileName(modelFile);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(modelFile))?.AsObject() ?? [];
        }
        catch (Exception error)
        {
            result.Message = "读不了 model3.json：" + error.Message;
            return result;
        }

        // ── 1/2. 搬表情和动作
        var expDir = Path.Combine(folder, "Exp3");
        var animDir = Path.Combine(folder, "Animation");
        foreach (var path in Directory.GetFiles(folder))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".exp3.json", StringComparison.OrdinalIgnoreCase))
            {
                result.MovedExpressions++;
                if (dryRun) continue;
                Directory.CreateDirectory(expDir);
                SafeMove(path, Path.Combine(expDir, name));
            }
            else if (name.EndsWith(".motion3.json", StringComparison.OrdinalIgnoreCase))
            {
                result.MovedMotions++;
                if (dryRun) continue;
                Directory.CreateDirectory(animDir);
                SafeMove(path, Path.Combine(animDir, name));
            }
        }

        // ── 3. 表情清单
        var expressions = new JsonArray();
        if (Directory.Exists(expDir))
        {
            foreach (var path in Directory.GetFiles(expDir, "*.exp3.json").OrderBy(p => p))
            {
                var name = Path.GetFileName(path);
                expressions.Add(new JsonObject
                {
                    ["Name"] = name[..^".exp3.json".Length],
                    // 本机修（2026-11-19）：文件已经搬进 Exp3\，这里必须带子目录前缀，否则 Cubism 找不到文件、
                    // 报 Invalid Json document 然后整个模型加载失败（夜兰就是这么崩的）。
                    ["File"] = "Exp3/" + name,
                });
            }
        }
        if (expressions.Count > 0)
        {
            result.Expressions = expressions.Count;
            root["FileReferences"] ??= new JsonObject();
            root["FileReferences"]!["Expressions"] = expressions;
        }

        // ── 4. 动作清单
        var motions = new JsonArray();
        if (Directory.Exists(animDir))
        {
            foreach (var path in Directory.GetFiles(animDir, "*.motion3.json").OrderBy(p => p))
            {
                motions.Add(new JsonObject
                {
                    // 同上：动作已经搬进 Animation\，路径要带上前缀。
                    ["File"] = "Animation/" + Path.GetFileName(path),
                    ["FadeInTime"] = 0.3,
                    ["FadeOutTime"] = 0.3,
                });
            }
        }
        if (motions.Count > 0)
        {
            result.Motions = motions.Count;
            root["FileReferences"] ??= new JsonObject();
            root["FileReferences"]!["Motions"] = new JsonObject { ["Idle"] = motions };
        }

        // ── 5. 口型 / 眨眼
        var names = ParametersOf(folder);
        if (names.Count > 0)
        {
            var lips = GuessLipsync(names);
            var blink = GuessBlink(names);
            result.Lipsync = lips.Count > 0 ? string.Join(" + ", lips) : "（没找到，说话时嘴不会动）";
            result.Blink = blink.Count > 0 ? string.Join(" + ", blink) : "（没找到，眼睛不会眨）";

            root["Groups"] ??= new JsonArray();
            var groups = root["Groups"]!.AsArray();
            JsonObject? lipsGroup = null, blinkGroup = null;
            foreach (var one in groups)
            {
                if (one is not JsonObject obj) continue;
                var label = obj["Name"]?.GetValue<string>();
                if (label == "LipSync") lipsGroup = obj;
                else if (label == "EyeBlink") blinkGroup = obj;
            }
            if (lipsGroup == null)
            {
                lipsGroup = new JsonObject { ["Target"] = "Parameter", ["Name"] = "LipSync", ["Ids"] = new JsonArray() };
                groups.Add(lipsGroup);
            }
            if (blinkGroup == null)
            {
                blinkGroup = new JsonObject { ["Target"] = "Parameter", ["Name"] = "EyeBlink", ["Ids"] = new JsonArray() };
                groups.Add(blinkGroup);
            }
            if (lips.Count > 0) lipsGroup["Ids"] = ToArray(lips);
            if (blink.Count > 0) blinkGroup["Ids"] = ToArray(blink);
        }
        else
        {
            result.Notes.Add("cdi3.json 里没有参数清单，口型和眨眼先空着（不影响显示）");
        }

        if (dryRun)
        {
            result.Ok = true;
            result.Message = "演练完成，没有改动任何文件";
            return result;
        }

        // ── 写回（留备份）
        try
        {
            var backup = modelFile + ".bak";
            if (!File.Exists(backup)) File.Copy(modelFile, backup);
            File.WriteAllText(modelFile, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                // 本机修（2026-11-18）：默认编码器会把中文转成 \uXXXX，Cubism 的 json 解析器认不出，模型直接报错。
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }),
                new UTF8Encoding(false));
            result.Ok = true;
            result.Message = "整理完成，原件留了 .bak";
        }
        catch (Exception error)
        {
            result.Message = "写回失败：" + error.Message;
        }
        return result;
    }

    private static void SafeMove(string from, string to)
    {
        try
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
        catch { }
    }

    private static JsonArray ToArray(List<string> items)
    {
        var array = new JsonArray();
        foreach (var one in items) array.Add(one);
        return array;
    }

    /// <summary>从 cdi3.json 里读参数名（读不到就返回空）。</summary>
    private static HashSet<string> ParametersOf(string folder)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var models = Directory.GetFiles(folder, "*.model3.json");
        if (models.Length == 0) return names;
        var baseName = models[0][..^".model3.json".Length];
        var cdi = baseName + ".cdi3.json";
        if (!File.Exists(cdi)) return names;
        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(cdi))?.AsObject();
            if (doc?["Parameters"] is JsonArray list)
                foreach (var one in list)
                    if (one?["Id"]?.GetValue<string>() is { Length: > 0 } id) names.Add(id);
        }
        catch { }
        return names;
    }

    private static List<string> GuessLipsync(HashSet<string> names)
    {
        foreach (var one in LipsyncFirst)
            if (names.Contains(one)) return [one];
        return [];
    }

    private static List<string> GuessBlink(HashSet<string> names)
    {
        foreach (var (left, right) in BlinkPairs)
            if (names.Contains(left) && names.Contains(right))
                return left == right ? [left] : [left, right];
        foreach (var one in BlinkSingle)
            if (names.Contains(one)) return [one];
        return [];
    }
}
