using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FishPet;

// 本机加（2026-11-17）：对话框后面的多轮对话。
// 和 Mutter 的区别：Mutter 是单轮（每次现编一句），这里要记住前面说过什么。
internal static class Chat
{
    // 只留最近这些轮，免得越聊越贵、越聊越慢
    private const int KeepTurns = 8;

    private static readonly List<(string Role, string Content)> History = [];

    internal static void Forget() => History.Clear();

    internal static int Turns => History.Count / 2;

    internal static async Task<Mutter.Spoken?> Send(
        HttpClient http, string baseUrl, string key, string model, string userText, string persona)
    {
        var personaHint = Mutter.PersonaHint(persona);
        // 本机改（2026-11-17）：小模型会把「格式：话|动作」里的「话」字照抄出来，
        // 所以改成给两个具体例子，并明确说别写字面。
        var prompt =
            "你是桌面上的大肥鱼，一只蓝头发的鲸鱼娘女仆，说话短、口语、有点懒。" +
            (personaHint.Length > 0 ? personaHint.TrimStart('。') + "。" : "") + "\n\n" +
            "人类说：\"" + userText + "\"\n\n" +
            "你要回一句（20 字以内），后面用竖线接 2~3 个神情动作。\n" +
            "输出示例（照这个样式）：\n" +
            "快去洗个热水澡|八字眉 看过来 叹气\n" +
            "这么晚才回来|嘟嘴 瞟一眼 抿嘴\n\n" +
            "可用的动作：" + Body.MutterVocabulary + "\n" +
            "直接把结果写出来，不要解释，不要写「话」这个字。";

        var messages = new List<object>();
        foreach (var (role, content) in History.TakeLast(KeepTurns * 2))
            messages.Add(new { role, content });
        messages.Add(new { role = "user", content = prompt });

        var payload = new
        {
            model,
            messages,
            max_tokens = 120,
            temperature = 0.9,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions");
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var reply = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        var said = Mutter.Parse(reply);
        if (said == null) return null;

        // 记进历史：用原始的一句，不带动作标记
        History.Add(("user", userText));
        History.Add(("assistant", said.Text));
        while (History.Count > KeepTurns * 2) History.RemoveRange(0, 2);

        return said;
    }
}
