using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace FishPet;

// 本机加（2026-11-15）：说话时顺带念出来。
// 1) 先找 audio\voices\<hash>.mp3 —— 用 edge-tts（晓伊，神经网络音色）提前合成好，音质接近真人、零延迟；
// 2) 没有就现场调 edge-tts 合成一条，存下来下次直接用；
// 3) 连现场合成也失败（没网/环境坏了），退回系统语音，至少有声音。
internal sealed class Voice : IDisposable
{
    private readonly SpeechSynthesizer synth = new();
    private readonly MediaPlayer player = new() { Volume = .85 };
    // 本机加（2026-11-16）：mp3 走 NAudio，好把实时振幅交给口型
    private readonly TalkingPlayer talker = new() { Volume = .85 };

    // 她这会儿说话有多大声（0–1）。PetWindow 的口型读这个。
    internal float Level => talker.Playing ? talker.Level : (player.PlaybackSession is { } s && s.PlaybackState == MediaPlaybackState.Playing ? FallbackLevel() : 0f);

    // 本机加（2026-11-16）：音频还在不在放。句与句之间要等它真的说完，不然会抢话。
    internal bool Playing => talker.Playing || (player.PlaybackSession is { } s && s.PlaybackState == MediaPlaybackState.Playing);

    // 系统语音没有振幅可读，退回一个轻一点的正弦（至少不像以前那样整段满幅）
    private static float FallbackLevel() =>
        (float)(.12 + .16 * Math.Abs(Math.Sin(Environment.TickCount64 / 95.0)));
    private bool ready = true;
    private int ticket;

    internal Voice()
    {
        try
        {
            var voices = SpeechSynthesizer.AllVoices;
            var pick = voices.FirstOrDefault(v => v.DisplayName.Contains("Yaoyao"))
                       ?? voices.FirstOrDefault(v => v.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                       ?? voices.FirstOrDefault();
            if (pick != null) synth.Voice = pick;
        }
        catch { ready = false; }
    }

    internal static string CacheDir => Path.Combine(Places.Root, "audio", "voices");
    internal double Volume
    {
        set
        {
            try { player.Volume = Math.Clamp(value, 0, 1); } catch { }
            try { talker.Volume = Math.Clamp(value, 0, 1); } catch { }
        }
    }
    internal static string LiveVoice = "zh-CN-XiaoyiNeural";

    // 本机加（2026-11-16）：四个人格四个音色，切换人格声音真的会变
    // 本机加（2026-11-16）：只用两个大陆女声（台湾腔念大陆词很怪，用户说听着别扭），
    // 四个人格靠「语速 + 音高」拉开，听感就够了。
    // 本机加（2026-11-16）：调得太狠会变形（拖沓、发飘），所以只动很小的幅度，接近原生。
    // 人格的区别主要靠「换音色」+ 一点点轻重，不再硬拉语速。
    // 本机加（2026-11-18）：现在说哪国话。LoadTouchFile 会根据 settings.Language 设好。
    // 英文/日文没法靠音色区分八个人设（edge-tts 各语言就一两个女声），
    // 所以用音高 + 语速拉开，和中文那套的思路一样。
    internal static string Language = "zh";

    // 本机改（2026-11-17）：音色从 Personas 表里读，加人设只改那一处
    internal static (string Voice, string Rate, string Pitch, string Volume) VoiceFor(string persona)
    {
        var one = Personas.Of(persona);
        var (voice, rate, pitch) = Language is "en" or "ja" ? Localized(one, Language) : (one.Voice, one.Rate, one.Pitch);
        return (voice, rate, pitch, one.Volume);
    }

    // 英文：Ana 是童声，Aria 亮，Jenny 温和；日文只有 Nanami 一个女声，全靠调。
    private static (string Voice, string Rate, string Pitch) Localized(Personas.Persona one, string lang) =>
        (lang, one.Name) switch
        {
            ("en", "幼童") => ("en-US-AnaNeural", "-10%", "+15Hz"),
            ("en", "傲娇") => ("en-US-AriaNeural", "+8%", "+5Hz"),
            ("en", "病娇") => ("en-US-AriaNeural", "-12%", "-8Hz"),
            ("en", "人妻") => ("en-US-JennyNeural", "-3%", "+0Hz"),
            ("en", "甜憨") => ("en-US-AnaNeural", "-12%", "+5Hz"),
            ("en", "清泠") => ("en-US-JennyNeural", "-15%", "+2Hz"),
            ("en", "狡黠") => ("en-US-AriaNeural", "+5%", "+8Hz"),
            ("en", "软恬") => ("en-US-JennyNeural", "-16%", "-4Hz"),
            ("en", _) => ("en-US-JennyNeural", "+0%", "+0Hz"),

            ("ja", "幼童") => ("ja-JP-NanamiNeural", "-10%", "+20Hz"),
            ("ja", "傲娇") => ("ja-JP-NanamiNeural", "+8%", "+5Hz"),
            ("ja", "病娇") => ("ja-JP-NanamiNeural", "-12%", "-8Hz"),
            ("ja", "人妻") => ("ja-JP-NanamiNeural", "-3%", "+0Hz"),
            ("ja", "甜憨") => ("ja-JP-NanamiNeural", "-10%", "+12Hz"),
            ("ja", "清泠") => ("ja-JP-NanamiNeural", "-15%", "+2Hz"),
            ("ja", "狡黠") => ("ja-JP-NanamiNeural", "+5%", "+8Hz"),
            ("ja", "软恬") => ("ja-JP-NanamiNeural", "-16%", "-5Hz"),
            ("ja", _) => ("ja-JP-NanamiNeural", "+0%", "+0Hz"),

            _ => (one.Voice, one.Rate, one.Pitch),
        };

    internal static (string Voice, string Rate, string Pitch) VoiceOnly(string persona)
    {
        var (voice, rate, pitch, _) = VoiceFor(persona);
        return (voice, rate, pitch);
    }

    internal static string VoiceName(string persona) => VoiceFor(persona).Voice;

    // 和 audio\gen_voices_all.py 的 clean() 保持一致
    private static string Normalize(string text) =>
        (text ?? "").Replace("～", "~").Replace("♪", "").Replace("♬", "").Trim();

    private static string Key(string text) => Key(text, "");

    private static string Key(string text, string voice)
    {
        // 带上音色：同一句话在不同人格下是不同的一份 mp3（本机加，2026-11-16）
        var seed = voice.Length == 0 ? text : voice + "|" + text;
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static (string Rate, string Pitch) Tone(string persona)
    {
        var one = Personas.Of(persona);
        return (one.Speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), one.Pitch);
    }

    internal void Speak(string text) => Speak(text, "");

    internal async void Speak(string text, string persona)
    {
        var clean = Normalize(text);
        if (clean.Length == 0) return;
        int mine = ++ticket;
        var (voice, rate, pitch, volume) = VoiceFor(persona);
        Places.Log("要念：" + (clean.Length > 20 ? clean[..20] : clean) + " " + voice + " " + rate + "/" + pitch + "（" + persona + "）");

        // 1) 现成的 mp3（按人格的音色找）
        try
        {
            var file = Path.Combine(CacheDir, Key(clean, voice + rate + pitch + volume) + ".mp3");
            if (File.Exists(file) && new FileInfo(file).Length > 512)
            {
                Stop();
                Play(file);
                return;
            }
        }
        catch { }

        // 2) 现场合成（要联网，一两秒）
        var made = await Live(clean, mine, voice, rate, pitch, volume);
        if (made != null && mine == ticket) { Stop(); Play(made); return; }
        if (mine != ticket) return; // 被后来的话顶掉了

        // 3) 兜底：系统语音
        Places.Log("现场合成没成，退回系统语音（ready=" + ready + "）");
        if (!ready) return;
        Stop();
        await Say(Ssml(clean, persona));
    }

    private void Play(string file)
    {
        try
        {
            player.Pause();
            player.Source = null;
            talker.Play(file);
            Places.Log("念（缓存）：" + Path.GetFileName(file) + " 音量 " + talker.Volume.ToString("0.00"));
        }
        catch (Exception error) { Places.Log("播放失败：" + error.Message); }
    }

    // 现场合成：跑一次 audio\tts-venv 里的 python，成功返回 mp3 路径
    private static async Task<string?> Live(string text, int mine, string voice, string rate, string pitch, string volume)
    {
        try
        {
            var python = Path.Combine(Places.Root, "audio", "tts-venv", "Scripts", "python.exe");
            if (!File.Exists(python)) return null;
            Directory.CreateDirectory(CacheDir);
            var file = Path.Combine(CacheDir, Key(text, voice + rate + pitch + volume) + ".mp3");
            var safe = (text.Length > 300 ? text[..300] : text).Replace("\"", "'").Replace("\n", " ");
            var args = "-m edge_tts --voice " + voice + " --rate=" + rate + " --pitch=" + pitch +
                       " --volume=" + volume +
                       " --text \"" + safe + "\" --write-media \"" + file + "\"";
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(python, args)
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                },
            };
            process.Start();
            var done = await Task.Run(() => process.WaitForExit(12000));
            if (!done) { try { process.Kill(true); } catch { } Places.Log("现场合成超时"); return null; }
            if (process.ExitCode != 0)
            {
                var err = "";
                try { err = process.StandardError.ReadToEnd(); } catch { }
                Places.Log("现场合成失败 exit=" + process.ExitCode + " " + (err.Length > 160 ? err[..160] : err).Replace("`r", " ").Replace("`n", " "));
                return null;
            }
            return File.Exists(file) && new FileInfo(file).Length > 512 ? file : null;
        }
        catch { return null; }
    }

    private static string Ssml(string text, string persona)
    {
        var (rate, pitch) = Tone(persona);
        var safe = System.Security.SecurityElement.Escape(text) ?? "";
        return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-CN'>" +
               "<prosody rate='" + rate + "' pitch='" + pitch + "'>" + safe + "</prosody></speak>";
    }

    private async Task Say(string ssml)
    {
        try
        {
            var stream = await synth.SynthesizeSsmlToStreamAsync(ssml);
            player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            player.Play();
        }
        catch
        {
            try
            {
                var plain = System.Text.RegularExpressions.Regex.Replace(ssml, "<[^>]+>", "");
                var stream = await synth.SynthesizeTextToStreamAsync(plain);
                player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                player.Play();
            }
            catch { }
        }
    }

    internal void Stop()
    {
        try { player.Pause(); player.Source = null; } catch { }
        talker.Stop();
    }

    public void Dispose()
    {
        try { talker.Dispose(); } catch { }
        try { player.Dispose(); } catch { }
        try { synth.Dispose(); } catch { }
    }

    internal static int CachedCount()
    {
        try { return Directory.Exists(CacheDir) ? Directory.GetFiles(CacheDir, "*.mp3").Length : 0; }
        catch { return 0; }
    }
}
