using WeChatBridge.Windows.Core;
using Xunit;

namespace WeChatBridge.Windows.Tests;

public sealed class ConfigurationRegistryTests
{
    [Fact]
    public void NotionConfigurationUsesTheDocumentedLocalFile()
    {
        Assert.Equal("notion.config.json", Path.GetFileName(WindowsStorage.NotionConfigPath));
        Assert.EndsWith("WeChatBridge", WindowsStorage.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotionTokenRoundTripsThroughWindowsDpapi()
    {
        const string token = "secret_test_token_for_dpapi";

        var protectedValue = WindowsSecretStore.ProtectToBase64(token);
        var restored = WindowsSecretStore.UnprotectFromBase64(protectedValue);

        Assert.NotEqual(token, protectedValue);
        Assert.Equal(token, restored);
    }
}
