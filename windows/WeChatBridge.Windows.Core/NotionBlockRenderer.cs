using System.Text;
using System.Text.Json.Nodes;

namespace WeChatBridge.Windows.Core;

public sealed record NotionBlockDocument(
    string Title,
    string VerificationMarker,
    IReadOnlyList<JsonObject> Blocks);

public static class NotionBlockRenderer
{
    private const int MaxRichTextCharacters = 1_800;

    public static NotionBlockDocument Render(
        string title,
        string? chatName,
        string? sceneName,
        DateTimeOffset createdAt,
        TranscriptInfo? transcript,
        string archiveName,
        Guid batchId)
    {
        var marker = $"WeChatBridge-Batch:{batchId:N}";
        var blocks = new List<JsonObject>();
        AddBlock(blocks, "heading_1", title);
        AddBlock(blocks, "paragraph", marker);
        AddBlock(blocks, "paragraph", "来源：微信合并转发归档");
        AddBlock(blocks, "paragraph", $"导入时间：{createdAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        AddBlock(blocks, "paragraph", $"原始归档：{archiveName}（文件已保存在本机收件箱）");

        if (!string.IsNullOrWhiteSpace(chatName))
        {
            AddBlock(blocks, "paragraph", $"聊天名称：{chatName.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            AddBlock(blocks, "paragraph", $"场景：{sceneName.Trim()}");
        }

        if (transcript?.MessageCount is { } count)
        {
            AddBlock(blocks, "paragraph", $"消息数：{count}");
        }

        AddBlock(blocks, "heading_2", "聊天记录");
        if (transcript?.Records is { Count: > 0 } records)
        {
            foreach (var record in records)
            {
                AddBlock(
                    blocks,
                    "heading_3",
                    $"{record.Sender} · {record.Date.ToLocalTime():yyyy-MM-dd HH:mm}");
                AddBlock(blocks, "paragraph", record.Text);
            }
        }
        else if (transcript is not null)
        {
            AddBlock(blocks, "paragraph", transcript.Body);
        }
        else
        {
            AddBlock(blocks, "paragraph", "未能从原始归档中解析聊天文本。原始文件仍保存在本机收件箱。");
        }

        return new NotionBlockDocument(title, marker, blocks);
    }

    private static void AddBlock(ICollection<JsonObject> blocks, string type, string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "（空消息）" : value;
        blocks.Add(new JsonObject
        {
            ["object"] = "block",
            ["type"] = type,
            [type] = new JsonObject
            {
                ["rich_text"] = RichText(text),
            },
        });
    }

    private static JsonArray RichText(string text)
    {
        var result = new JsonArray();
        foreach (var chunk in Chunks(text))
        {
            result.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = new JsonObject
                {
                    ["content"] = chunk,
                },
            });
        }

        return result;
    }

    private static IEnumerable<string> Chunks(string value)
    {
        var builder = new StringBuilder(MaxRichTextCharacters);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            if (count > 0 && count + runeText.Length > MaxRichTextCharacters)
            {
                yield return builder.ToString();
                builder.Clear();
                count = 0;
            }

            builder.Append(runeText);
            count += runeText.Length;
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }
}
