namespace WeChatBridge.Windows.Core;

public static class WindowsArchiveWorkflow
{
    public static ExportResult Export(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(request.SourcePath);
        var vault = Path.GetFullPath(request.VaultPath);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new FileNotFoundException("找不到要导入的微信归档。", source);
        }

        if (!Directory.Exists(vault))
        {
            throw new DirectoryNotFoundException("Obsidian 知识库文件夹不存在。" + Environment.NewLine + vault);
        }

        var inspection = WeChatArchiveReader.Inspect(source);
        var batchId = Guid.NewGuid();
        var batchDirectory = WindowsStorage.CreateBatchDirectory(batchId);
        var localSource = Path.Combine(batchDirectory, "source");
        var record = new WindowsBatchRecord(
            batchId,
            DateTimeOffset.UtcNow,
            source,
            localSource,
            "received",
            null,
            null,
            inspection.Transcript?.MessageCount);

        WindowsStorage.WriteBatchRecord(batchDirectory, record);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopySource(source, localSource, cancellationToken);
            record = record with { Status = "stored" };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);

            var root = Path.Combine(vault, SafeNames.FolderName(request.Subfolder, "微信流"));
            var attachments = Path.Combine(root, "附件");
            Directory.CreateDirectory(attachments);

            var archiveName = StoreOriginal(source, attachments, cancellationToken);
            var attachmentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(source))
            {
                WeChatArchiveReader.CopyAttachments(
                    source,
                    attachments,
                    inspection.Transcript?.Path,
                    attachmentNames,
                    cancellationToken);
            }
            else
            {
                CopyDirectoryContents(source, Path.Combine(attachments, archiveName), cancellationToken);
            }

            var title = MarkdownRenderer.Title(request.ChatName, inspection.Transcript, archiveName);
            var noteName = SafeNames.UniqueFileName(root, title + ".md");
            var notePath = Path.Combine(root, noteName);
            var markdown = MarkdownRenderer.Render(
                title,
                request.ChatName,
                request.SceneName,
                DateTimeOffset.Now,
                inspection.Transcript,
                archiveName,
                attachmentNames);
            File.WriteAllText(notePath, markdown, new System.Text.UTF8Encoding(false));

            record = record with
            {
                Status = "exported",
                NotePath = notePath,
            };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);
            return new ExportResult(
                batchId,
                notePath,
                batchDirectory,
                Path.Combine(attachments, archiveName),
                inspection.Transcript?.MessageCount);
        }
        catch (Exception exception)
        {
            record = record with
            {
                Status = "failed",
                Error = exception.Message,
            };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);
            throw;
        }
    }

    private static string StoreOriginal(string source, string attachments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposed = SafeNames.FileName(Path.GetFileName(source), "微信归档.zip");
        if (File.Exists(source))
        {
            var name = SafeNames.UniqueFileName(attachments, proposed);
            File.Copy(source, Path.Combine(attachments, name));
            return name;
        }

        var directoryName = SafeNames.UniqueDirectoryName(attachments, Path.GetFileName(source));
        Directory.CreateDirectory(Path.Combine(attachments, directoryName));
        return directoryName;
    }

    private static void CopySource(string source, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
            return;
        }

        Directory.CreateDirectory(destination);
        CopyDirectoryContents(source, destination, cancellationToken);
    }

    private static void CopyDirectoryContents(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
