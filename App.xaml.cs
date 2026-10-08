using System.Windows;
using System.Windows.Media;
using PianoRoblox.Views;

namespace PianoRoblox;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            if (MainWindow is MainWindow window) { window.ViewModel.EmergencyRelease(); window.ViewModel.HandleError(args.Exception); }
            else MessageBox.Show(args.Exception.Message, "PianoRoblox");
            args.Handled = true;
        };
    }
    public static void ApplyTheme(bool dark)
    {
        var colors = dark ? new[] { "#10151F", "#1A2230", "#232E40", "#EFF4FB", "#AFBDCF" }
            : new[] { "#F1F4F8", "#FFFFFF", "#E0E7F0", "#172238", "#52627A" };
        string[] names = ["BackgroundBrush", "PanelBrush", "FieldBrush", "TextBrush", "MutedBrush"];
        for (int i = 0; i < names.Length; i++) Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}

