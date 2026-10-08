using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using PianoRoblox.Services;
using PianoRoblox.ViewModels;

namespace PianoRoblox.Views;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private HotkeyService? hotkeys;
    private bool closing;
    public MainWindow() : this(new MainViewModel()) { }
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent(); DataContext = ViewModel;
        // ProcessExit can run on a non-UI thread: use the captured service owner,
        // never Application.MainWindow (which requires Dispatcher access).
        var cleanupOwner = ViewModel;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cleanupOwner.EmergencyRelease();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            hotkeys = new(new WindowInteropHelper(this).Handle, ViewModel.Settings.Hotkeys, ViewModel.Log);
            hotkeys.Pressed += ViewModel.HandleHotkey;
        };
        Closing += OnClosing;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true; IsEnabled = false;
        try { hotkeys?.Dispose(); await ViewModel.ShutdownAsync(); }
        finally { closing = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
    private async void PianoNoteClicked(object? sender, int midi)
    {
        try { await ViewModel.TestAsync([midi], true); }
        catch (Exception ex) { ViewModel.HandleError(ex); }
    }
    private void SongDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement((DataGrid)sender, source) is DataGridRow)
            ViewModel.Library.PlaySelectedCommand.Execute(null);
    }
}

