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
    private CancellationTokenSource? _exportCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _settings = WindowsStorage.LoadSettings();
        VaultTextBox.Text = _settings.ObsidianVaultPath;
        SubfolderTextBox.Text = _settings.ObsidianSubfolder;
        StatusText.Text = "等待选择微信导出的 ZIP 或文件夹。";
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
