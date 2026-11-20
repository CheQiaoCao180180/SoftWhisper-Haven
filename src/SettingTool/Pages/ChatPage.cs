// 重写版（2026-11-17）：对话页。
// 在设置工具里就能跟她聊：左边是聊天记录，下面输入框。
// 用的是同一套 LLM（settings.json 里的 LlmBaseUrl / LlmApiKey / LlmModel），
// 聊完会让桌宠那边的气泡也跟着说——走桌宠的 say 接口。

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SettingTool;

internal sealed partial class MainForm
{
    private FlowLayoutPanel? chatLog;
    private NeoField? chatInput;
    private Panel? chatScroller;

    private FlowLayoutPanel BuildChatPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("对话", "在这儿打字跟她聊。她那边也会用气泡说出来。", CardWidth));

        // ── 聊天记录
        var log = new Card("聊天记录", CardWidth);
        var clear = new NeoButton("清空") { Width = 78 };
        clear.Click += (_, _) =>
        {
            chatHistory.Clear();
            chatLog?.Controls.Clear();
            AddBubble("想说什么就说吧。", fromHer: true);
        };
        log.AddControl(Row(clear));

        chatScroller = new Panel
        {
            Width = CardWidth - 44,
            Tag = "wide", Height = 250,
            BackColor = Theme.Card,
            AutoScroll = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        chatLog = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Card,
            Width = CardWidth - 60,
            Tag = "wide", MaximumSize = new Size(CardWidth - 60, 0),
            MinimumSize = new Size(CardWidth - 60, 0),
        };
        chatScroller.Controls.Add(chatLog);
        log.AddControl(chatScroller);
        Mount(page, log);

        // ── 输入
        var input = new Card("说点什么", CardWidth);
        chatInput = new NeoField(new TextBox { BorderStyle = BorderStyle.None }, 0);
        chatInput.Width = CardWidth - 190;
        chatInput.Height = 40;
        var inputBox = chatInput.Inner as TextBox;
        if (inputBox != null) inputBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control)
            {
                e.SuppressKeyPress = true;
                SendChat();
            }
        };

        var send = new NeoButton("发送") { Width = 92, Height = 40 };
        send.Click += (_, _) => SendChat();
        input.AddControl(Row(chatInput, send));
        input.AddControl(Hint("回车发送。她说的话会同时出现在桌面的气泡里。"));
        Mount(page, input);

        AddBubble("想说什么就说吧。", fromHer: true);
        return page;
    }

    private readonly List<(string Role, string Text)> chatHistory = [];

    private void AddBubble(string text, bool fromHer)
    {
        if (chatLog == null) return;

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Card,
            Width = CardWidth - 70,
            Tag = "wide", MaximumSize = new Size(CardWidth - 70, 0),
            Padding = new Padding(fromHer ? 0 : 90, 3, fromHer ? 90 : 0, 3),
        };

        var bubble = new ChatBubble(text, fromHer);
        row.Controls.Add(bubble);
        chatLog.Controls.Add(row);

        // 滚到底
        if (chatScroller != null)
            chatScroller.ScrollControlIntoView(row);
    }

    private async void SendChat()
    {
        if (chatInput == null) return;
        var text = chatInput.Text.Trim();
        if (text.Length == 0) return;

        chatInput.Text = "";
        AddBubble(text, fromHer: false);
        chatHistory.Add(("user", text));

        var (baseUrl, key, model) = LlmConfig();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            AddBubble("……还没接上大模型。去「其它」页填一下接口地址和模型名。", fromHer: true);
            return;
        }

        try
        {
            var reply = await AskLlm(baseUrl, key, model, text);
            if (string.IsNullOrWhiteSpace(reply))
            {
                AddBubble("……脑子卡住了，再说一次？", fromHer: true);
                return;
            }

            // 「话|动作」拆开：气泡里只显示话，动作交给桌宠
            var parts = reply.Split('|');
            var words = parts[0].Trim();
            AddBubble(words, fromHer: true);
            chatHistory.Add(("assistant", words));

            // 让桌面那边也说一遍（带动作）
            TellPet(words, parts.Length > 1 ? parts[1].Trim() : null);
        }
        catch (Exception error)
        {
            AddBubble("出错了：" + error.Message, fromHer: true);
        }
    }

    private (string Url, string Key, string Model) LlmConfig()
    {
        return (
            store.Text("LlmBaseUrl", "http://127.0.0.1:11434/v1"),
            store.Text("LlmApiKey", ""),
            store.Text("LlmModel", "qwen25-3b"));
    }

    private async Task<string> AskLlm(string baseUrl, string key, string model, string userText)
    {
        var person = store.Text("Persona", "人妻");
        var hint = person switch
        {
            "人妻" => "人设是「人妻」：温柔笃定，会念叨吃饭穿衣早点睡。",
            "傲娇" => "人设是「傲娇」：嘴上不肯服软，关心人绕着弯说。",
            "童真" => "人设是「童真」：像小孩，好奇，情绪直白。",
            "病娇" => "人设是「病娇」：声音甜甜的，眼神一直跟着对方。",
            _ => "",
        };

        var prompt =
            "你是桌面上的大肥鱼，一只蓝头发的鲸鱼娘女仆，说话短、口语、有点懒。" + hint + "\n\n" +
            "人类说：\"" + userText + "\"\n\n" +
            "你要回一句（20 字以内），后面用竖线接 2~3 个神情动作。\n" +
            "输出示例（照这个样式）：\n" +
            "快去洗个热水澡|八字眉 看过来 叹气\n" +
            "这么晚才回来|嘟嘴 瞟一眼 抿嘴\n\n" +
            "可用的动作：歪头 转头 低头 抬头 正视 前倾 后仰 站直 看过来 看左边 看右边 " +
            "睁大 半睁 闭眼 眯眼笑 笑眼 斜眼 挑眉 皱眉 八字眉 微笑 大笑 坏笑 瘪嘴 " +
            "嘟嘴 鼓腮 抿嘴 舌 脸红 黑脸 冷汗 爱心眼 抚胸 竖手指 放下手 点头 摇头 眨眼 瞟一眼 叹气 缩一下\n" +
            "直接把结果写出来，不要解释，不要写「话」这个字。";

        var messages = new List<object>();
        foreach (var (role, content) in chatHistory.TakeLast(12))
            messages.Add(new { role, content });
        messages.Add(new { role = "user", content = prompt });

        var payload = new
        {
            model,
            messages,
            max_tokens = 120,
            temperature = 0.9,
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions");
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Add("Authorization", "Bearer " + key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return "";
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    /// <summary>让桌宠那边也把这句话说出来（走她的 say 接口）。</summary>
    private void TellPet(string words, string? cue)
    {
        try
        {
            var apiFile = Path.Combine(Program.Root, "api.json");
            if (!File.Exists(apiFile)) return;
            var api = JsonNode.Parse(File.ReadAllText(apiFile));
            var url = api?["url"]?.GetValue<string>();
            var token = api?["token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token)) return;

            var say = string.IsNullOrEmpty(cue) ? words : "〔" + cue + "〕" + words;
            var payload = new JsonObject { ["text"] = say };
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var request = new HttpRequestMessage(HttpMethod.Post, url + "/call/say")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Authorization", "Bearer " + token);
            using var _ = client.Send(request);
        }
        catch { }
    }
}

// ───────────────────────────────────── 气泡
internal sealed class ChatBubble : Control
{
    private readonly string text;
    private readonly bool fromHer;

    internal ChatBubble(string content, bool her)
    {
        text = content;
        fromHer = her;
        BackColor = Theme.Card;
        var width = Math.Min(380, TextRenderer.MeasureText(content, Theme.Body).Width + 28);
        Width = Math.Max(60, width);
        Height = TextRenderer.MeasureText(content, Theme.Body, new Size(Width - 28, 0), TextFormatFlags.WordBreak).Height + 20;
        Margin = new Padding(0, 3, 0, 3);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(rect, 13);
        using (var fill = new SolidBrush(fromHer ? Theme.BluePale : Theme.Pink))
            g.FillPath(fill, path);

        var flags = TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(14, 10, Width - 28, Height - 20),
            fromHer ? Theme.Ink : Color.White, flags);
    }
}
