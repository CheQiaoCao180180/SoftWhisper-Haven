// 本机加（2026-11-19）：第一次启动的引导窗。
//
// 新用户第一次打开会遇到两件毫无提示的事：没有模型时桌面上只有一个空壳，没有语音时她说话不出声。
// 这里在启动时就查一遍，缺什么弹一张单页说明：放模型 / 装语音 / 怎么叫她。
// 弹过一次就记进 settings（Guided），以后不再打扰；模型和语音都齐了的话，装好当天就不弹。

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FishPet;

internal static class FirstRun
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x24, 0x2C, 0x3D));
    private static readonly Brush Soft = new SolidColorBrush(Color.FromRgb(0x6B, 0x74, 0x88));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x7F, 0xA8, 0xE8));

    /// <summary>缺东西、又还没引导过，就弹一次。返回有没有弹。</summary>
    internal static bool ShowIfNeeded(Settings settings, string modelFolder, bool hasModel, int voices)
    {
        if (settings.Guided) return false;
        // 只弹一次，缺什么标什么。语音那栏不拿"目录里有几条"当有无——她说话时会现生成缓存，
        // 刚说过一句就冒出几个 mp3，按条数判断会把"其实没装语音"误当成装好了。
        Places.Log($"第一次启动引导：模型={(hasModel ? "有" : "没有")}，语音缓存 {voices} 条");
        try
        {
            Show(modelFolder, hasModel, voices);
            Places.Log("第一次启动引导：窗口已弹出");
            settings.Guided = true;
            settings.Save();
            return true;
        }
        catch (Exception error)
        {
            Places.Log("引导窗弹不出来：" + error.Message);
            return false;
        }
    }

    private static void Show(string modelFolder, bool hasModel, int voices)
    {
        var panel = new StackPanel { Margin = new Thickness(30, 26, 30, 22) };

        panel.Children.Add(new TextBlock
        {
            Text = "第一次见面，三步就能用",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "缺的东西这儿都写着；都齐了，关掉这张就好。",
            FontSize = 12.5,
            Foreground = Soft,
            Margin = new Thickness(0, 0, 0, 18),
        });

        panel.Children.Add(Step("1", "放模型",
            hasModel ? "已经有了，桌面上站着的就是它。" : "把整个模型文件夹放进这个目录（里面要有 model3.json）：",
            hasModel ? null : modelFolder,
            hasModel ? null : ("打开 model 文件夹", () => OpenFolder(modelFolder)),
            hasModel));

        panel.Children.Add(Step("2", "装语音",
            voices >= 100
                ? $"语音已经齐了（{voices} 条缓存），她说话有声音。"
                : "她念台词靠 edge-tts：第一次说话会现生成，慢一站；想一次装齐，到设置工具的「语音」页。",
            null, null, voices >= 100));

        panel.Children.Add(Step("3", "怎么叫她",
            "桌面上的 DeepFish 图标双击就能叫出来；在 AI 对话框里跟她说话，她也会露面。\n" +
            "桌面上的「大肥鱼设置」双击能调人设、语音、参数；任务栏右下角那只小鲸鱼点开也是它。",
            null, null, true));

        var know = new Button
        {
            Content = "知道了",
            Width = 108,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 30, 16),
            Background = Accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13,
        };

        // 按钮钉在窗口底部，其余内容装进滚动区：窗口矮一点也不会把按钮挤出屏幕
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 0);
        Grid.SetRow(know, 1);
        grid.Children.Add(scroll);
        grid.Children.Add(know);

        var window = new Window
        {
            Title = "大肥鱼 · 第一次见面",
            Width = 620,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.White,
            Content = grid,
            ShowInTaskbar = true,
            Topmost = true,
            ResizeMode = ResizeMode.CanMinimize,
        };
        know.Click += (_, _) => window.Close();
        window.Show();
    }

    /// <summary>一步：编号 + 标题 + 说明 +（可选）路径和按钮 + 打勾。</summary>
    private static UIElement Step(string number, string title, string body, string? path, (string Text, Action Run)? action, bool done)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = done ? new SolidColorBrush(Color.FromRgb(0x8F, 0xC8, 0xA8)) : Accent,
            Child = new TextBlock
            {
                Text = done ? "✓" : number,
                Foreground = Brushes.White,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            Margin = new Thickness(9, 2, 0, 0),
        });
        box.Children.Add(head);

        box.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 12.5,
            Foreground = Soft,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 470,
            Margin = new Thickness(31, 5, 0, 0),
        });

        if (path != null)
        {
            var field = new TextBox
            {
                Text = path,
                IsReadOnly = true,
                FontSize = 12,
                Margin = new Thickness(31, 6, 0, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xE2, 0xF0)),
            };
            box.Children.Add(field);
        }

        if (action is { } todo)
        {
            var button = new Button
            {
                Content = todo.Text,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(31, 8, 0, 0),
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12.5,
            };
            button.Click += (_, _) => todo.Run();
            box.Children.Add(button);
        }

        return box;
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error) { Places.Log("打不开模型文件夹：" + error.Message); }
    }

    /// <summary>audio\voices 下有几个 mp3（没有就是 0）。</summary>
    internal static int VoiceCount()
    {
        try
        {
            var folder = Path.Combine(Places.Root, "audio", "voices");
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mp3", SearchOption.AllDirectories).Length : 0;
        }
        catch { return 0; }
    }
}
