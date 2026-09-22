using System.Text.Json;

namespace WeChatBridge.Windows.Core;

public static class WindowsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string Root => Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatBridge");

    public static string Inbox => Path.Combine(Root, "Inbox");
    private static string SettingsPath => Path.Combine(Root, "settings.json");

    public static WindowsSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new WindowsSettings();
            }

            return JsonSerializer.Deserialize<WindowsSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new WindowsSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WindowsSettings();
        }
    }

    public static void SaveSettings(WindowsSettings settings)
    {
        Directory.CreateDirectory(Root);
        WriteJsonAtomic(SettingsPath, settings);
    }

    public static string CreateBatchDirectory(Guid batchId)
    {
        var directory = Path.Combine(Inbox, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{batchId:N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void WriteBatchRecord(string batchDirectory, WindowsBatchRecord record)
    {
        WriteJsonAtomic(Path.Combine(batchDirectory, "manifest.json"), record);
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        var temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
