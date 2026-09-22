using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeChatBridge.Windows.Core;

public sealed record NotionPage(string Id, string Url);

public sealed record NotionVerification(
    string PageId,
    string PageUrl,
    int ChildCount,
    bool MarkerFound);

public interface INotionClient
{
    Task<NotionPage> CreatePageAsync(
        string parentPageId,
        string title,
        IReadOnlyList<JsonObject> children,
        CancellationToken cancellationToken = default);

    Task AppendChildrenAsync(
        string pageId,
        IReadOnlyList<JsonObject> children,
        CancellationToken cancellationToken = default);

    Task<NotionVerification> VerifyPageAsync(
        string pageId,
        string marker,
        CancellationToken cancellationToken = default);
}

public sealed class NotionClient : INotionClient, IDisposable
{
    public const string ApiVersion = "2026-03-11";
    public const int MaxBlocksPerRequest = 50;

    private static readonly Uri ApiBaseUri = new("https://api.notion.com/v1/");
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _token;

    public NotionClient(string token, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new NotionConfigurationException("请先填写 Notion Token。");
        }

        _token = token.Trim();
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _ownsHttpClient = httpClient is null;
    }

    public async Task<NotionPage> CreatePageAsync(
        string parentPageId,
        string title,
        IReadOnlyList<JsonObject> children,
        CancellationToken cancellationToken = default)
    {
        var parent = NotionIds.Normalize(parentPageId);
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "微信聊天记录" : title.Trim();
        var body = new JsonObject
        {
            ["parent"] = new JsonObject
            {
                ["page_id"] = parent,
            },
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject
                {
                    ["title"] = RichText(safeTitle),
                },
            },
            ["children"] = Blocks(children),
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "pages", body, cancellationToken);
        return ParsePage(response.RootElement);
    }

    public async Task AppendChildrenAsync(
        string pageId,
        IReadOnlyList<JsonObject> children,
        CancellationToken cancellationToken = default)
    {
        if (children.Count == 0)
        {
            return;
        }

        var id = NotionIds.Normalize(pageId);
        var body = new JsonObject
        {
            ["children"] = Blocks(children),
        };
        using var response = await SendJsonAsync(
            HttpMethod.Patch,
            $"blocks/{Uri.EscapeDataString(id)}/children",
            body,
            cancellationToken);
    }

    public async Task<NotionVerification> VerifyPageAsync(
        string pageId,
        string marker,
        CancellationToken cancellationToken = default)
    {
        var id = NotionIds.Normalize(pageId);
        using var pageResponse = await SendJsonAsync(
            HttpMethod.Get,
            $"pages/{Uri.EscapeDataString(id)}",
            null,
            cancellationToken);
        var page = ParsePage(pageResponse.RootElement);

        var childCount = 0;
        var markerFound = false;
        string? cursor = null;
        do
        {
            var query = "page_size=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                query += "&start_cursor=" + Uri.EscapeDataString(cursor);
            }

            using var childrenResponse = await SendJsonAsync(
                HttpMethod.Get,
                $"blocks/{Uri.EscapeDataString(id)}/children?{query}",
                null,
                cancellationToken);
            var root = childrenResponse.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in results.EnumerateArray())
                {
                    childCount++;
                    markerFound |= ContainsText(child, marker);
                }
            }

            cursor = root.TryGetProperty("has_more", out var hasMore) && hasMore.GetBoolean()
                ? root.TryGetProperty("next_cursor", out var next) ? next.GetString() : null
                : null;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return new NotionVerification(page.Id, page.Url, childCount, markerFound);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(ApiBaseUri, relativePath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.TryAddWithoutValidation("Notion-Version", ApiVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(content)
                    ? JsonDocument.Parse("{}")
                    : JsonDocument.Parse(content);
            }

            if (attempt + 1 < maxAttempts && IsRetryable(response.StatusCode))
            {
                await Task.Delay(RetryDelay(response, attempt), cancellationToken);
                continue;
            }

            throw ToApiException(response.StatusCode, content);
        }

        throw new NotionApiException("Notion API 请求失败。", (int)HttpStatusCode.ServiceUnavailable);
    }

    private static NotionApiException ToApiException(HttpStatusCode statusCode, string content)
    {
        string? message = null;
        string? code = null;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            message = root.TryGetProperty("message", out var messageProperty)
                ? messageProperty.GetString()
                : null;
            code = root.TryGetProperty("code", out var codeProperty)
                ? codeProperty.GetString()
                : null;
        }
        catch (JsonException)
        {
            // The HTTP status is still useful when a proxy returns non-JSON.
        }

        var detail = string.IsNullOrWhiteSpace(message) ? "未返回详细错误" : message.Trim();
        return new NotionApiException(
            $"Notion API 请求失败（HTTP {(int)statusCode}）：{detail}",
            (int)statusCode,
            code);
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout or
            (HttpStatusCode)529;
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            return retryAfter < TimeSpan.FromSeconds(10) ? retryAfter : TimeSpan.FromSeconds(10);
        }

        return TimeSpan.FromMilliseconds(500 * (1 << attempt));
    }

    private static NotionPage ParsePage(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.String)
        {
            throw new NotionApiException("Notion 返回的页面没有 ID。", 200);
        }

        var id = NotionIds.Normalize(idProperty.GetString()!, "Notion 页面 ID");
        var url = root.TryGetProperty("url", out var urlProperty) && urlProperty.ValueKind == JsonValueKind.String
            ? urlProperty.GetString()
            : null;
        return new NotionPage(id, string.IsNullOrWhiteSpace(url) ? NotionIds.PageUrl(id) : url!);
    }

    private static JsonArray Blocks(IReadOnlyList<JsonObject> children)
    {
        var array = new JsonArray();
        foreach (var child in children)
        {
            // Callers may reuse a rendered block list for retries or tests.
            // JsonNode instances cannot have two parents, so clone at the
            // transport boundary before placing them in a request body.
            array.Add(child.DeepClone());
        }

        return array;
    }

    private static JsonArray RichText(string text)
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = new JsonObject
                {
                    ["content"] = text.Length > 1_800 ? text[..1_800] : text,
                },
            },
        };
    }

    private static bool ContainsText(JsonElement element, string marker)
    {
        if (element.ValueKind == JsonValueKind.String &&
            element.GetString()?.Contains(marker, StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ContainsText(property.Value, marker))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsText(item, marker))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
