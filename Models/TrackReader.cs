using MajdataEdit_Neo.Base;
using MajdataEdit_Neo.Models.TrackUtils;
using ManagedBass;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MajdataEdit_Neo.Models;

class TrackReader : IDisposable
{
    private bool _disposed;
    private int bgmStream = 0;
    private string? _currentTrackPath;
    private string? _transcodedWavPath;

    public TrackReader()
    {
        if (File.Exists(MajEnv.MajdataViewBassDllFile))
            NativeLibrary.Load(MajEnv.MajdataViewBassDllFile);
        Bass.Init(Bass.NoSoundDevice);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Bass.StreamFree(bgmStream);
        bgmStream = 0;
        CleanupTranscoded();
        Bass.Free();
    }

    /// <summary>
    /// 当前真正可播放的音频路径：非 BASS 原生格式（opus/m4a/aac/wma 等）已用 ffmpeg
    /// 转码为临时 WAV。推送给播放器（ViewX）时应使用此路径。
    /// </summary>
    public string? ResolvedTrackPath => _transcodedWavPath ?? _currentTrackPath;

    public void Play(double time)
    {
        Bass.ChannelSetPosition(bgmStream, Bass.ChannelSeconds2Bytes(bgmStream, time));
        Bass.ChannelPlay(bgmStream);
    }
    public void Pause()
    {
        Bass.ChannelPause(bgmStream);
    }
    public void Stop()
    {
        Bass.ChannelStop(bgmStream);
    }

    public double CurrentPosition()
    {
        return Bass.ChannelBytes2Seconds(bgmStream, Bass.ChannelGetPosition(bgmStream));
    }

    public bool isPlaying { get { return Bass.ChannelIsActive(bgmStream) == PlaybackState.Playing; } }

    public TrackInfo ReadTrack(string dirpath)
    {
        var filePath = TrackFile.Find(dirpath)
            ?? throw new Exception(
                $"找不到 track 音频文件（支持 {string.Join(", ", TrackFile.SupportedExtensions)}）。\nTrack file not found.");

        if (bgmStream is not 0)
        {
            Bass.StreamFree(bgmStream);
            bgmStream = 0;
        }
        CleanupTranscoded();
        _currentTrackPath = filePath;

        // 优先直接解码；BASS 核心支持 mp3/ogg(Vorbis)/wav/flac/aiff
        var decodePath = filePath;
        var bgmDecode = Bass.CreateStream(decodePath, 0L, 0L, BassFlags.Decode);
        if (bgmDecode == 0)
        {
            // 非原生格式或 BASS 无法识别的容器 → ffmpeg 转码为临时 WAV
            decodePath = TranscodeToWav(filePath);
            bgmDecode = Bass.CreateStream(decodePath, 0L, 0L, BassFlags.Decode);
            if (bgmDecode == 0)
                throw new Exception(
                    $"音频解码失败（{Bass.LastError}）。\nAudio decode failed: {decodePath}");
        }

        var bgmSample = 0;
        try
        {
            var songLength = Bass.ChannelBytes2Seconds(bgmDecode, Bass.ChannelGetLength(bgmDecode));
            bgmSample = Bass.SampleLoad(decodePath, 0, 0, 1, BassFlags.Default);
            if (bgmSample == 0)
                throw new Exception(
                    $"音频采样失败（{Bass.LastError}）。\nAudio sample load failed: {decodePath}");

            var bgmInfo = Bass.SampleGetInfo(bgmSample);
            var freq = bgmInfo.Frequency;
            var sampleCount = (long)(songLength * freq * 2);
            var bgmRAW = new short[sampleCount];
            Bass.SampleGetData(bgmSample, bgmRAW);

            bgmStream = Bass.CreateStream(decodePath, 0, 0, BassFlags.Prescan);
            if (bgmStream == 0)
                throw new Exception(
                    $"播放流创建失败（{Bass.LastError}）。\nPlayback stream create failed: {decodePath}");

            return new TrackInfo(songLength, bgmRAW);
        }
        catch (Exception e)
        {
            throw new Exception(
                $"音频解码失败（{Path.GetFileName(filePath)}，{Bass.LastError}）。\n" +
                "Audio decode fail. 支持的格式: mp3/ogg/wav/flac/aiff（原生）+ opus/m4a/aac/wma（需 ffmpeg）\n" +
                e.Message,
                e);
        }
        finally
        {
            Bass.StreamFree(bgmDecode);
            if (bgmSample != 0) Bass.SampleFree(bgmSample);
        }
    }

    /// <summary>用 ffmpeg 把任意音频转码为临时 WAV（44100Hz 双声道 PCM16）。</summary>
    private string TranscodeToWav(string filePath)
    {
        _transcodedWavPath = Path.Combine(Path.GetTempPath(), $"majdata_track_{Guid.NewGuid():N}.wav");
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-y", "-i", filePath, "-vn", "-ar", "44100", "-ac", "2", "-acodec", "pcm_s16le", _transcodedWavPath })
            startInfo.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new Exception("无法启动 ffmpeg。");
            var errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new Exception($"ffmpeg 转码失败: {error}");
        }
        catch (Win32Exception)
        {
            throw new Exception(
                $"音频格式 {Path.GetExtension(filePath)} 需要安装 ffmpeg 才能解码。\n" +
                "请将 ffmpeg 加入 PATH 后重试（mp3/ogg/wav/flac 无需 ffmpeg）。");
        }

        if (!File.Exists(_transcodedWavPath))
            throw new Exception("ffmpeg 转码未产出文件。");
        return _transcodedWavPath;
    }

    private void CleanupTranscoded()
    {
        if (_transcodedWavPath is { } p && File.Exists(p))
        {
            try { File.Delete(p); }
            catch { /* 文件被占用时忽略，留待系统清理 */ }
        }
        _transcodedWavPath = null;
    }
}

public class TrackInfo
{
    public double Length { get; }
    public short[] RawWave { get; } = Array.Empty<short>();
    private short[][] waveThumbnails = new short[3][];
    public short[] GetWaveThumbnails(int thumbLevel = 0)
    {
        if (thumbLevel < 0) return waveThumbnails[0];
        if (thumbLevel > 2) return waveThumbnails[2];
        return waveThumbnails[thumbLevel];
    }
    public TrackInfo(double length, short[] rawWave)
    {
        if (length == 0 || rawWave.Length == 0) throw new Exception("Music Wave Load Error");
        Length = length;
        RawWave = rawWave;

        var sampleCount = rawWave.Length;

        waveThumbnails[0] = new short[sampleCount / 20 + 1];
        for (var i = 0; i < sampleCount; i = i + 20) waveThumbnails[0][i / 20] = RawWave[i];
        waveThumbnails[1] = new short[sampleCount / 50 + 1];
        for (var i = 0; i < sampleCount; i = i + 50) waveThumbnails[1][i / 50] = RawWave[i];
        waveThumbnails[2] = new short[sampleCount / 100 + 1];
        for (var i = 0; i < sampleCount; i = i + 100) waveThumbnails[2][i / 100] = RawWave[i];
    }
}
