using System.Windows;
using System.Windows.Input;
using NoticeMemo.App.ViewModels;
using NoticeMemo.Core.Models;

namespace NoticeMemo.App;

public partial class ImportDialog : Window
{
    private readonly ImportViewModel _viewModel;

    public ImportDialog(string initialText = "", string sourceGroup = "学院通知群")
    {
        InitializeComponent();
        _viewModel = new ImportViewModel(initialText, sourceGroup);
        DataContext = _viewModel;
    }

    public Memo? ResultMemo { get; private set; }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                _viewModel.RawText = Clipboard.GetText();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"读取剪贴板失败：{exception.Message}", "NoticeMemo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanSave)
        {
            return;
        }

        ResultMemo = _viewModel.BuildMemo();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
