// 重写版（2026-11-17）：语音页 + 外观页。

namespace SettingTool;

internal sealed partial class MainForm
{
    // 字段
    private NeoToggle speechToggle = new("说话时朗读");
    private NeoSlider volumeSlider = new(320);
    private Label volumeText = new();
    private NeoField personaField = ComboField(["幼童", "傲娇", "病娇", "人妻", "甜憨", "清泠", "狡黠", "软恬"], 150);
    // 本机加（2026-11-17）：台词语言
    private NeoField langField = ComboField(["中文", "English", "日本語"], 150);
    private NeoToggle personaFollow = new("跟着 DSH 的人设自动切", 560);
    private NeoSlider mutterSlider = new(320);
    private Label mutterText = new();
    private NeoField llmUrl = TextField("", 400);
    private NeoField llmModel = TextField("", 200);
    private NeoField llmKey = TextField("", 300);

    private FlowLayoutPanel BuildVoicePage()
    {
        var page = NewPage();
        Mount(page, new PageHead("语音", "她说什么、怎么说。语音是 edge-tts 合成的，图省事可以用现成的缓存。", CardWidth));

        // 念出来
        var speaking = new Card("念出来", CardWidth);
        speechToggle.Text = "说话时朗读（关掉就只看字）";
        speaking.AddControl(speechToggle);

        volumeSlider.Minimum = 0;
        volumeSlider.Maximum = 100;
        volumeText.ForeColor = Theme.InkSoft;
        volumeText.Font = Theme.Mono;
        volumeText.AutoSize = true;
        volumeText.BackColor = Theme.Card;
        volumeText.Margin = new Padding(8, 8, 0, 0);
        volumeSlider.ValueChanged += (_, _) => volumeText.Text = volumeSlider.Value + "%";
        speaking.AddControl(Row(Tag("音量", 44), volumeSlider, volumeText));
        Mount(page, speaking);

        // 人格
        var personaCard = new Card("人格", CardWidth);
        personaCard.AddControl(Row(Tag("当前", 44), personaField));
        personaCard.AddControl(Row(Tag("语言", 44), langField));
        // 本机加（2026-11-18）：切语言就像游戏里那样，立刻生效——
        // 台词区马上换语言，设置也当场写进 settings.json，不用再按「保存」。
        if (langField.Inner is ComboBox langCombo)
            langCombo.SelectedIndexChanged += (_, _) =>
            {
                store.Set("Language", langField.SelectedIndex switch { 1 => "en", 2 => "ja", _ => "zh" });
                try { store.Save(); } catch { }
                Note(langField.SelectedIndex switch
                {
                    1 => "已切到 English（桌宠重启后语音也变英文）",
                    2 => "已切到 日本語（桌宠重启后语音也变日文）",
                    _ => "已切回中文",
                });
            };
        personaCard.AddControl(personaFollow);
        personaCard.AddControl(Hint("换人格会换一整套台词和音色。桌宠身上按 Ctrl+Alt+X 也能切。\n语言只影响台词文件：中文读根目录，英文/日文读 lang 下对应的那份；没有对应文件会自动退回中文。"));
        Mount(page, personaCard);

        // 现编嘀咕
        var mutter = new Card("现编嘀咕", CardWidth);
        mutter.AddControl(Hint("要本地 Ollama 或别的模型接口；0% 就完全不现编，只用写好的台词。"));
        mutterSlider.Minimum = 0;
        mutterSlider.Maximum = 100;
        mutterText.ForeColor = Theme.InkSoft;
        mutterText.Font = Theme.Mono;
        mutterText.AutoSize = true;
        mutterText.BackColor = Theme.Card;
        mutterText.Margin = new Padding(8, 8, 0, 0);
        mutterSlider.ValueChanged += (_, _) => mutterText.Text = mutterSlider.Value + "%";
        mutter.AddControl(Row(Tag("频率", 44), mutterSlider, mutterText));
        mutter.AddControl(Row(Tag("地址", 44), llmUrl));
        mutter.AddControl(Row(Tag("模型", 44), llmModel));
        mutter.AddControl(Row(Tag("密钥", 44), llmKey));
        Mount(page, mutter);

        return page;
    }

    // ───────────────────────────────── 外观
    private NeoField shapeField = ComboField(["小只", "Q 版", "全身"], 150);
    private NeoField fpsField = ComboField(["30", "60", "90", "120"], 110);
    private NeoField texField = ComboField(["高清（4096 原图）", "标准（2048）", "流畅（1024）"], 220);
    private NeoField scaleField = NumField(1.05m, 0.5m, 2m, 0.05m, 110);
    private NeoField bubbleScaleField = NumField(1m, 0.5m, 2m, 0.05m, 110);
    private NeoField bubbleSecField = NumField(0m, -1m, 180m, 1m, 110);
    private NeoToggle followMouseToggle = new("视线跟着鼠标", 560);
    private NeoToggle hideFullToggle = new("别的程序全屏时躲起来", 560);
    private NeoToggle keepScreenToggle = new("拖到屏幕边会弹回来", 560);
    private NeoToggle startHiddenToggle = new("启动时先收着（等我叫）", 560);

    private FlowLayoutPanel BuildLookPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("外观", "大小、清晰度、气泡、视线。改完要重启桌宠才生效。", CardWidth));

        var size = new Card("体型与大小", CardWidth);
        size.AddControl(Row(Tag("体型", 60), shapeField));
        size.AddControl(Row(Tag("大小", 60), scaleField, Tag("帧率", 44), fpsField));
        size.AddControl(Row(Tag("清晰度", 60), texField));
        size.AddControl(Hint("清晰度越高越吃显存；改档位立绘会重新加载几秒。"));
        Mount(page, size);

        var bubble = new Card("气泡", CardWidth);
        bubble.AddControl(Row(Tag("大小", 60), bubbleScaleField, Tag("停留", 44), bubbleSecField));
        bubble.AddControl(Hint("停留秒数：-1 一直留着到下一句；0 按字数自动（5–20 秒）。"));
        Mount(page, bubble);

        var sight = new Card("视线与位置", CardWidth);
        sight.AddControl(followMouseToggle);
        sight.AddControl(hideFullToggle);
        sight.AddControl(keepScreenToggle);
        sight.AddControl(startHiddenToggle);
        Mount(page, sight);

        return page;
    }
}
