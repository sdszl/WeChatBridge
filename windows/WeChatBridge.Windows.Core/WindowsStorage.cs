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
    public static string NotionConfigPath => Path.Combine(Root, "notion.config.json");
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

    public static NotionSettings LoadNotionSettings()
    {
        if (!File.Exists(NotionConfigPath))
        {
            return new NotionSettings(string.Empty, null);
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredNotionSettings>(
                File.ReadAllText(NotionConfigPath),
                JsonOptions);
            if (stored is null)
            {
                return new NotionSettings(string.Empty, null);
            }

            var token = string.IsNullOrWhiteSpace(stored.ProtectedToken)
                ? null
                : WindowsSecretStore.UnprotectFromBase64(stored.ProtectedToken);
            return new NotionSettings(stored.ParentPageId?.Trim() ?? string.Empty, token);
        }
        catch (NotionConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new NotionConfigurationException(
                $"无法读取本机 Notion 配置，请检查文件：{NotionConfigPath}",
                exception);
        }
    }

    public static void SaveNotionSettings(string parentPageId, string token)
    {
        var stored = new StoredNotionSettings
        {
            ParentPageId = parentPageId.Trim(),
            ProtectedToken = WindowsSecretStore.ProtectToBase64(token),
        };
        Directory.CreateDirectory(Root);
        WriteJsonAtomic(NotionConfigPath, stored);
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

    private sealed class StoredNotionSettings
    {
        public string ParentPageId { get; set; } = string.Empty;
        public string ProtectedToken { get; set; } = string.Empty;
    }
}
