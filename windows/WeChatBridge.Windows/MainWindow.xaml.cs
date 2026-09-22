using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WeChatBridge.Windows.Core;

namespace WeChatBridge.Windows;

public partial class MainWindow : Window
{
    private readonly List<string> _sources = [];
    private WindowsSettings _settings;
    private NotionSettings _notionSettings;
    private CancellationTokenSource? _exportCancellation;
    private string? _lastNotionUrl;
    private string? _lastCodexWorkspace;

    public MainWindow()
    {
        InitializeComponent();
        _settings = WindowsStorage.LoadSettings();
        try
        {
            _notionSettings = WindowsStorage.LoadNotionSettings();
        }
        catch (NotionConfigurationException exception)
        {
            _notionSettings = new NotionSettings(string.Empty, null);
            StatusText.Text = exception.Message;
        }

        VaultTextBox.Text = _settings.ObsidianVaultPath;
        SubfolderTextBox.Text = _settings.ObsidianSubfolder;
        NotionPageIdTextBox.Text = _notionSettings.ParentPageId;
        NotionTokenStatusText.Text = _notionSettings.HasToken
            ? "已保存 Token；留空即可复用。"
            : "尚未保存 Token。";
        if (string.IsNullOrWhiteSpace(StatusText.Text))
        {
            StatusText.Text = "等待选择微信导出的 ZIP 或文件夹。";
        }
    }

    private void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "微信归档 ZIP (*.zip)|*.zip|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Title = "选择微信导出的 ZIP",
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddSources(dialog.FileNames);
        }
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择包含微信导出文件的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            AddSources([dialog.SelectedPath]);
        }
    }

    private void ChooseVault_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择 Obsidian 知识库文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(VaultTextBox.Text) ? VaultTextBox.Text : string.Empty,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            VaultTextBox.Text = dialog.SelectedPath;
            SaveSettings();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _sources.Clear();
        InputList.Items.Clear();
        StatusText.Text = "已清空待处理列表。";
    }

    public void AddSharedSources(IEnumerable<string> paths)
    {
        AddSources(paths);
        StatusText.Text = _sources.Count == 0
            ? "微信分享没有收到可处理的 ZIP。"
            : $"已自动收到 {_sources.Count} 个微信归档；现在可以导入 Notion 或生成 Markdown。";
    }

    public void ShowShareError(string message)
    {
        StatusText.Text = $"接收微信分享失败：{message}";
    }

    private async void CodexAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_sources.Count == 0)
        {
            StatusText.Text = "请先选择或接收微信 ZIP。";
            return;
        }

        SetBusy(true);
        _exportCancellation = new CancellationTokenSource();
        var cancellationToken = _exportCancellation.Token;
        var completed = 0;
        var failures = 0;

        try
        {
            foreach (var source in _sources.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusText.Text = $"正在准备 Codex 工作区：{Path.GetFileName(source)}";
                try
                {
                    var workspace = await Task.Run(
                        () => CodexWorkspaceWorkflow.Prepare(source, ChatNameTextBox.Text, cancellationToken),
                        cancellationToken);
                    _lastCodexWorkspace = workspace.WorkspacePath;
                    OpenCodexButton.IsEnabled = true;
                    StatusText.Text = $"正在让 Codex 分析：{Path.GetFileName(source)}";
                    var result = await CodexLauncher.AnalyzeAsync(workspace, cancellationToken);
                    completed++;
                    _lastCodexWorkspace = result.WorkspacePath;
                    StatusText.Text = $"Codex 分析完成：{result.ResultPath}";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;
                    StatusText.Text = $"Codex 分析失败：{Path.GetFileName(source)} · {exception.Message}";
                }

                ExportProgress.Value = 100d * (completed + failures) / _sources.Count;
            }

            if (failures == 0)
            {
                StatusText.Text = $"全部完成：{completed} 个归档已由 Codex 分析。可打开 Codex 工作区继续追问。";
            }
            else
            {
                StatusText.Text = $"处理结束：成功 {completed} 个，失败 {failures} 个。已生成的本地工作区仍保留。";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消。已生成的 Codex 工作区和原始归档均已保留。";
        }
        finally
        {
            _exportCancellation.Dispose();
            _exportCancellation = null;
            SetBusy(false);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_sources.Count == 0)
        {
            StatusText.Text = "请先选择 ZIP 文件或文件夹。";
            return;
        }

        var vault = VaultTextBox.Text.Trim();
        if (!Directory.Exists(vault))
        {
            StatusText.Text = "请先选择一个已存在的 Obsidian 知识库文件夹。";
            return;
        }

        SaveSettings();
        SetBusy(true);
        _exportCancellation = new CancellationTokenSource();
        var token = _exportCancellation.Token;
        var completed = 0;
        var failures = 0;

        try
        {
            foreach (var source in _sources.ToArray())
            {
                token.ThrowIfCancellationRequested();
                StatusText.Text = $"正在处理：{Path.GetFileName(source)}";
                try
                {
                    var request = new ExportRequest(
                        source,
                        vault,
                        ChatNameTextBox.Text,
                        string.Empty,
                        string.IsNullOrWhiteSpace(SubfolderTextBox.Text) ? "微信流" : SubfolderTextBox.Text);
                    var result = await Task.Run(() => WindowsArchiveWorkflow.Export(request, token), token);
                    completed++;
                    StatusText.Text = $"已完成 {completed}/{_sources.Count}：{result.NotePath}";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;
                    StatusText.Text = $"处理失败：{Path.GetFileName(source)} · {exception.Message}";
                }

                ExportProgress.Value = 100d * (completed + failures) / _sources.Count;
            }

            StatusText.Text = failures == 0
                ? $"全部完成：{completed} 个归档已生成 Markdown。"
                : $"处理结束：成功 {completed} 个，失败 {failures} 个。失败文件仍保留在本地收件箱。";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消。已完成的归档和原始文件均已保留。";
        }
        finally
        {
            _exportCancellation.Dispose();
            _exportCancellation = null;
            SetBusy(false);
        }
    }

    private async void ImportNotion_Click(object sender, RoutedEventArgs e)
    {
        if (_sources.Count == 0)
        {
            StatusText.Text = "请先选择 ZIP 文件或文件夹。";
            return;
        }

        string parentPageId;
        try
        {
            parentPageId = NotionIds.Normalize(NotionPageIdTextBox.Text);
        }
        catch (NotionConfigurationException exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        var token = NotionTokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _notionSettings.Token ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            StatusText.Text = "请填写 Notion Integration Token；保存后下次可留空复用。";
            return;
        }

        try
        {
            WindowsStorage.SaveNotionSettings(parentPageId, token);
            _notionSettings = new NotionSettings(parentPageId, token);
            NotionPageIdTextBox.Text = parentPageId;
            NotionTokenBox.Clear();
            NotionTokenStatusText.Text = "Token 已使用当前 Windows 用户的 DPAPI 加密保存。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotionConfigurationException)
        {
            StatusText.Text = $"Notion 配置保存失败：{exception.Message}";
            return;
        }

        SetBusy(true);
        _exportCancellation = new CancellationTokenSource();
        var cancellationToken = _exportCancellation.Token;
        var completed = 0;
        var failures = 0;

        try
        {
            using var notionClient = new NotionClient(token);
            foreach (var source in _sources.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusText.Text = $"正在导入 Notion：{Path.GetFileName(source)}";
                try
                {
                    var request = new NotionImportRequest(
                        source,
                        parentPageId,
                        ChatNameTextBox.Text,
                        string.Empty);
                    var result = await NotionArchiveWorkflow.ImportAsync(request, notionClient, cancellationToken);
                    completed++;
                    _lastNotionUrl = result.PageUrl;
                    OpenNotionButton.IsEnabled = true;
                    StatusText.Text = $"已完成 {completed}/{_sources.Count}：{result.PageUrl}";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;
                    StatusText.Text = $"Notion 导入失败：{Path.GetFileName(source)} · {exception.Message}";
                }

                ExportProgress.Value = 100d * (completed + failures) / _sources.Count;
            }

            StatusText.Text = failures == 0
                ? $"全部完成：{completed} 个归档已导入 Notion，并已回读验证。"
                : $"处理结束：成功 {completed} 个，失败 {failures} 个。失败归档仍保留在本地收件箱。";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消。已完成的 Notion 页面和本地原始归档均已保留。";
        }
        finally
        {
            _exportCancellation.Dispose();
            _exportCancellation = null;
            SetBusy(false);
        }
    }

    private void OpenVault_Click(object sender, RoutedEventArgs e)
    {
        var path = Directory.Exists(VaultTextBox.Text) ? VaultTextBox.Text : WindowsStorage.Root;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private void OpenNotion_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_lastNotionUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("notion.so", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".notion.so", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("notion.site", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".notion.site", StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "最近的 Notion 页面链接无效。";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });
    }

    private void OpenCodex_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastCodexWorkspace) || !Directory.Exists(_lastCodexWorkspace))
        {
            StatusText.Text = "还没有可打开的 Codex 工作区。";
            return;
        }

        try
        {
            CodexLauncher.OpenWorkspace(_lastCodexWorkspace);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"打开 Codex 失败：{exception.Message}";
        }
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            AddSources(paths);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _exportCancellation?.Cancel();
        base.OnClosed(e);
    }

    private void AddSources(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path) ||
                (File.Exists(path) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)))
            {
                var full = Path.GetFullPath(path);
                if (!_sources.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    _sources.Add(full);
                    InputList.Items.Add(full);
                }
            }
        }

        StatusText.Text = _sources.Count == 0
            ? "未添加可处理的 ZIP 或文件夹。"
            : $"已选择 {_sources.Count} 个归档，点击“导入并生成 Markdown”。";
    }

    private void SaveSettings()
    {
        _settings.ObsidianVaultPath = VaultTextBox.Text.Trim();
        _settings.ObsidianSubfolder = string.IsNullOrWhiteSpace(SubfolderTextBox.Text) ? "微信流" : SubfolderTextBox.Text.Trim();
        try
        {
            WindowsStorage.SaveSettings(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"设置未能保存，但本次操作仍可继续：{exception.Message}";
        }
    }

    private void SetBusy(bool busy)
    {
        SelectFilesButton.IsEnabled = !busy;
        SelectFolderButton.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        OpenButton.IsEnabled = !busy;
        ImportNotionButton.IsEnabled = !busy;
        CodexAnalyzeButton.IsEnabled = !busy;
        OpenNotionButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_lastNotionUrl);
        OpenCodexButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_lastCodexWorkspace);
        NotionPageIdTextBox.IsEnabled = !busy;
        NotionTokenBox.IsEnabled = !busy;
        VaultTextBox.IsEnabled = !busy;
        SubfolderTextBox.IsEnabled = !busy;
        ChatNameTextBox.IsEnabled = !busy;
        ExportProgress.IsIndeterminate = busy;
        if (!busy)
        {
            ExportProgress.IsIndeterminate = false;
        }
    }
}
