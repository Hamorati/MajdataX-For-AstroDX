using System;
using System.IO;
using System.Linq;

namespace MajdataEdit_Neo.Models.TrackUtils;

/// <summary>
/// 谱面目录下的 track 音频文件查找（多格式支持）。
/// </summary>
public static class TrackFile
{
    /// <summary>支持的音频扩展名（按查找优先级）。</summary>
    public static readonly string[] SupportedExtensions =
        [".mp3", ".ogg", ".wav", ".flac", ".opus", ".m4a", ".aac", ".wma", ".aiff"];

    /// <summary>BASS 核心可直接解码的格式（无需 ffmpeg 转码）。</summary>
    public static bool IsBassNative(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".ogg" or ".wav" or ".flac" or ".aiff";

    /// <summary>
    /// 按优先级查找目录下的 track 音频文件（大小写不敏感）。
    /// 若不存在 track.* 命名的文件，退化为查找目录下任意受支持的音频文件。
    /// </summary>
    public static string? Find(string dirpath)
    {
        if (!Directory.Exists(dirpath)) return null;

        foreach (var ext in SupportedExtensions)
        {
            var p = Path.Combine(dirpath, "track" + ext);
            if (File.Exists(p)) return p;
        }

        var lower = SupportedExtensions.Select(e => e.ToLowerInvariant()).ToArray();
        string? Best(string? f)
        {
            if (f is null) return null;
            var idx = Array.IndexOf(lower, Path.GetExtension(f).ToLowerInvariant());
            return idx < 0 ? null : f;
        }

        // track.* 大小写兜底
        var candidate = Directory.EnumerateFiles(dirpath, "track.*")
            .Select(Best)
            .Where(f => f is not null)
            .OrderBy(f => Array.IndexOf(lower, Path.GetExtension(f!).ToLowerInvariant()))
            .FirstOrDefault();
        if (candidate is not null) return candidate;

        // 目录下任意受支持的音频文件（如用户直接选择 song.opus 建谱时）
        return Directory.EnumerateFiles(dirpath)
            .Select(Best)
            .Where(f => f is not null)
            .OrderBy(f => Array.IndexOf(lower, Path.GetExtension(f!).ToLowerInvariant()))
            .FirstOrDefault();
    }
}
