// 重写版（2026-11-17）：录 GIF —— 不再抓屏幕，改成让渲染器直接出帧。
//
// 为什么改：试过三种抓屏方案都不行——
//   只置顶 → 设置工具自己挡着；
//   挪左上角 → 桌面图标进画面；
//   最小化设置工具 → DSH 的窗口是全屏的，照样挡。
// 抓屏这条路根本绕不开窗口遮挡，所以改成：渲染器按脚本吐 PNG 帧，再拿 ffmpeg 合成 GIF。
// 代价是没有气泡（气泡是 WPF 画的，渲染器不知道），换来的是干净、可预期的画面。

using System.Diagnostics;

namespace SettingTool;

internal sealed partial class MainForm
{
    private static readonly string[] FfmpegCandidates =
    [
        @"E:\Live2D Cubism 4.1\tools\ffmpeg\ffmpeg.exe",
        @"E:\Krita (x64)\bin\ffmpeg.exe",
        "ffmpeg.exe",
    ];

    private FlowLayoutPanel BuildRecordPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("录 GIF", "让渲染器一帧一帧出图，再合成功图。不受窗口遮挡影响。", CardWidth));

        var card = new Card("录一段", CardWidth);
        card.AddControl(Hint("录的是她本人的画面（待机动作）。气泡不在里面——那是另一层画的。"));
        card.AddControl(Hint("每帧 0.1 秒；先出 PNG 再合成，中间产物会被自动清掉。"));

        var shortOne = new NeoButton("录 2 秒") { Width = 96 };
        shortOne.Click += (_, _) => RecordGif(2);

        var normal = new NeoButton("录 4 秒") { Width = 96 };
        normal.Click += (_, _) => RecordGif(4);

        var longer = new NeoButton("录 8 秒") { Width = 96 };
        longer.Click += (_, _) => RecordGif(8);

        var openFolder = new NeoButton("打开 gifs 文件夹") { Width = 140 };
        openFolder.Click += (_, _) => OpenFolder(Path.Combine(Program.Root, "gifs"));

        card.AddControl(Row(shortOne, normal, longer));
        card.AddControl(Row(openFolder));
        Mount(page, card);

        var tips = new Card("说明", CardWidth);
        tips.AddControl(Hint("走的命令：\nl2d-render.exe --model … --script …\n脚本里每 0.1 秒存一张 png，最后 ffmpeg 把这几张合成 GIF。\n\n她正在说话时的动作、表情不会录进去——渲染器只知道模型参数，不知道站在桌面上的那一刻发生了什么。"));
        Mount(page, tips);

        return page;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    private async void RecordGif(int seconds)
    {
        var renderer = Path.Combine(Program.Root, "renderer", "build", "Release", "l2d-render.exe");
        if (!File.Exists(renderer))
        {
            Note("找不到渲染器（renderer\\build\\Release\\l2d-render.exe）");
            return;
        }

        var ffmpeg = FfmpegCandidates.FirstOrDefault(File.Exists);
        if (ffmpeg == null)
        {
            Note("没找到 ffmpeg，合不成 GIF");
            return;
        }

        var model = store.Text("ModelPath", "");
        if (string.IsNullOrWhiteSpace(model))
            model = Path.Combine("DeepSeek", "大肥鱼.model3.json");
        var modelPath = Path.IsPathRooted(model) ? model : Path.Combine(Program.Root, "model", model);
        if (!File.Exists(modelPath))
        {
            Note("模型文件找不到：" + modelPath);
            return;
        }

        var frames = Path.Combine(Path.GetTempPath(), "fishpet-frames-" + DateTime.Now.Ticks);
        var gifs = Path.Combine(Program.Root, "gifs");
        try
        {
            Directory.CreateDirectory(frames);
            Directory.CreateDirectory(gifs);
        }
        catch (Exception error)
        {
            Note("建目录失败：" + error.Message);
            return;
        }

        var fps = 10;
        Note($"出帧中（{seconds} 秒 × {fps} 帧）…");

        try
        {
            // 一帧一句：step 推进时间，png 存图
            var script = new List<string>();
            for (var i = 0; i < seconds * fps; i++)
            {
                script.Add("step 0.1");
                script.Add($"png {Path.Combine(frames, $"f{i:D4}.png")}");
            }
            var scriptFile = Path.Combine(frames, "script.txt");
            File.WriteAllText(scriptFile, string.Join('\n', script));

            var args =
                $"--model \"{modelPath}\" --width 360 --height 720 --texscale 0.25 --masksize 512 " +
                $"--cache \"{Path.Combine(Program.Root, "cache")}\" --script \"{scriptFile}\"";

            var render = Process.Start(new ProcessStartInfo(renderer, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (render == null) { Note("渲染器没起来"); return; }
            await render.WaitForExitAsync();

            var count = Directory.GetFiles(frames, "f*.png").Length;
            if (count == 0)
            {
                Note("一帧都没出，渲染器可能报错了");
                return;
            }

            var output = Path.Combine(gifs, $"pet-{DateTime.Now:MMdd-HHmmss}.gif");
            Note($"合成中（{count} 帧）…");

            var mergeArgs =
                $"-y -framerate {fps} -i \"{Path.Combine(frames, "f%04d.png")}\" " +
                $"-vf \"split[s0][s1];[s0]palettegen=max_colors=192[p];[s1][p]paletteuse=dither=bayer\" " +
                $"-loop 0 \"{output}\"";

            var merge = Process.Start(new ProcessStartInfo(ffmpeg, mergeArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (merge == null) { Note("ffmpeg 没起来"); return; }
            await merge.WaitForExitAsync();

            if (File.Exists(output))
                Note($"录好了：{Path.GetFileName(output)}（{new FileInfo(output).Length / 1024} KB）");
            else
                Note("合成失败，看看 ffmpeg 的报错");
        }
        catch (Exception error)
        {
            Note("录制失败：" + error.Message);
        }
        finally
        {
            try { Directory.Delete(frames, true); } catch { }
        }
    }
}
