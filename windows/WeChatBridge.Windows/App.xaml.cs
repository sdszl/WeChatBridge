using System.Windows;
using Windows.ApplicationModel.DataTransfer.ShareTarget;

namespace WeChatBridge.Windows;

public partial class App : System.Windows.Application
{
    private ShareOperation? _shareOperation;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        _shareOperation = ShareTargetReceiver.TryGetShareOperation();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (_shareOperation is not null)
        {
            _ = ReceiveShareAsync(window, _shareOperation);
        }
    }

    private static async Task ReceiveShareAsync(MainWindow window, ShareOperation operation)
    {
        try
        {
            var received = await ShareTargetReceiver.ReceiveAsync(operation);
            window.AddSharedSources(received);
            window.Activate();
        }
        catch (Exception exception)
        {
            window.ShowShareError(exception.Message);
        }
    }
}
