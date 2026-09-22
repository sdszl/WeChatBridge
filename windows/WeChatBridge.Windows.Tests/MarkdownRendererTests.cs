using WeChatBridge.Windows.Core;
using Xunit;

namespace WeChatBridge.Windows.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void RendersFrontMatterAndAttachmentLinks()
    {
        var transcript = new TranscriptInfo(
            "聊天记录.txt",
            "·甲\n2026年9月5日 08:05\n看图 photo.png\n",
            [new TranscriptRecord("甲", new DateTimeOffset(2026, 9, 5, 8, 5, 0, TimeSpan.FromHours(8)), "看图 photo.png")]);

        var markdown = MarkdownRenderer.Render(
            "测试群的聊天",
            "测试群",
            null,
            new DateTimeOffset(2026, 9, 5, 8, 10, 0, TimeSpan.FromHours(8)),
            transcript,
            "聊天.zip",
            new Dictionary<string, string> { ["photo.png"] = "photo.png" });

        Assert.Contains("title: \"测试群的聊天\"", markdown);
        Assert.Contains("messages: 1", markdown);
        Assert.Contains("![[附件/photo.png]]", markdown);
        Assert.Contains("[[附件/聊天.zip]]", markdown);
    }
}
