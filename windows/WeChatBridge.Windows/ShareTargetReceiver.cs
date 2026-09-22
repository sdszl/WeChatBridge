using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using WeChatBridge.Windows.Core;

namespace WeChatBridge.Windows;

/// <summary>
/// Receives ZIP files from the Windows Share Target contract.
/// The source item is copied before the share operation is completed because
/// the source application may revoke its temporary access afterwards.
/// </summary>
internal static class ShareTargetReceiver
{
    private const long MaxSharedFileBytes = WeChatArchiveReader.MaxExpandedBytes;

    public static ShareOperation? TryGetShareOperation()
    {
        try
        {
            var activation = AppInstance.GetActivatedEventArgs();
            if (activation is null || activation.Kind != ActivationKind.ShareTarget)
            {
                return null;
            }

            return (activation as IShareTargetActivatedEventArgs)?.ShareOperation;
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A normal unpackaged launch has no activation payload. Keep the
            // ordinary WPF launch path available until the MSIX is installed.
            return null;
        }
    }

    public static async Task<IReadOnlyList<string>> ReceiveAsync(ShareOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.ReportStarted();

        try
        {
            var data = operation.Data;
            if (!data.Contains(StandardDataFormats.StorageItems))
            {
                throw new InvalidDataException("微信没有提供可读取的文件分享内容。");
            }

            var items = await data.GetStorageItemsAsync();
            var files = items.OfType<IStorageFile>().ToArray();
            if (files.Length == 0)
            {
                throw new InvalidDataException("本次分享不包含文件。");
            }

            operation.ReportDataRetrieved();
            var received = new List<string>(files.Length);
            Directory.CreateDirectory(WindowsStorage.Inbox);

            foreach (var file in files)
            {
                var sourcePath = file.Path;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    throw new FileNotFoundException("分享文件暂时不可访问。", sourcePath);
                }

                if (!string.Equals(file.FileType, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"暂不接收此文件类型：{file.FileType}。请从微信分享 ZIP 归档。");
                }

                var attributes = File.GetAttributes(sourcePath);
                if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("分享文件不能是符号链接或重解析点。");
                }

                var length = new FileInfo(sourcePath).Length;
                if (length < 0 || length > MaxSharedFileBytes)
                {
                    throw new InvalidDataException("分享文件超过 1 GiB 安全上限。");
                }

                var destination = SafeNames.UniqueFileName(
                    WindowsStorage.Inbox,
                    SafeNames.FileName(file.Name, "微信归档.zip"));
                await CopyFileAsync(sourcePath, Path.Combine(WindowsStorage.Inbox, destination));
                received.Add(Path.Combine(WindowsStorage.Inbox, destination));
            }

            operation.ReportCompleted();
            return received;
        }
        catch (Exception exception)
        {
            try
            {
                operation.ReportError(exception.Message);
            }
            catch (Exception reportException) when (reportException is COMException or InvalidOperationException)
            {
                // Keep the original receive error; the share broker may have
                // already ended the operation by the time we report it.
            }

            throw;
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination);
    }
}
