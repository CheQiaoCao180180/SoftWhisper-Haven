// 重写版（2026-11-17）：把界面和 settings.json 对上。
// 读和写都只碰认识的字段；不认识的（桌宠存的坐标之类）原样留着。

namespace SettingTool;

internal sealed partial class MainForm
{
    private static readonly string[] Shapes = ["小只", "Q 版", "全身"];
    private static readonly string[] ShapeIds = ["small", "chibi", "full"];
    private static readonly string[] TexLabels = ["高清（4096 原图）", "标准（2048）", "流畅（1024）"];
    private static readonly double[] TexValues = [1, 0.5, 0.25];
    private static readonly string[] Personas = ["幼童", "傲娇", "病娇", "人妻", "甜憨", "清泠", "狡黠", "软恬"];

    private static readonly string[] Clothes = ["长裙", "胸口衣服", "脱鞋", "收起尾巴"];
    private static readonly string[] Accessories = ["黑框眼镜", "单片眼镜", "钢盆", "兔牙"];

    /// <summary>把 settings.json 读进各个控件。</summary>
    /// <summary>找出配成同一组键的动作。</summary>
    private List<string> DuplicateHotkeys()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dupes = new List<string>();
        foreach (var (key, title, _, _, _) in HotkeyDefs)
        {
            if (!hotkeyCaps.TryGetValue(key, out var cap)) continue;
            var combo = cap.Text.Trim();
            if (combo.Length == 0 || combo.Contains(' ')) continue;
            if (seen.TryGetValue(combo, out var owner))
                dupes.Add(owner + " 和 " + title + " 都是 " + combo);
            else
                seen[combo] = title;
        }
        return dupes;
    }

    private void LoadIntoPages()
    {
        // 语音
        speechToggle.Checked = store.Flag("Speech", true);
        volumeSlider.Value = (int)Math.Round(store.Number("SpeechVolume", 0.85) * 100);
        volumeText.Text = volumeSlider.Value + "%";

        var persona = store.Text("Persona", "人妻");
        var at = Array.IndexOf(Personas, persona);
        personaField.SelectedIndex = at >= 0 ? at : 0;
        langField.SelectedIndex = store.Text("Language", "zh") switch { "en" => 1, "ja" => 2, _ => 0 };
        personaFollow.Checked = store.Flag("PersonaFollowsHarness", true);

        mutterSlider.Value = (int)Math.Round(store.Number("MutterChance", 0) * 100);
        mutterText.Text = mutterSlider.Value + "%";
        llmUrl.Text = store.Text("LlmBaseUrl", "http://127.0.0.1:11434/v1");
        llmModel.Text = store.Text("LlmModel", "qwen25-3b");
        llmKey.Text = store.Text("LlmApiKey", "");

        // 外观
        var shape = store.Text("Shape", "small");
        var shapeAt = Array.IndexOf(ShapeIds, shape);
        shapeField.SelectedIndex = shapeAt >= 0 ? shapeAt : 0;
        scaleField.Value = (decimal)store.Number("Scale", 1.05);
        var fps = store.Int("Fps", 60).ToString();
        var fpsAt = fpsField.Items.IndexOf(fps);
        fpsField.SelectedIndex = fpsAt >= 0 ? fpsAt : 1;
        var tex = store.Number("TexScale", 0.5);
        var texAt = Array.IndexOf(TexValues, tex);
        texField.SelectedIndex = texAt >= 0 ? texAt : 1;
        bubbleScaleField.Value = (decimal)store.Number("BubbleScale", 1);
        bubbleSecField.Value = store.Int("BubbleSeconds", 0);

        followMouseToggle.Checked = store.Flag("FollowMouse", true);
        hideFullToggle.Checked = store.Flag("HideOnFullscreen", true);
        keepScreenToggle.Checked = store.Flag("KeepOnScreen", false);
        startHiddenToggle.Checked = store.Flag("StartHidden", false);

        // 行为
        idleField.Value = store.Int("IdleSeconds", 120);
        quitTapsField.Value = Math.Clamp(store.Int("QuitTaps", 3), 0, 99);
        coaxSleepToggle.Checked = store.Flag("CoaxSleep", false);
        coaxLunchToggle.Checked = store.Flag("CoaxLunch", false);
        musicDanceToggle.Checked = store.Flag("MusicDance", true);
        musicAppsField.Text = store.Text("MusicApps", "cloudmusic,qqmusic,spotify");
        checkUpdateToggle.Checked = store.Flag("CheckUpdates", false);

        // 模型
        FillModels();
        var chosen = store.Text("ModelPath", "");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var modelAt = modelField.Items.IndexOf(chosen);
            if (modelAt >= 0) modelField.SelectedIndex = modelAt;
        }

        var worn = store.List("Outfit");
        foreach (var toggle in outfitToggles)
            toggle.Checked = worn.Contains(toggle.Text);


        // 其它
        birthdayField.Text = store.Text("Birthday", "");
    }

    /// <summary>把控件写回 settings.json。</summary>
    private void SaveAll()
    {
        store.Set("Speech", speechToggle.Checked);
        store.Set("SpeechVolume", volumeSlider.Value / 100.0);
        store.Set("Persona", Personas[Math.Max(0, personaField.SelectedIndex)]);
        store.Set("Language", langField.SelectedIndex switch { 1 => "en", 2 => "ja", _ => "zh" });
        store.Set("PersonaFollowsHarness", personaFollow.Checked);
        store.Set("MutterChance", mutterSlider.Value / 100.0);
        store.Set("LlmBaseUrl", llmUrl.Text.Trim());
        store.Set("LlmModel", llmModel.Text.Trim());
        store.Set("LlmApiKey", llmKey.Text.Trim());

        store.Set("Shape", ShapeIds[Math.Max(0, shapeField.SelectedIndex)]);
        store.Set("Scale", (double)scaleField.Value);
        if (int.TryParse(fpsField.Text, out var fps)) store.Set("Fps", fps);
        store.Set("TexScale", TexValues[Math.Max(0, texField.SelectedIndex)]);
        store.Set("BubbleScale", (double)bubbleScaleField.Value);
        store.Set("BubbleSeconds", (int)bubbleSecField.Value);

        store.Set("FollowMouse", followMouseToggle.Checked);
        store.Set("HideOnFullscreen", hideFullToggle.Checked);
        store.Set("KeepOnScreen", keepScreenToggle.Checked);
        store.Set("StartHidden", startHiddenToggle.Checked);

        store.Set("IdleSeconds", (int)idleField.Value);
        store.Set("QuitTaps", Math.Clamp((int)quitTapsField.Value, 0, 99));
        store.Set("CoaxSleep", coaxSleepToggle.Checked);
        store.Set("CoaxLunch", coaxLunchToggle.Checked);
        store.Set("MusicDance", musicDanceToggle.Checked);
        store.Set("MusicApps", musicAppsField.Text.Trim());
        store.Set("CheckUpdates", checkUpdateToggle.Checked);

        var model = modelField.Text;
        store.Set("ModelPath", model.StartsWith("（") ? "" : model);

        var worn = outfitToggles.Where(one => one.Checked).Select(one => one.Text).ToList();
        store.Set("Outfit", worn);

        // 本机加（2026-11-18）：两个动作配成同一组键的话，桌宠那边后一个注册不上。
        // 这里先拦一下，别让他去翻日志。
        if (DuplicateHotkeys() is { Count: > 0 } dupes)
        {
            Note("有重复的键：" + string.Join("；", dupes) + "　先把它们改成不一样的再保存");
            return;
        }

        // 快捷键（本机加 2026-11-17）
        foreach (var (key, _, _, fallback, _) in HotkeyDefs)
        {
            if (!hotkeyCaps.TryGetValue(key, out var cap)) continue;
            var value = cap.Text;
            // 捕获时的那句提示别写进配置
            store.Set(key, value.Contains(' ') ? fallback : value);
        }

        store.Set("Birthday", birthdayField.Text.Trim());

        try
        {
            store.Save();
            // 本机改（2026-11-17）：桌宠只在启动时读 settings.json，
            // 光保存不重启是看不到变化的——这里说清楚，免得以为没生效。
            Note(PetHost.Running ? "已保存 · 点「保存并重启」才生效" : "已保存");
        }
        catch (Exception error)
        {
            Note("保存失败：" + error.Message);
        }
    }
}
