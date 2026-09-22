namespace WeChatBridge.Windows.Core;

public static class NotionArchiveWorkflow
{
    public static async Task<NotionImportResult> ImportAsync(
        NotionImportRequest request,
        INotionClient notionClient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notionClient);

        var source = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            throw new FileNotFoundException("找不到要导入的微信归档。", source);
        }

        var parentPageId = NotionIds.Normalize(request.ParentPageId);
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
        NotionPage? page = null;
        NotionBlockDocument? document = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopySource(source, localSource, cancellationToken);
            record = record with { Status = "stored" };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);

            var archiveName = Path.GetFileName(source);
            var title = MarkdownRenderer.Title(request.ChatName, inspection.Transcript, archiveName);
            var renderedDocument = NotionBlockRenderer.Render(
                title,
                request.ChatName,
                request.SceneName,
                DateTimeOffset.Now,
                inspection.Transcript,
                archiveName,
                batchId);
            document = renderedDocument;

            var firstBatch = renderedDocument.Blocks.Take(NotionClient.MaxBlocksPerRequest).ToArray();
            page = await notionClient.CreatePageAsync(
                parentPageId,
                renderedDocument.Title,
                firstBatch,
                cancellationToken);
            record = record with
            {
                Status = "notion-created",
                NotionPageId = page.Id,
                NotionPageUrl = page.Url,
                NotionBlockCount = renderedDocument.Blocks.Count,
            };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);

            foreach (var batch in renderedDocument.Blocks.Skip(firstBatch.Length).Chunk(NotionClient.MaxBlocksPerRequest))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await notionClient.AppendChildrenAsync(page.Id, batch, cancellationToken);
            }

            var verification = await notionClient.VerifyPageAsync(
                page.Id,
                renderedDocument.VerificationMarker,
                cancellationToken);
            if (!verification.MarkerFound)
            {
                throw new NotionApiException(
                    "Notion 页面已创建，但回读时没有找到本次导入标记，已停止报告为成功。",
                    200);
            }

            record = record with { Status = "notion-imported" };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);
            return new NotionImportResult(
                batchId,
                page.Id,
                page.Url,
                batchDirectory,
                inspection.Transcript?.MessageCount,
                renderedDocument.Blocks.Count);
        }
        catch (Exception exception)
        {
            record = record with
            {
                Status = "failed",
                Error = exception.Message,
                NotionPageId = page?.Id,
                NotionPageUrl = page?.Url,
                NotionBlockCount = document?.Blocks.Count,
            };
            WindowsStorage.WriteBatchRecord(batchDirectory, record);
            throw;
        }
    }

    private static void CopySource(string source, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArchiveValidationException("微信归档不能包含符号链接或重解析点。");
            }

            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
