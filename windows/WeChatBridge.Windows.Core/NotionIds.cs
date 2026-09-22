using System.Text.RegularExpressions;

namespace WeChatBridge.Windows.Core;

public static partial class NotionIds
{
    public static string Normalize(string rawValue, string fieldName = "Notion 页面 ID")
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new NotionConfigurationException($"请填写{fieldName}。");
        }

        var match = PageIdPattern().Match(rawValue.Trim());
        if (!match.Success)
        {
            throw new NotionConfigurationException(
                $"{fieldName}格式不正确。请粘贴 Notion 页面链接或 32 位页面 ID。");
        }

        return match.Value;
    }

    public static bool TryNormalize(string? rawValue, out string pageId)
    {
        try
        {
            pageId = Normalize(rawValue ?? string.Empty);
            return true;
        }
        catch (NotionConfigurationException)
        {
            pageId = string.Empty;
            return false;
        }
    }

    public static string PageUrl(string pageId)
    {
        return "https://www.notion.so/" + Normalize(pageId).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"(?<![0-9a-fA-F])(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32})(?![0-9a-fA-F])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PageIdPattern();
}
