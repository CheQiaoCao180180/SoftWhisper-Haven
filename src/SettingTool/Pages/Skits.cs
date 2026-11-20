// 重写版（2026-11-17）：小剧场页 + 自己编一段。
// 桌宠盯着插件根目录的所有 *.txt，改完 1 秒内自动重载，所以存进去就能演，不用重启。

using System.Text;
using System.Text.Json.Nodes;

namespace SettingTool;

internal sealed partial class MainForm
{
    private FlowLayoutPanel? skitGrid;
    private NeoField? skitName;
    private TextBox? skitBody;

    private FlowLayoutPanel BuildSkitPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("小剧场", "她闲着的时候会自己挑一段演。想马上看哪段，点一下就行。", CardWidth));

        // ── 现成的
        var card = new Card("全部小剧场", CardWidth);
        var refresh = new NeoButton("重新扫描");
        refresh.Click += (_, _) => { FillSkits(); Note("已重新扫描小剧场"); };
        card.AddControl(Row(refresh));
        card.AddControl(Hint("带 @ 的是有条件才会演的（生日、深夜、亲密等级够高之类）；手动点不受限制。"));

        skitGrid = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Theme.Card,
            Width = CardWidth - 44,
            Tag = "wide", MaximumSize = new Size(CardWidth - 44, 0),
            Margin = new Padding(0, 6, 0, 0),
        };
        card.AddControl(skitGrid);
        Mount(page, card);

        // ── 自己编
        var write = new Card("自己编一段", CardWidth);
        write.AddControl(Hint("每行一句：「台词|动作|停留秒数」。台词可以空（只做动作），秒数不写默认 3.5。"));

        skitName = new NeoField(new TextBox { BorderStyle = BorderStyle.None }, 260);
        write.AddControl(Row(Tag("名字", 50), skitName));

        skitBody = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Width = CardWidth - 60,
            Tag = "wide", Height = 150,
            Font = Theme.Mono,
            BackColor = Color.White,
            ForeColor = Theme.Ink,
            Margin = new Padding(0, 6, 0, 6),
        };
        write.AddControl(skitBody);
        write.AddControl(Hint("例：\n快去洗澡|八字眉 看过来 叹气|3\n这么晚才回来|嘟嘴 瞟一眼 抿嘴|3.5"));

        var save = new NeoButton("保存并试演") { Width = 130 };
        save.Click += (_, _) => SaveSkit();
        var preview = new NeoButton("只看格式") { Width = 100 };
        preview.Click += (_, _) => CheckSkit();
        write.AddControl(Row(save, preview));
        Mount(page, write);

        FillSkits();
        return page;
    }

    private void FillSkits()
    {
        if (skitGrid == null) return;
        skitGrid.Controls.Clear();

        foreach (var (name, condition) in ReadSkits())
        {
            var button = new NeoButton(name, ButtonKind.Outline) { Height = 34, Margin = new Padding(0, 0, 8, 8) };
            if (!string.IsNullOrEmpty(condition))
            {
                button.Kind = ButtonKind.Outline;   // 用户要全白底
                button.Tint = Theme.InkSoft;
            }
            var target = name;
            button.Click += (_, _) => PlaySkit(target);
            skitGrid.Controls.Add(button);
        }

        if (skitGrid.Controls.Count == 0)
        {
            // 这里别放 HintBlock：它是 AutoSize 的容器，套在 AutoSize 的网格里会互相追着重排
            skitGrid.Controls.Add(new Label
            {
                Text = "skits.txt 里没读到东西",
                AutoSize = true,
                ForeColor = Theme.InkSoft,
                Font = Theme.Small,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 8),
            });
        }
    }

    /// <summary>从 skits.txt 里读「# 名字 @条件」。</summary>
    private static List<(string Name, string Condition)> ReadSkits()
    {
        var found = new List<(string, string)>();
        var file = Path.Combine(Program.Root, "skits.txt");
        if (!File.Exists(file)) return found;

        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var text = line.Trim();
                if (!text.StartsWith("#")) continue;
                text = text[1..].Trim();
                if (text.Length == 0) continue;

                var at = text.IndexOf('@');
                if (at > 0) found.Add((text[..at].Trim(), text[(at + 1)..].Trim()));
                else found.Add((text, ""));
            }
        }
        catch { }

        return found;
    }

    private (string Name, List<string> Lines) ReadSkitInput()
    {
        var name = skitName?.Text.Trim() ?? "";
        var lines = new List<string>();
        foreach (var raw in (skitBody?.Text ?? "").Split('\n'))
        {
            var text = raw.Trim();
            if (text.Length > 0) lines.Add(text);
        }
        return (name, lines);
    }

    private void CheckSkit()
    {
        var (name, lines) = ReadSkitInput();
        if (name.Length == 0) { Note("先起个名字"); return; }
        if (lines.Count == 0) { Note("写点什么再试"); return; }

        var problems = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var parts = lines[i].Split('|');
            if (parts.Length < 2) problems.Add($"第 {i + 1} 行少了竖线");
            else if (parts[1].Trim().Length == 0) problems.Add($"第 {i + 1} 行没有动作");
        }

        Note(problems.Count == 0
            ? $"「{name}」{lines.Count} 句，格式没问题"
            : "有问题：" + string.Join("；", problems.Take(3)));
    }

    private void SaveSkit()
    {
        var (name, lines) = ReadSkitInput();
        if (name.Length == 0) { Note("先起个名字"); return; }
        if (lines.Count == 0) { Note("写点什么再保存"); return; }

        try
        {
            var file = Path.Combine(Program.Root, "skits.txt");
            var text = File.Exists(file) ? File.ReadAllText(file) : "";

            // 同名的先删掉旧的（整段，从 # 名字 到下一个 # 之前）
            if (text.Contains("# " + name))
            {
                var kept = new StringBuilder();
                var skipping = false;
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("# "))
                    {
                        skipping = trimmed[2..].Trim().Split(' ')[0] == name;
                    }
                    if (!skipping) kept.Append(line).Append('\n');
                }
                text = kept.ToString();
                Note("同名的小剧场已替换");
            }

            var block = new StringBuilder();
            block.Append('\n').Append("# ").Append(name).Append('\n');
            foreach (var line in lines) block.Append(line).Append('\n');

            File.AppendAllText(file, block.ToString(), new UTF8Encoding(false));

            Note("已保存「" + name + "」，一秒后她就能演");
            FillSkits();
            PlaySkit(name);
        }
        catch (Exception error)
        {
            Note("保存失败：" + error.Message);
        }
    }

    /// <summary>通过桌宠的 HTTP 接口演一段。</summary>
    private void PlaySkit(string name)
    {
        try
        {
            var apiFile = Path.Combine(Program.Root, "api.json");
            if (!File.Exists(apiFile))
            {
                Note("桌宠没开，读不到 api.json");
                return;
            }

            var api = JsonNode.Parse(File.ReadAllText(apiFile));
            var url = api?["url"]?.GetValue<string>();
            var token = api?["token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
            {
                Note("api.json 里没有地址或 token");
                return;
            }

            var payload = new JsonObject { ["stage"] = 6, ["text"] = name };
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var request = new HttpRequestMessage(HttpMethod.Post, url + "/call/coax")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Authorization", "Bearer " + token);
            using var response = client.Send(request);
            Note(response.IsSuccessStatusCode ? "正在演：" + name : "没演成：" + (int)response.StatusCode);
        }
        catch (Exception error)
        {
            Note("播放失败：" + error.Message);
        }
    }
}
