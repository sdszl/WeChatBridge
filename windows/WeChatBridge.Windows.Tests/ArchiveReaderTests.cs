using System.IO.Compression;
using System.Text;
using WeChatBridge.Windows.Core;
using Xunit;

namespace WeChatBridge.Windows.Tests;

public sealed class ArchiveReaderTests
{
    [Fact]
    public void ReadsNativeTranscriptAndCountsMessages()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = Path.Combine(root, "聊天.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var transcript = archive.CreateEntry("聊天记录.txt");
                using (var writer = new StreamWriter(transcript.Open(), new UTF8Encoding(false)))
                {
                    writer.Write("·甲\n2026年9月5日 08:05\n第一条\n\n·乙\n2026年9月5日 08:06\n第二条\n");
                }

                var image = archive.CreateEntry("images/photo.png");
                using var imageStream = image.Open();
                imageStream.Write([1, 2, 3, 4]);
            }

            var inspection = WeChatArchiveReader.Inspect(archivePath);

            Assert.Equal(2, inspection.Transcript?.MessageCount);
            Assert.Equal("聊天记录.txt", inspection.Transcript?.Path);
            Assert.Contains(inspection.Entries, x => x.Name == "images/photo.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsTraversalEntryBeforeOpeningFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = Path.Combine(root, "bad.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../outside.txt");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("not safe");
            }

            Assert.Throws<ArchiveValidationException>(() => WeChatArchiveReader.Inspect(archivePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wechatbridge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
