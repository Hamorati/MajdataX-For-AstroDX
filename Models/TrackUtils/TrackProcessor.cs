using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MajdataEdit_Neo.Models.TrackUtils;

public static class TrackProcessor
{
    public static bool ExtractAudio(
        string ffmpegPath,
        string videoPath,
        string audioOutputPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoPath)) return true;

        return RunFFmpeg(ffmpegPath, [
            "-y",
            "-i", videoPath,
            "-vn",
            "-ar", "44100",
            "-acodec", "libmp3lame",
            "-q:a", "2",
            audioOutputPath
        ], cancellationToken);
    }

    public static bool AdjustMediaTime(
        string ffmpegPath,
        string filePath,
        double targetTime,
        double offset,
        bool clone = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return true;

        var diff = targetTime - offset;
        if (Math.Abs(diff) < 0.01) return true;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var isAudio = ext is ".mp3" or ".wav" or ".ogg" or ".flac" or ".opus" or ".m4a" or ".aac" or ".wma" or ".aiff";
        var audioCodec = ext switch
        {
            ".mp3" => "libmp3lame",
            ".ogg" => "libvorbis",
            ".wav" => "pcm_s16le",
            ".flac" => "flac",
            ".opus" => "libopus",
            ".m4a" or ".aac" => "aac",
            ".wma" => "wmav2",
            _ => "aac"
        };

        var tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"t_{Guid.NewGuid()}{ext}");
        string[] arguments;

        if (diff < 0)
        {
            var cut = Math.Abs(diff).ToString(CultureInfo.InvariantCulture);
            if (isAudio)
                arguments = ["-y", "-i", filePath, "-ss", cut, "-c:a", audioCodec, tempPath];
            else
                arguments = [
                    "-y", "-i", filePath,
                    "-ss", cut,
                    "-c:v", "libx264",
                    "-c:a", audioCodec,
                    "-preset", "superfast",
                    tempPath
                ];
        }
        else
        {
            var delayMs = (diff * 1000).ToString(CultureInfo.InvariantCulture);
            var duration = diff.ToString(CultureInfo.InvariantCulture);
            if (isAudio)
            {
                arguments = [
                    "-y", "-i", filePath,
                    "-af", $"adelay={delayMs}:all=1",
                    "-c:a", audioCodec,
                    tempPath
                ];
            }
            else
            {
                arguments = [
                    "-y", "-i", filePath,
                    "-vf", $"tpad=start_duration={duration}:start_mode={(clone ? "clone" : "add")}",
                    "-an",
                    "-c:v", "libx264",
                    "-preset", "superfast",
                    tempPath
                ];
            }
        }

        try
        {
            if (!RunFFmpeg(ffmpegPath, arguments, cancellationToken))
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return false;
            }

            if (File.Exists(tempPath))
            {
                var rawPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"raw{ext}");
                if (File.Exists(rawPath))
                    File.Delete(rawPath);
                File.Move(filePath, rawPath);
                File.Move(tempPath, filePath);
            }
            return true;
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public static bool CompressVideo(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return RunFFmpeg(ffmpegPath, [
            "-y",
            "-i", inputPath,
            "-vf", "scale=-2:540,fps=30",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-b:v", "540k",
            "-an",
            outputPath
        ], cancellationToken);
    }

    static bool RunFFmpeg(
        string ffmpegPath,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to terminate FFmpeg: {ex}");
            }
        });

        process.WaitForExit();
        var error = errorTask.GetAwaiter().GetResult();
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (process.ExitCode != 0) throw new Exception($"FFmpeg Error: {error}");
        return true;
    }
}
