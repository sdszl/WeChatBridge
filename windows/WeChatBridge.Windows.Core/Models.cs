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

public sealed record WindowsBatchRecord(
    Guid BatchId,
    DateTimeOffset CreatedAt,
    string SourcePath,
    string LocalSourcePath,
    string Status,
    string? NotePath,
    string? Error,
    int? MessageCount);

public sealed class ArchiveValidationException : Exception
{
    public ArchiveValidationException(string message) : base(message) { }
    public ArchiveValidationException(string message, Exception innerException) : base(message, innerException) { }
}
