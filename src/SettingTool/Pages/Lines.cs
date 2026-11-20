// 本机加（2026-11-17）：台词页。
// 按「人格 × 语言 × 情绪」浏览，直接改，存回文件；也能看到哪几句还没语音。
// 桌宠盯着 *.txt 热重载，存完那边一秒内就换新。

using System.Text;

namespace SettingTool;

internal sealed partial class MainForm
{
    private static readonly string[] LangNames = ["中文", "English", "日本語"];
    // 本机改（2026-11-17）：台词按语言分目录了，lines\zh|en|ja
    private static readonly string[] LangFolders = ["zh", "en", "ja"];

    private static readonly string[] MoodNames =
    [
        "摸头", "满足", "害羞", "娇羞", "爱心", "开心",
        "委屈", "戳脸", "亲吻", "护裙", "被踩", "跺脚", "叹气",
    ];

    private ComboBox? linePersona;
    private ComboBox? lineMood;
    private TextBox? lineEditor;
    private Label? lineInfo;

    /// <summary>本机改（2026-11-18）：不再单独占一页，改成往语音页里加卡片。</summary>
    private void BuildLineCards(FlowLayoutPanel page)
    {
        var pick = new Card("挑一组台词", CardWidth);
        linePersona = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Font = Theme.Body };
        linePersona.Items.AddRange(["幼童", "傲娇", "病娇", "人妻", "甜憨", "清泠", "狡黠", "软恬"]);
        linePersona.SelectedIndex = 0;

        lineMood = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Font = Theme.Body };
        lineMood.Items.AddRange(MoodNames);
        lineMood.SelectedIndex = 0;

        pick.AddControl(Row(Tag("人格", 44), linePersona, Tag("情绪", 44), lineMood));
        pick.AddControl(Hint("看哪个语言的台词，跟着上面「人格」卡片里那个语言走。"));

        var load = new NeoButton("读出来") { Width = 96 };
        load.Click += (_, _) => LoadLines();
        var save = new NeoButton("保存这组") { Width = 110 };
        save.Click += (_, _) => SaveLines();
        pick.AddControl(Row(load, save));
        Mount(page, pick);

        var edit = new Card("台词（一行一句，空行会被忽略）", CardWidth);
        lineEditor = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Width = CardWidth - 60,
            Tag = "wide", Height = 220,
            Font = Theme.Body,
            BackColor = Color.White,
            ForeColor = Theme.Ink,
            Margin = new Padding(0, 4, 0, 4),
        };
        edit.AddControl(lineEditor);

        lineInfo = new Label
        {
            Text = "还没读",
            AutoSize = false,
            Width = CardWidth - 60,
            Tag = "wide", Height = 26,
            Font = Theme.Small,
            ForeColor = Theme.InkSoft,
            BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 0, 0),
        };
        edit.AddControl(lineInfo);
        edit.AddControl(Hint("格式是「人格.情绪|台词」，这里只显示台词本身；改完按「保存这组」会连着前缀一起写回去。"));
        Mount(page, edit);

        LoadLines();
    }

    private string CurrentLangFile()
    {
        // 本机改（2026-11-18）：语言跟上面那个「语言」下拉走，不再单独设一个。
        var at = Math.Clamp(langField.SelectedIndex, 0, LangFolders.Length - 1);
        return Path.Combine(Program.Root, "lines", LangFolders[at], "touch.txt");
    }

    private string CurrentPrefix()
    {
        var who = linePersona?.SelectedItem?.ToString() ?? "幼童";
        var mood = lineMood?.SelectedItem?.ToString() ?? "摸头";
        return who + "." + mood;
    }

    private void LoadLines()
    {
        if (lineEditor == null) return;
        var file = CurrentLangFile();
        var prefix = CurrentPrefix();

        if (!File.Exists(file))
        {
            lineEditor.Text = "";
            if (lineInfo != null) lineInfo.Text = "文件不存在：" + file;
            return;
        }

        var found = new List<string>();
        var all = 0;
        foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            var text = raw.Trim();
            if (text.Length == 0 || text.StartsWith("//")) continue;
            all++;
            var bar = text.IndexOf('|');
            if (bar <= 0) continue;
            if (!text[..bar].Trim().Equals(prefix, StringComparison.Ordinal)) continue;
            found.Add(text[(bar + 1)..].Trim());
        }

        lineEditor.Text = string.Join(Environment.NewLine, found);
        if (lineInfo != null)
        {
            var name = Path.GetFileName(file);
            lineInfo.Text = $"「{prefix}」{found.Count} 句（文件共 {all} 行，{name}）";
        }
    }

    private void SaveLines()
    {
        if (lineEditor == null) return;
        var file = CurrentLangFile();
        var prefix = CurrentPrefix();

        try
        {
            var existing = File.Exists(file) ? File.ReadAllLines(file, Encoding.UTF8).ToList() : [];
            var kept = new List<string>();
            foreach (var raw in existing)
            {
                var text = raw.Trim();
                var bar = text.IndexOf('|');
                if (text.Length > 0 && !text.StartsWith("//") && bar > 0
                    && text[..bar].Trim().Equals(prefix, StringComparison.Ordinal))
                    continue;     // 这一组的旧句子全删掉，下面重写
                kept.Add(raw);
            }

            var fresh = new List<string>();
            foreach (var raw in lineEditor.Text.Split('\n'))
            {
                var text = raw.Trim();
                if (text.Length == 0) continue;
                fresh.Add(prefix + "|" + text);
            }

            File.WriteAllLines(file, [.. kept, .. fresh], new UTF8Encoding(false));
            Note($"已保存「{prefix}」{fresh.Count} 句");
            LoadLines();
        }
        catch (Exception error)
        {
            Note("保存失败：" + error.Message);
        }
    }
}
