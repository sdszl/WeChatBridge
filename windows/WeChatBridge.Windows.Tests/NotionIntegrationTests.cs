using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeChatBridge.Windows.Core;
using Xunit;

namespace WeChatBridge.Windows.Tests;

public sealed class NotionIntegrationTests
{
    [Fact]
    public void RendersTranscriptAsSafeNotionBlocks()
    {
        var transcript = new TranscriptInfo(
            "聊天记录.txt",
            "原文",
            [new TranscriptRecord("甲", new DateTimeOffset(2026, 9, 5, 8, 5, 0, TimeSpan.FromHours(8)), new string('长', 4_500))]);

        var document = NotionBlockRenderer.Render(
            "测试群的聊天",
            "测试群",
            null,
            DateTimeOffset.Now,
            transcript,
            "聊天.zip",
            Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.Contains(document.Blocks, block => block.ToJsonString().Contains(document.VerificationMarker, StringComparison.Ordinal));
        Assert.Contains(document.Blocks, block => block["type"]?.GetValue<string>() == "heading_3");
        Assert.All(document.Blocks, block => AssertRichTextChunksDoNotExceed(block, 1_800));
    }

    [Fact]
    public async Task UsesCurrentApiVersionAndVerifiesCreatedPage()
    {
        const string pageId = "11111111-1111-1111-1111-111111111111";
        const string marker = "WeChatBridge-Batch:test";
        var handler = new NotionHandler(pageId, marker);
        using var httpClient = new HttpClient(handler);
        using var client = new NotionClient("secret_test_token", httpClient);

        var children = new List<JsonObject>
        {
            new()
            {
                ["object"] = "block",
                ["type"] = "paragraph",
                ["paragraph"] = new JsonObject
                {
                    ["rich_text"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = new JsonObject { ["content"] = marker },
                        },
                    },
                },
            },
        };

        var page = await client.CreatePageAsync(pageId, "测试页面", children);
        await client.AppendChildrenAsync(page.Id, children);
        var verification = await client.VerifyPageAsync(page.Id, marker);

        Assert.Equal(pageId, page.Id);
        Assert.True(verification.MarkerFound);
        Assert.Equal(1, verification.ChildCount);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(NotionClient.ApiVersion, request.NotionVersion));
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        Assert.Contains("page_id", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsNotionPageUrlAndRejectsUntrustedInput()
    {
        var pageId = NotionIds.Normalize("https://www.notion.so/My-page-11111111111111111111111111111111?pvs=4");

        Assert.Equal("11111111111111111111111111111111", pageId);
        Assert.False(NotionIds.TryNormalize("not-a-page-id", out _));
    }

    private static void AssertRichTextChunksDoNotExceed(JsonObject block, int maxCharacters)
    {
        var type = block["type"]?.GetValue<string>();
        var payload = type is null ? null : block[type] as JsonObject;
        var richText = payload?["rich_text"] as JsonArray;
        if (richText is null)
        {
            return;
        }

        foreach (var item in richText)
        {
            var content = item?["text"]?["content"]?.GetValue<string>();
            Assert.NotNull(content);
            Assert.InRange(content!.Length, 1, maxCharacters);
        }
    }

    private sealed class NotionHandler : HttpMessageHandler
    {
        private readonly string _pageId;
        private readonly string _marker;

        public NotionHandler(string pageId, string marker)
        {
            _pageId = pageId;
            _marker = marker;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.Headers.GetValues("Notion-Version").Single(),
                body));

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v1/pages")
            {
                return JsonResponse($"{{\"id\":\"{_pageId}\",\"url\":\"https://www.notion.so/{_pageId}\"}}");
            }

            if (request.Method == HttpMethod.Patch)
            {
                return JsonResponse("{\"object\":\"list\",\"results\":[]}");
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v1/pages/" + _pageId)
            {
                return JsonResponse($"{{\"id\":\"{_pageId}\",\"url\":\"https://www.notion.so/{_pageId}\"}}");
            }

            return JsonResponse(
                $"{{\"object\":\"list\",\"has_more\":false,\"next_cursor\":null,\"results\":[{{\"type\":\"paragraph\",\"paragraph\":{{\"rich_text\":[{{\"type\":\"text\",\"text\":{{\"content\":\"{_marker}\"}}}}]}}}}]}}");
        }

        private static HttpResponseMessage JsonResponse(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string NotionVersion, string Body);
}
