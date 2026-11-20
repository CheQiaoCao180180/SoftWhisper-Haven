using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FishPet;

// 本机加（2026-11-17）：把东西拖到她身上，她"吃掉"。
//   · 图片：看一眼，评一句（「这张好看」「谁啊这是」）
//   · 文本 / 代码：当成 token 吃掉（她本来就爱吃 token）
//   · 别的文件：也吃，按扩展名说不同的词
// 拖进来的时候她会抬头看，吃下去有一下吞咽动作。
internal sealed partial class PetWindow
{
    private DispatcherTimer? dropWatch;
    private bool draggingFile;

    /// <summary>窗口建好之后调一次，把拖放接上。</summary>
    private void HookDrop()
    {
        AllowDrop = true;

        DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;

            draggingFile = true;
            // 抬头看你，嘴微张——像等着投喂
            body.Apply("抬头 看上面 张嘴");
            if (dropWatch == null)
            {
                dropWatch = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
                {
                    if (draggingFile) return;
                    dropWatch?.Stop();
                    body.Relax();
                }, Dispatcher);
            }
            dropWatch.Start();
        };

        DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };

        DragLeave += (_, e) =>
        {
            draggingFile = false;
            body.Relax();
            e.Handled = true;
        };

        Drop += (_, e) => OnFileDropped(e);
    }

    private void OnFileDropped(DragEventArgs e)
    {
        draggingFile = false;
        if (dropWatch != null) dropWatch.Stop();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        e.Handled = true;

        // 一次最多吃三个，免得一口气吃太多
        var eaten = files.Take(3).ToList();
        var lines = new List<string>();

        foreach (var file in eaten)
        {
            var kind = Classify(file);
            lines.Add(EatLine(kind, file));
        }

        // 吃下去的动作：张嘴 → 抿嘴 → 眯眼笑
        body.Apply("张嘴 前倾");
        Dispatcher.BeginInvoke(new Action(() =>
        {
            body.Apply("抿嘴 眯眼笑 点头×2");
            var text = string.Join("　", lines);
            SayLine(text);
            BondSeen();
        }), DispatcherPriority.Background);
    }

    private enum FoodKind { Image, Text, Audio, Video, Archive, Unknown }

    private static FoodKind Classify(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => FoodKind.Image,
            ".txt" or ".md" or ".json" or ".cs" or ".py" or ".js" or ".html" or ".css" or ".xml" or ".yml" or ".yaml" or ".log" => FoodKind.Text,
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => FoodKind.Audio,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => FoodKind.Video,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => FoodKind.Archive,
            _ => FoodKind.Unknown,
        };
    }

    private static readonly Random eatRoll = new();

    private static string EatLine(FoodKind kind, string path)
    {
        var name = Path.GetFileName(path);
        var shortName = name.Length > 14 ? name[..12] + "…" : name;

        return kind switch
        {
            FoodKind.Image => Pick(shortName + "，这张我看看", "谁啊这是……拍得还行", "收下了，挂在心里"),
            FoodKind.Text => Pick(shortName + "，好多字，嚼起来有点干", "这个能吃，就是有点硬", "文字嘛，我最爱吃了"),
            FoodKind.Audio => Pick(shortName + "，这个得配着节拍吃", "有声音的，稀里呼噜", "吃完想跳两下"),
            FoodKind.Video => Pick(shortName + "，这个太大块，得慢慢咬", "嚼不动……先含着", "这个能吃一晚上"),
            FoodKind.Archive => Pick(shortName + "，一咬开全是东西", "压缩过的，特别顶", "这个最管饱"),
            _ => Pick(shortName + "……能吃吗？我试试", "嚼起来像塑料，但没事", "反正不挑食"),
        };
    }

    private static string Pick(params string[] options) => options[eatRoll.Next(options.Length)];
}
