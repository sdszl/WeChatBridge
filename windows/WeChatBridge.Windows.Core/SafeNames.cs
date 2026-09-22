using System.Text;

namespace WeChatBridge.Windows.Core;

public static class SafeNames
{
    private const int MaxUtf8Bytes = 200;

    public static string FileName(string? raw, string fallback = "共享文件")
    {
        var name = raw?.Replace('/', '\\') ?? string.Empty;
        var leaf = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(leaf))
        {
            leaf = fallback;
        }

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(leaf.Length);
        foreach (var character in leaf)
        {
            if (char.IsControl(character) || invalid.Contains(character))
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (result is "" or "." or ".." || result.StartsWith(".", StringComparison.Ordinal))
        {
            result = fallback + result;
        }

        if (IsReservedDeviceName(result))
        {
            result = "_" + result;
        }

        return Truncate(result.Length == 0 ? fallback : result);
    }

    public static string FolderName(string? raw, string fallback = "微信流")
    {
        return FileName(raw, fallback).TrimEnd('.', ' ');
    }

    public static string UniqueFileName(string directory, string proposedName)
    {
        var safe = FileName(proposedName);
        var extension = Path.GetExtension(safe);
        var stem = Path.GetFileNameWithoutExtension(safe);
        var candidate = Path.Combine(directory, safe);
        var number = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem} {number++}{extension}");
        }

        return Path.GetFileName(candidate);
    }

    public static string UniqueDirectoryName(string directory, string proposedName)
    {
        var safe = FolderName(proposedName);
        var candidate = Path.Combine(directory, safe);
        var number = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{safe} {number++}");
        }

        return Path.GetFileName(candidate);
    }

    private static string Truncate(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) <= MaxUtf8Bytes)
        {
            return value;
        }

        var extension = Path.GetExtension(value);
        var stem = Path.GetFileNameWithoutExtension(value);
        var budget = Math.Max(1, MaxUtf8Bytes - Encoding.UTF8.GetByteCount(extension));
        while (stem.Length > 1 && Encoding.UTF8.GetByteCount(stem) > budget)
        {
            stem = stem[..^1];
        }

        return stem.Length == 0 ? "共享文件" + extension : stem + extension;
    }

    private static bool IsReservedDeviceName(string value)
    {
        var device = value.Split('.', 2)[0].TrimEnd(' ', '.').ToUpperInvariant();
        return device is "CON" or "PRN" or "AUX" or "NUL" or
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";
    }
}
