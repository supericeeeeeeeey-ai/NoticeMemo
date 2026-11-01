using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using NoticeMemo.App.Services;
using NoticeMemo.App.ViewModels;

namespace NoticeMemo.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ClipboardMonitor _clipboardMonitor;
    private bool _initialized;
    private bool _exitRequested;

    public MainWindow(MainViewModel viewModel, ClipboardMonitor clipboardMonitor)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _clipboardMonitor = clipboardMonitor;
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? HiddenToTray;

    public bool CloseToTrayEnabled { get; set; } = true;

    public string DefaultSourceGroup { get; set; } = "学院通知群";

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _viewModel.Initialize();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested || !CloseToTrayEnabled)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            ImportManual_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V)
        {
            ImportClipboard_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
        {
            Export_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void ImportClipboard_Click(object sender, RoutedEventArgs e)
    {
        ImportClipboardNow();
    }

    private void ImportManual_Click(object sender, RoutedEventArgs e)
    {
        OpenImport("");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var result = _viewModel.ExportAll();
        if (result.Count == 0)
        {
            MessageBox.Show(this, "还没有可以导出的备忘录。", "NoticeMemo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"已导出 {result.Count} 条 Markdown 到：\n{result.DirectoryPath}\n\n是否打开导出文件夹？",
            "导出完成",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (choice == MessageBoxResult.Yes)
        {
            OpenFolder(result.DirectoryPath);
        }
    }

    private void OpenExportFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_viewModel.ExportDirectory);
        OpenFolder(_viewModel.ExportDirectory);
    }

    private void ToggleDone_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleDone();
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ActivateSelected();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var markdown = _viewModel.GetSelectedMarkdown();
        if (markdown is null)
        {
            return;
        }

        try
        {
            _clipboardMonitor.SuppressNextChange();
            Clipboard.SetText(markdown);
            _viewModel.StatusMessage = "已复制 Markdown。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"复制失败：{exception.Message}", "NoticeMemo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedMemo is not { } memo)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"确定删除「{memo.Title}」吗？此操作无法撤销。",
            "删除备忘录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (choice == MessageBoxResult.Yes)
        {
            _viewModel.DeleteSelected();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }

    public void ImportClipboardNow()
    {
        ShowAndActivate();
        OpenImport(GetClipboardText());
    }

    public bool ImportPendingText(string text, string sourceGroup)
    {
        ShowAndActivate();
        return OpenImport(text, sourceGroup);
    }

    public void PrepareForExit()
    {
        _exitRequested = true;
    }

    private bool OpenImport(string initialText, string? sourceGroup = null)
    {
        var dialog = new ImportDialog(
            initialText,
            string.IsNullOrWhiteSpace(sourceGroup) ? DefaultSourceGroup : sourceGroup) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultMemo is not { } memo)
        {
            return false;
        }

        var result = _viewModel.AddImportedMemo(memo);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "NoticeMemo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return result.Success;
    }

    private static string GetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"无法打开文件夹：{exception.Message}", "NoticeMemo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
