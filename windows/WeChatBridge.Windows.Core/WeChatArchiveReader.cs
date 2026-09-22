using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace WeChatBridge.Windows.Core;

/// Reads WeChat's ordinary UTF-8/deflated ZIP without using ExtractToDirectory.
/// Every entry is validated before a file is opened, and extraction is always
/// performed into a caller-owned directory with unique file creation.
public static class WeChatArchiveReader
{
    public const int MaxEntries = 1_000;
    public const long MaxExpandedBytes = 1_073_741_824;
    public const long MaxTranscriptBytes = 16_777_216;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Regex TranscriptHeader = new(
        @"(?m)^·([^\r\n]+)\n(\d{4}年\d{1,2}月\d{1,2}日 \d{2}:\d{2})\n",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ArchiveInspection Inspect(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (Directory.Exists(fullPath))
        {
            return InspectDirectory(fullPath);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要导入的文件或文件夹。", fullPath);
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArchiveValidationException("不接受符号链接或重解析点作为微信归档来源。" );
        }

        try
        {
            using var archive = ZipFile.OpenRead(fullPath);
            var entries = ValidateZip(archive);
            var transcript = FindTranscript(entries);
            return new ArchiveInspection(
                entries.Select(x => new ArchiveEntryInfo(x.Name, x.Length, x.IsDirectory)).ToArray(),
                transcript);
        }
        catch (ArchiveValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            throw new ArchiveValidationException("微信 ZIP 无法安全读取，文件已保留。", exception);
        }
    }

    public static void CopyAttachments(
        string sourcePath,
        string destinationDirectory,
        string? transcriptPath,
        IDictionary<string, string> attachmentNames,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        WithEntries(sourcePath, entries =>
        {
            foreach (var entry in entries.Where(x => !x.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (transcriptPath is not null &&
                    string.Equals(entry.Name, transcriptPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var leaf = entry.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "附件";
                var destinationName = SafeNames.UniqueFileName(destinationDirectory, leaf);
                var destinationPath = Path.Combine(destinationDirectory, destinationName);
                using var input = entry.Open();
                using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyStream(input, output, cancellationToken);

                attachmentNames[entry.Name] = destinationName;
                if (!attachmentNames.ContainsKey(leaf))
                {
                    attachmentNames[leaf] = destinationName;
                }
            }
        });
    }

    private static ArchiveInspection InspectDirectory(string root)
    {
        var entries = BuildDirectoryEntries(root);
        if (entries.Count == 0 || entries.All(x => x.Length == 0))
        {
            throw new ArchiveValidationException("微信导出的文件夹为空。" );
        }

        return new ArchiveInspection(
            entries.Select(x => new ArchiveEntryInfo(x.Name, x.Length, false)).ToArray(),
            FindTranscript(entries));
    }

    private static List<SourceEntry> BuildDirectoryEntries(string root)
    {
        var result = new List<SourceEntry>();
        var pending = new Stack<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);
        long total = 0;

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            EnsureNotReparsePoint(current);
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                EnsureNotReparsePoint(child);
                if (Directory.Exists(child))
                {
                    pending.Push(child);
                    continue;
                }

                var info = new FileInfo(child);
                if (result.Count + 1 > MaxEntries)
                {
                    throw new ArchiveValidationException("微信归档包含过多文件。" );
                }

                if (info.Length < 0 || total > MaxExpandedBytes - info.Length)
                {
                    throw new ArchiveValidationException("微信归档展开后超过安全大小限制。" );
                }

                total += info.Length;
                var relative = Path.GetRelativePath(root, child).Replace('\\', '/');
                ValidateEntryName(relative, isDirectory: false, seen, aliases);
                result.Add(new SourceEntry(relative, info.Length, false, null, child));
            }
        }

        return result;
    }

    private static List<SourceEntry> ValidateZip(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaxEntries)
        {
            throw new ArchiveValidationException("微信 ZIP 的文件数量不在支持范围内。" );
        }

        var entries = new List<SourceEntry>(archive.Entries.Count);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            var name = ValidateEntryName(entry.FullName, isDirectory, seen, aliases);
            var parts = name.Split('/');
            if (!isDirectory)
            {
                for (var index = 1; index < parts.Length; index++)
                {
                    var prefix = string.Join('/', parts.Take(index)).Normalize(NormalizationForm.FormC);
                    if (fileKeys.Contains(prefix.ToUpperInvariant()))
                    {
                        throw new ArchiveValidationException("ZIP 文件条目与父目录发生路径冲突。" );
                    }
                }

                fileKeys.Add(name.ToUpperInvariant());
            }
            long length;
            try
            {
                length = entry.Length;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                throw new ArchiveValidationException("ZIP 条目大小无法验证。", exception);
            }

            if (isDirectory && length != 0)
            {
                throw new ArchiveValidationException("ZIP 目录条目包含文件内容。" );
            }

            if (!isDirectory)
            {
                if (length < 0 || total > MaxExpandedBytes - length)
                {
                    throw new ArchiveValidationException("微信 ZIP 展开后超过安全大小限制。" );
                }

                total += length;
            }

            entries.Add(new SourceEntry(name, length, isDirectory, entry, null));
        }

        if (total == 0)
        {
            throw new ArchiveValidationException("微信 ZIP 不包含可读取的文件。" );
        }

        return entries;
    }

    private static string ValidateEntryName(
        string rawName,
        bool isDirectory,
        Dictionary<string, string> seen,
        Dictionary<string, string>? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(rawName) || rawName.Contains('\\') || rawName.Contains(':') || rawName.Contains('\0'))
        {
            throw new ArchiveValidationException("ZIP 条目名称不安全。" );
        }

        var name = isDirectory ? rawName[..^1] : rawName;
        if (name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArchiveValidationException("ZIP 条目路径不安全。" );
        }

        var parts = name.Split('/');
        if (parts.Any(part => part is "" or "." or ".." || part.Any(char.IsControl)))
        {
            throw new ArchiveValidationException("ZIP 条目路径包含非法分段。" );
        }

        var normalized = name.Normalize(NormalizationForm.FormC);
        var key = normalized.ToUpperInvariant();
        if (seen.TryGetValue(key, out var previous) && !string.Equals(previous, name, StringComparison.Ordinal))
        {
            throw new ArchiveValidationException("ZIP 条目存在大小写或 Unicode 重名。" );
        }

        if (seen.ContainsKey(key))
        {
            throw new ArchiveValidationException("ZIP 条目重复。" );
        }

        seen[key] = name;
        aliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index <= parts.Length; index++)
        {
            var prefix = string.Join('/', parts.Take(index)).Normalize(NormalizationForm.FormC);
            var prefixKey = prefix.ToUpperInvariant();
            if (aliases.TryGetValue(prefixKey, out var oldPrefix) && !string.Equals(oldPrefix, prefix, StringComparison.Ordinal))
            {
                throw new ArchiveValidationException("ZIP 父目录存在大小写或 Unicode 别名。" );
            }

            aliases[prefixKey] = prefix;
        }

        return normalized;
    }

    private static TranscriptInfo? FindTranscript(IReadOnlyList<SourceEntry> entries)
    {
        var candidates = new List<(SourceEntry Entry, string Body, IReadOnlyList<TranscriptRecord>? Records)>();
        foreach (var entry in entries.Where(x => !x.IsDirectory && x.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Length > MaxTranscriptBytes)
            {
                continue;
            }

            try
            {
                var body = ReadText(entry);
                candidates.Add((entry, body, ParseTranscript(body)));
            }
            catch (DecoderFallbackException)
            {
                // A non-UTF-8 text attachment is not a reason to reject the
                // whole archive; it simply is not a transcript candidate.
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var native = candidates.FirstOrDefault(x =>
            string.Equals(Path.GetFileName(x.Entry.Name.Replace('/', '\\')), "聊天记录.txt", StringComparison.OrdinalIgnoreCase));
        var selected = native.Entry is not null
            ? native
            : candidates.OrderByDescending(x => x.Records?.Count ?? 0).ThenByDescending(x => x.Entry.Length).First();
        return new TranscriptInfo(selected.Entry.Name, selected.Body, selected.Records);
    }

    private static IReadOnlyList<TranscriptRecord>? ParseTranscript(string body)
    {
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimStart('\uFEFF');
        var matches = TranscriptHeader.Matches(normalized);
        if (matches.Count == 0 || matches[0].Index != 0)
        {
            return null;
        }

        var records = new List<TranscriptRecord>(matches.Count);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!DateTime.TryParseExact(
                    match.Groups[2].Value,
                    "yyyy年M月d日 HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var date))
            {
                return null;
            }

            var start = match.Index + match.Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : normalized.Length;
            var text = normalized[start..end].Trim();
            records.Add(new TranscriptRecord(
                match.Groups[1].Value,
                new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local)),
                text));
        }

        return records;
    }

    private static string ReadText(SourceEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream(capacity: checked((int)Math.Min(entry.Length, MaxTranscriptBytes)));
        CopyStream(input, output, CancellationToken.None, MaxTranscriptBytes);
        return StrictUtf8.GetString(output.ToArray());
    }

    private static void WithEntries(string sourcePath, Action<IReadOnlyList<SourceEntry>> action)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (Directory.Exists(fullPath))
        {
            action(BuildDirectoryEntries(fullPath));
            return;
        }

        using var archive = ZipFile.OpenRead(fullPath);
        action(ValidateZip(archive));
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArchiveValidationException("微信归档不能包含符号链接或重解析点。" );
        }
    }

    private static void CopyStream(Stream input, Stream output, CancellationToken cancellationToken, long? limit = null)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (limit is not null && total > limit.Value)
            {
                throw new ArchiveValidationException("文本条目超过安全大小限制。" );
            }

            output.Write(buffer, 0, read);
        }
    }

    private sealed record SourceEntry(
        string Name,
        long Length,
        bool IsDirectory,
        ZipArchiveEntry? ZipEntry,
        string? FilePath)
    {
        public Stream Open() => ZipEntry?.Open() ?? new FileStream(FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
