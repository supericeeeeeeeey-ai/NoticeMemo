using System.Windows;
using System.Windows.Input;
using NoticeMemo.Core.Settings;

namespace NoticeMemo.App;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _draft;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _draft = settings.Clone();
        DataContext = _draft;
    }

    public AppSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _draft.Normalize();
        Result = _draft.Clone();
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
