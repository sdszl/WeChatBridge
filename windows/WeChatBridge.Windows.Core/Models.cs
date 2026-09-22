namespace WeChatBridge.Windows.Core;

public sealed record TranscriptRecord(string Sender, DateTimeOffset Date, string Text);

public sealed record TranscriptInfo(
    string Path,
    string Body,
    IReadOnlyList<TranscriptRecord>? Records)
{
    public int? MessageCount => Records?.Count;
    public DateTimeOffset? Start => Records is { Count: > 0 } ? Records.Min(x => x.Date) : null;
    public DateTimeOffset? End => Records is { Count: > 0 } ? Records.Max(x => x.Date) : null;
}

public sealed record ArchiveEntryInfo(string Name, long Length, bool IsDirectory);

public sealed record ArchiveInspection(
    IReadOnlyList<ArchiveEntryInfo> Entries,
    TranscriptInfo? Transcript)
{
    public long ExpandedBytes => Entries.Where(x => !x.IsDirectory).Sum(x => x.Length);
}

public sealed class WindowsSettings
{
    public string ObsidianVaultPath { get; set; } = string.Empty;
    public string ObsidianSubfolder { get; set; } = "微信流";
}

public sealed record NotionSettings(string ParentPageId, string? Token)
{
    public bool HasToken => !string.IsNullOrWhiteSpace(Token);
}

public sealed record ExportRequest(
    string SourcePath,
    string VaultPath,
    string ChatName,
    string SceneName,
    string Subfolder);

public sealed record ExportResult(
    Guid BatchId,
    string NotePath,
    string LocalBatchPath,
    string ArchivePath,
    int? MessageCount);

public sealed record NotionImportRequest(
    string SourcePath,
    string ParentPageId,
    string ChatName,
    string SceneName);

public sealed record NotionImportResult(
    Guid BatchId,
    string PageId,
    string PageUrl,
    string LocalBatchPath,
    int? MessageCount,
    int BlockCount);

public sealed record WindowsBatchRecord(
    Guid BatchId,
    DateTimeOffset CreatedAt,
    string SourcePath,
    string LocalSourcePath,
    string Status,
    string? NotePath,
    string? Error,
    int? MessageCount,
    string? NotionPageId = null,
    string? NotionPageUrl = null,
    int? NotionBlockCount = null);

public sealed class ArchiveValidationException : Exception
{
    public ArchiveValidationException(string message) : base(message) { }
    public ArchiveValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class NotionConfigurationException : Exception
{
    public NotionConfigurationException(string message) : base(message) { }
    public NotionConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class NotionApiException : Exception
{
    public NotionApiException(string message, int statusCode, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
}
