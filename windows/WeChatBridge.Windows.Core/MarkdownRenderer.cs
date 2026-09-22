using System.Globalization;

namespace WeChatBridge.Windows.Core;

public static class MarkdownRenderer
{
    public static string Title(string? chatName, TranscriptInfo? transcript, string archiveName)
    {
        var supplied = chatName?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            return supplied.EndsWith("的聊天", StringComparison.Ordinal) ? supplied : supplied + "的聊天";
        }

        var participants = transcript?.Records?
            .Select(x => x.Sender.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        return participants.Length switch
        {
            0 => Path.GetFileNameWithoutExtension(archiveName),
            1 => participants[0] + "的聊天",
            2 => participants[0] + "与" + participants[1] + "的聊天",
            _ => participants[0] + $"等{participants.Length}人的聊天",
        };
    }

    public static string Render(
        string title,
        string? chatName,
        string? sceneName,
        DateTimeOffset createdAt,
        TranscriptInfo? transcript,
        string archiveName,
        IReadOnlyDictionary<string, string> attachments)
    {
        var exported = createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            "---",
            $"title: {Quote(title)}",
            "source: WeChat",
        };

        if (!string.IsNullOrWhiteSpace(chatName))
        {
            lines.Add($"chat: {Quote(chatName.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            lines.Add($"scene: {Quote(sceneName.Trim())}");
        }

        lines.Add($"exported: {exported}");
        if (transcript?.MessageCount is { } count)
        {
            lines.Add($"messages: {count}");
        }

        lines.Add($"archive: {Quote($"附件/{archiveName}")}");
        lines.Add("---");
        lines.Add(string.Empty);
        lines.Add($"# {title}");
        lines.Add(string.Empty);
        lines.Add($"> 来源：微信 · 原始归档：[[附件/{EscapeLink(archiveName)}]]");

        if (transcript is not null)
        {
            lines.Add(string.Empty);
            lines.Add("## 聊天记录");
            if (transcript.Records is { Count: > 0 } records)
            {
                foreach (var record in records)
                {
                    lines.Add(string.Empty);
                    lines.Add($"**{record.Sender}** · {record.Date.ToLocalTime():yyyy-MM-dd HH:mm}");
                    lines.Add(string.Empty);
                    lines.Add(record.Text);
                    foreach (var attachment in ReferencedAttachments(record.Text, attachments))
                    {
                        lines.Add(string.Empty);
                        lines.Add(AttachmentLink(attachment));
                    }
                }
            }
            else
            {
                lines.Add(string.Empty);
                lines.Add(transcript.Body);
            }
        }
        else
        {
            lines.Add(string.Empty);
            lines.Add("未能从原始归档中解析聊天文本。原始文件已保留，可在附件中打开。");
        }

        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    private static IEnumerable<string> ReferencedAttachments(
        string text,
        IReadOnlyDictionary<string, string> attachments)
    {
        var remaining = text;
        foreach (var pair in attachments.OrderByDescending(x => x.Key.Length).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            if (pair.Key.Length == 0 || !remaining.Contains(pair.Key, StringComparison.Ordinal))
            {
                continue;
            }

            yield return pair.Value;
            remaining = remaining.Replace(pair.Key, string.Empty, StringComparison.Ordinal);
        }
    }

    private static string AttachmentLink(string name)
    {
        var path = "附件/" + EscapeLink(name);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or
            ".mp4" or ".mov" or ".m4v" or ".avi" or ".mp3" or ".wav" or ".m4a" or ".pdf"
            ? $"![[{path}]]"
            : $"[[{path}]]";
    }

    private static string EscapeLink(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal)
            .Replace("^", "\\^", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
    }
}
