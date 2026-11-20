// 重写版（2026-11-17）：音乐页。
// 本地音乐库：把歌丢进插件的 music\ 文件夹，这里就能看到、能放。
// 放歌的时候她会自己听到、跟着节拍摇——那是桌宠本来就有的能力（Beat.cs），不用额外接线。

using NAudio.Wave;

namespace SettingTool;

internal sealed partial class MainForm
{
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aiff"];

    private FlowLayoutPanel? trackList;
    private Label? nowPlaying;
    private NeoButton? playButton;
    private readonly List<string> tracks = [];
    private int currentTrack = -1;

    private IWavePlayer? player;
    private WaveStream? reader;
    private bool stoppingFromEnd;

    private FlowLayoutPanel BuildMusicPage()
    {
        var page = NewPage();
        Mount(page, new PageHead("音乐", "放本地音乐。她听见了会自己跟着节拍摇。", CardWidth));

        var folder = new Card("音乐库", CardWidth);
        var openFolder = new NeoButton("打开 music 文件夹") {  };
        openFolder.Click += (_, _) => OpenMusicFolder();
        var rescan = new NeoButton("重新扫描");
        rescan.Click += (_, _) => { FillTracks(); Note("已重新扫描音乐"); };
        folder.AddControl(Row(openFolder, rescan));
        folder.AddControl(Hint("支持 mp3 / wav / m4a / flac / ogg。把歌复制进去，再点「重新扫描」。"));

        trackList = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Card,
            Width = CardWidth - 44,
            Tag = "wide", MaximumSize = new Size(CardWidth - 44, 0),
            MinimumSize = new Size(CardWidth - 44, 0),
            Margin = new Padding(0, 6, 0, 0),
        };
        folder.AddControl(trackList);
        Mount(page, folder);

        var control = new Card("播放", CardWidth);
        nowPlaying = new Label
        {
            Text = "还没放歌",
            AutoSize = false,
            Width = CardWidth - 60,
            Tag = "wide", Height = 30,
            ForeColor = Theme.Ink,
            Font = Theme.Body,
            BackColor = Theme.Card,
            Margin = new Padding(0, 2, 0, 8),
        };
        control.AddControl(nowPlaying);

        playButton = new NeoButton("播放") { Width = 100 };
        playButton.Click += (_, _) => TogglePlay();

        var next = new NeoButton("下一首") { Width = 96 };
        next.Click += (_, _) => NextTrack();

        var stop = new NeoButton("停止") { Width = 88 };
        stop.Click += (_, _) => StopPlayback();

        control.AddControl(Row(playButton, next, stop));
        control.AddControl(Hint("关掉窗口会停止播放。桌宠会一直听着——歌一响她就开始摇。"));
        Mount(page, control);

        FillTracks();
        return page;
    }

    private void OpenMusicFolder()
    {
        var folder = Path.Combine(Program.Root, "music");
        Directory.CreateDirectory(folder);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
    }

    private void FillTracks()
    {
        if (trackList == null) return;
        trackList.Controls.Clear();
        tracks.Clear();

        var folder = Path.Combine(Program.Root, "music");
        try { Directory.CreateDirectory(folder); } catch { }

        try
        {
            tracks.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(path => AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .OrderBy(path => Path.GetFileName(path)));
        }
        catch { }

        if (tracks.Count == 0)
        {
            trackList.Controls.Add(Hint("music 文件夹是空的 —— 点上面的按钮打开它，把歌拖进去"));
            return;
        }

        for (var i = 0; i < tracks.Count; i++)
        {
            var index = i;
            var name = Path.GetFileNameWithoutExtension(tracks[i]);
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Card,
                Width = 620,
                Margin = new Padding(0, 1, 0, 1),
            };

            var number = new Label
            {
                Text = (i + 1).ToString("00"),
                AutoSize = false,
                Width = 34,
                Height = 32,
                ForeColor = Theme.InkFaint,
                Font = Theme.Mono,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 8, 0),
            };
            row.Controls.Add(number);

            var button = new NeoButton(name) { Height = 32, Margin = new Padding(0) };
            button.Click += (_, _) => PlayTrack(index);
            row.Controls.Add(button);

            trackList.Controls.Add(row);
        }
    }

    private void EnsurePlayer()
    {
        if (player != null) return;
        player = new WaveOutEvent { DesiredLatency = 200 };
        player.PlaybackStopped += (_, _) => nextFromStopped?.Invoke();
    }

    private Action? nextFromStopped;

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= tracks.Count) return;
        try
        {
            StopPlayback();
            EnsurePlayer();
            currentTrack = index;

            // 本机改（2026-11-17）：mp3 走纯托管的读取器，别用走 MediaFoundation 的那个
            // （那一套在后台线程调用会崩在 COM 上，桌宠那边就是被它坑的）
            var extension = Path.GetExtension(tracks[index]).ToLowerInvariant();
            reader = extension switch
            {
                ".mp3" => new Mp3FileReader(tracks[index]),
                ".wav" => new WaveFileReader(tracks[index]),
                _ => new AudioFileReader(tracks[index]),
            };
            player!.Init(reader);
            player.Play();

            var title = Path.GetFileNameWithoutExtension(tracks[index]);
            if (nowPlaying != null) nowPlaying.Text = "正在放：" + title;
            if (playButton != null) playButton.Text = "暂停";
            Note("正在放：" + title);

            nextFromStopped = () =>
            {
                if (stoppingFromEnd) return;
                stoppingFromEnd = true;
                var upcoming = currentTrack + 1 < tracks.Count ? currentTrack + 1 : 0;
                stoppingFromEnd = false;
                PlayTrack(upcoming);
            };
        }
        catch (Exception error)
        {
            Note("放不出来：" + error.Message);
        }
    }

    private void TogglePlay()
    {
        if (player == null || reader == null)
        {
            if (tracks.Count > 0) PlayTrack(currentTrack < 0 ? 0 : currentTrack);
            return;
        }

        if (player.PlaybackState == PlaybackState.Playing)
        {
            player.Pause();
            if (playButton != null) playButton.Text = "继续";
        }
        else
        {
            player.Play();
            if (playButton != null) playButton.Text = "暂停";
        }
    }

    private void NextTrack()
    {
        if (tracks.Count == 0) return;
        PlayTrack((currentTrack + 1) % tracks.Count);
    }

    private void StopPlayback()
    {
        try
        {
            if (player != null)
            {
                player.Stop();
                nextFromStopped = null;
            }
            reader?.Dispose();
            reader = null;
            if (nowPlaying != null) nowPlaying.Text = "还没放歌";
            if (playButton != null) playButton.Text = "播放";
        }
        catch { }
    }
}
