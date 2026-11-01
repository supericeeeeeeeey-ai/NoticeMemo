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

    public IReadOnlyList<ReminderLeadOption> ReminderLeadOptions { get; } =
    [
        new(0, "截止时提醒"),
        new(10, "提前 10 分钟"),
        new(30, "提前 30 分钟"),
        new(60, "提前 1 小时"),
        new(120, "提前 2 小时"),
        new(1440, "提前 1 天")
    ];

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

public sealed record ReminderLeadOption(int Minutes, string Label);
