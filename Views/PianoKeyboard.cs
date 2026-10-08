using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;

namespace PianoRoblox.Views;

public sealed class PianoKeyboard : UserControl
{
    public static readonly DependencyProperty ActiveNotesProperty = DependencyProperty.Register(nameof(ActiveNotes), typeof(HashSet<int>), typeof(PianoKeyboard), new PropertyMetadata(null, (d, _) => ((PianoKeyboard)d).Refresh()));
    public HashSet<int>? ActiveNotes { get => (HashSet<int>?)GetValue(ActiveNotesProperty); set => SetValue(ActiveNotesProperty, value); }
    public bool IsInteractive { get; set; }
    public event EventHandler<int>? NoteClicked;
    private readonly Dictionary<int, (Button Button, PianoNote Note)> buttons = new();
    private static readonly Brush ActiveBrush = Frozen(97, 219, 185), BlackBrush = Frozen(28, 34, 46), WhiteBrush = Frozen(232, 238, 245);
    private static Brush Frozen(byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
    public PianoKeyboard()
    {
        var canvas = new Canvas { Width = 36 * 30, Height = 155 };
        int whiteIndex = 0;
        foreach (var note in new PianoMappingService().All)
        {
            bool black = note.IsBlack;
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            foreach (string line in new[] { note.NoteName, note.MidiNote.ToString(), note.KeyboardKey.ToString() })
                content.Children.Add(new TextBlock { Text = line, FontSize = black ? 9 : 10, Foreground = black ? Brushes.White : Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button
            {
                Content = content, Width = black ? 20 : 29, Height = black ? 97 : 150,
                Padding = new Thickness(0, black ? 42 : 94, 0, 4), Margin = new Thickness(0),
                ToolTip = $"{note.NoteName} • MIDI {note.MidiNote} • {(note.RequiresShift ? "Shift + " : "")}{(char)note.VirtualKey}"
            };
            Canvas.SetLeft(button, black ? whiteIndex * 30 - 10 : whiteIndex++ * 30);
            Panel.SetZIndex(button, black ? 1 : 0);
            button.Click += (_, _) => { if (IsInteractive) NoteClicked?.Invoke(this, note.MidiNote); };
            canvas.Children.Add(button); buttons[note.MidiNote] = (button, note);
        }
        Content = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Refresh();
    }
    private void Refresh()
    {
        foreach (var pair in buttons)
        {
            var brush = ActiveNotes?.Contains(pair.Key) == true ? ActiveBrush : pair.Value.Note.IsBlack ? BlackBrush : WhiteBrush;
            if (pair.Value.Button.Background != brush) pair.Value.Button.Background = brush;
        }
    }
}

