// 本机加（2026-11-16）：用 NAudio 播 mp3，顺手把实时振幅交出去——嘴就能真的跟着声音动，
// 而不是跟着一个固定正弦波空转。
//
// 为什么另起一个类：Voice.cs 原来用 Windows.Media.Playback.MediaPlayer，它不给实时电平；
// NAudio 读出来的每一帧样本我们都能看到，算 RMS 就得到「这一刻有多大声」。
//
// 本机修（2026-11-17）：原来用 AudioFileReader，它底层走 MediaFoundation 解 mp3。
// 从后台线程调用时会崩在 COM 上（IMFSourceReader，E_NOINTERFACE），表现为「话说到一半没了」。
// 换成 Mp3FileReader：纯托管解码，不碰 COM，稳。

using System;
using NAudio.Wave;

namespace FishPet;

internal sealed class TalkingPlayer : IDisposable
{
    // 本机改（2026-11-19）：150ms 是给蓝牙耳机留的余量，有线和普通音箱听着就慢半拍。
    // 降到 70：普通设备没感觉，蓝牙真断音了把这里调回去。
    private readonly WaveOutEvent output = new() { DesiredLatency = 70, NumberOfBuffers = 3 };
    private LevelReader? source;
    private Mp3FileReader? reader;
    private bool playing;

    internal float Level => playing ? source?.Level ?? 0f : 0f;
    internal bool Playing => playing;

    internal double Volume
    {
        get => output.Volume;
        set
        {
            try { output.Volume = (float)Math.Clamp(value, 0, 1); } catch { }
        }
    }

    internal void Play(string path)
    {
        Stop();
        try
        {
            // 本机改（2026-11-19）：原来每次都 new Mp3FileReader 再 Init，紧接着上一句的尾巴
            // 还留在缓冲里，听着像回声。Init 之前先确认输出已经彻底停了。
            try { output.Stop(); } catch { }
            reader = new Mp3FileReader(path);
            var samples = reader.ToSampleProvider();
            // 单声道统一，省得后面算 RMS 时两个声道数不一致
            if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoProvider(samples);
            source = new LevelReader(samples);
            output.PlaybackStopped += OnStopped;
            output.Init(source);
            output.Play();
            playing = true;
        }
        catch (Exception error)
        {
            Places.Log("NAudio 放不出来：" + error.Message);
            Stop();
        }
    }

    // 播完（或被设备中断）自动收尾：playing 一置 false，下一句才敢接
    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        playing = false;
        if (e.Exception != null) Places.Log("播放中断：" + e.Exception.Message);
    }

    internal void Stop()
    {
        playing = false;
        try { output.PlaybackStopped -= OnStopped; } catch { }
        try { output.Stop(); } catch { }
        try { reader?.Dispose(); } catch { }
        reader = null;
        source = null;
    }

    public void Dispose()
    {
        Stop();
        try { output.Dispose(); } catch { }
    }

    /// <summary>两个声道取平均，变成单声道。</summary>
    private sealed class StereoToMonoProvider : ISampleProvider
    {
        private readonly ISampleProvider inner;
        private float[] buffer = [];

        internal StereoToMonoProvider(ISampleProvider inner)
        {
            this.inner = inner;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(inner.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] target, int offset, int count)
        {
            var need = count * 2;
            if (buffer.Length < need) buffer = new float[need];
            var read = inner.Read(buffer, 0, need);
            var frames = read / 2;
            for (var i = 0; i < frames; i++)
                target[offset + i] = (buffer[i * 2] + buffer[i * 2 + 1]) * .5f;
            return frames;
        }
    }

    /// <summary>包一层采样器：每读到一块就把它的响度算出来，给口型用。</summary>
    private sealed class LevelReader : ISampleProvider
    {
        private readonly ISampleProvider inner;
        private float level;

        internal LevelReader(ISampleProvider inner) => this.inner = inner;

        internal float Level => level;

        public WaveFormat WaveFormat => inner.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read <= 0) { level *= .6f; return read; }

            // RMS：人声里静音段和重音差别很大，平方平均最能反映「这会儿多大声」
            double sum = 0;
            for (var i = offset; i < offset + read; i++) sum += buffer[i] * (double)buffer[i];
            var rms = Math.Sqrt(sum / read);

            // 抬一下再压一下：轻声也能看出嘴在动，重音也不至于把嘴撑爆
            var shaped = (float)Math.Pow(Math.Clamp(rms * 3.2, 0, 1), 0.62);
            // 平滑：快起慢落，像真的嘴
            level = shaped > level ? level * .35f + shaped * .65f : level * .72f + shaped * .28f;
            return read;
        }
    }
}
