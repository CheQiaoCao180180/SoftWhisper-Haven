using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FishPet;

// 本机加（2026-11-17）：对话框窗口。
// 打字 → 她那边的气泡说出来（带动作），这里同时留一份聊天记录。
public partial class ChatWindow : Window
{
    private readonly HttpClient http;
    private readonly FishPet.Settings settings;
    private readonly Func<string, Task<Mutter.Spoken?>> ask;
    private bool busy;

    internal ChatWindow(HttpClient http, FishPet.Settings settings, Func<string, Task<Mutter.Spoken?>> ask)
    {
        InitializeComponent();
        this.http = http;
        this.settings = settings;
        this.ask = ask;

        AddBubble("想说什么就说吧。", fromHer: true);
        Loaded += (_, _) => Input.Focus();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Messages.Children.Clear();
        Chat.Forget();
        AddBubble("……刚才聊的我都忘啦。", fromHer: true);
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        // 输入框变高时，滚到底
        Scroll.ScrollToEnd();
    }

    private async void OnInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            await SendAsync();
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private async void OnSend(object sender, RoutedEventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        if (busy) return;

        var text = Input.Text.Trim();
        if (text.Length == 0) return;

        Input.Clear();
        AddBubble(text, fromHer: false);

        if (string.IsNullOrWhiteSpace(settings.LlmApiKey) && string.IsNullOrWhiteSpace(settings.LlmBaseUrl))
        {
            AddBubble("……我还没接上大模型，说不出话。（设置里填一下接口地址和模型）", fromHer: true);
            return;
        }

        busy = true;
        Send.IsEnabled = false;

        try
        {
            var said = await ask(text);
            if (said == null)
                AddBubble("……脑子卡住了，再说一次？", fromHer: true);
            else
                AddBubble(said.Text, fromHer: true);
        }
        catch (Exception error)
        {
            AddBubble("出错了：" + error.Message, fromHer: true);
        }
        finally
        {
            busy = false;
            Send.IsEnabled = true;
            Input.Focus();
        }
    }

    private void AddBubble(string text, bool fromHer)
    {
        var bubble = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 5, 0, 5),
            MaxWidth = 300,
            HorizontalAlignment = fromHer ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = fromHer
                ? new SolidColorBrush(Color.FromRgb(0xF1, 0xF7, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xEE)),
        };

        bubble.Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x44, 0x58)),
            LineHeight = 20,
        };

        Messages.Children.Add(bubble);
        Dispatcher.BeginInvoke(new Action(() => Scroll.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
    }
}
