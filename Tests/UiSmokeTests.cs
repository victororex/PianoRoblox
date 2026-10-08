using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PianoRoblox.Core.Services;
using PianoRoblox.Core.Models;
using PianoRoblox.ViewModels;
using PianoRoblox.Views;
using Xunit;

namespace PianoRoblox.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public async Task AllTabsLoadBindAndRenderWithoutOpeningDesktopWindow()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var output = Environment.GetEnvironmentVariable("PIANOROBLOX_UI_TEST_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "PianoRobloxTests", "renders");
            Directory.CreateDirectory(output);
            void Stage(string stage) => File.AppendAllText(Path.Combine(output, "stages.log"), stage + Environment.NewLine);
            try
            {
                Stage("Application resources");
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/PianoRoblox;component/Views/Theme.xaml")
                });
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var directory = Path.Combine(Path.GetTempPath(), "PianoRobloxTests", Guid.NewGuid().ToString());
                var library = new SongLibraryService(directory);
                var songs = new[] { new Song { Title = "Primeira", Artist = "Teste", Kind = "JSON", FileName = "one.json" },
                    new Song { Title = "Segunda", Artist = "Teste", Kind = "JSON", FileName = "two.json" } };
                var json = new SequenceJsonService(new());
                Pump(json.ExportAsync(library.GetSongPath(songs[0]), new("Primeira", 200, 120, [new(0, [60])])));
                Pump(json.ExportAsync(library.GetSongPath(songs[1]), new("Segunda", 200, 1800, [new(0, [62]), new(1500, [64])])));
                Pump(library.SaveAsync(songs));
                var playlist = new Playlist { Name = "Testes V2", SongIds = songs.Select(s => s.Id).ToList() };
                Pump(new PlaylistService(directory).SaveAsync([playlist]));
                var keyboard = new FakePlayer(); var failures = new List<Exception>();
                var vm = new MainViewModel(library, keyboard, () => true, failures.Add);
                Pump(vm.InitializeAsync());
                vm.Settings.CountdownSeconds = 0; vm.Library.SelectedPlaylist = vm.Library.Playlists.Single(); vm.Library.SelectedSong = vm.Songs[0];
                string midiPath = Path.Combine(directory, "complex.mid"); OptimizationV2Tests.ComplexMidi().Write(midiPath);
                File.Copy(midiPath, Path.Combine(output, "complex-synthetic.mid"), true);
                Pump(vm.MidiImport.LoadAsync(midiPath));
                Assert.False(vm.MidiImport.Tracks.Last().IsSelected);
                vm.MidiImport.Preset = OptimizationPreset.Original;
                vm.MidiImport.Preset = OptimizationPreset.Balanced;
                vm.MidiImport.Preset = OptimizationPreset.Performance;
                vm.MidiImport.Tracks[1].IsSelected = false;
                Pump(vm.MidiImport.EnsureReadyAsync());
                Assert.Equal(OptimizationPreset.Performance, vm.MidiImport.Result!.EffectivePreset);
                Assert.DoesNotContain(vm.MidiImport.Result.Notes, note => note.TrackIndex == 1);
                vm.MidiImport.Tracks[1].IsSelected = true; Pump(vm.MidiImport.EnsureReadyAsync());
                vm.MidiImport.BenchmarkCommand.Execute(null);
                Pump(Until(() => vm.MidiImport.BenchmarkCommand.CanExecute(null)));
                Assert.Contains("Original:", vm.MidiImport.BenchmarkText);
                File.WriteAllText(Path.Combine(output, "benchmark.txt"), vm.MidiImport.MetadataSummary + "\n" + vm.MidiImport.BenchmarkText);
                Stage("Construct window");
                var window = new MainWindow(vm);
                Stage("Window constructed");
                var view = (FrameworkElement)window.Content;
                using var errors = new StringWriter();
                using var listener = new TextWriterTraceListener(errors);
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                for (int i = 0; i < 7; i++)
                {
                    Stage($"Render tab {i}");
                    vm.SelectedTab = i;
                    view.Measure(new Size(1100, 800)); view.Arrange(new Rect(0, 0, 1100, 800)); view.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.UpdateLayout();
                    Stage("Layout complete");
                    var bitmap = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32);
                    var background = new DrawingVisual();
                    using (var drawing = background.RenderOpen())
                        drawing.DrawRectangle((Brush)app.FindResource("BackgroundBrush"), null, new Rect(0, 0, 1100, 800));
                    bitmap.Render(background);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"tab-{i}.png")); encoder.Save(stream);
                    if (i == 6)
                    {
                        var scroll = Descendants<System.Windows.Controls.ScrollViewer>(view).First(item => item.ScrollableHeight > 100);
                        scroll.ScrollToBottom(); view.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); view.UpdateLayout();
                        var lower = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32); lower.Render(background); lower.Render(view);
                        var lowerEncoder = new PngBitmapEncoder(); lowerEncoder.Frames.Add(BitmapFrame.Create(lower));
                        using var lowerStream = File.Create(Path.Combine(output, "midi-tracks-benchmark.png")); lowerEncoder.Save(lowerStream);
                    }
                }
                Stage("Playlist automatic transition");
                Pump(vm.Library.StartSelectedAsync());
                Pump(Until(() => keyboard.Notes.Contains(62)));
                Assert.Equal(songs[1].Id, vm.Library.Queue.Current!.SongId);
                Assert.True(keyboard.Releases >= 2); Assert.Equal(1, keyboard.MaxConcurrent);
                Pump(vm.Library.PreviousAsync(100));
                Assert.Equal(songs[0].Id, vm.Library.Queue.Current!.SongId);
                Pump(vm.StopAsync()); int count = keyboard.Notes.Length;
                Pump(Task.Delay(240)); Assert.Equal(count, keyboard.Notes.Length);
                Assert.Equal(songs[0].Id, vm.Library.Queue.Current!.SongId);
                Stage("Stop invalidates pending MIDI load and settings restore");
                var midiSong = new Song { Title = "Complexo", Kind = "MIDI", FileName = "complex.mid", Speed = 110, Transpose = 12,
                    Optimization = new() { Preset = OptimizationPreset.Original }, SelectedTracks = [1], PerformanceMode = PlaybackPerformanceMode.FpsFriendly };
                File.Copy(midiPath, library.GetSongPath(midiSong)); vm.Songs.Add(midiSong);
                vm.Library.SelectedPlaylist = null; vm.Library.SelectedSong = midiSong;
                var loading = vm.Library.StartSelectedAsync(); Pump(vm.StopAsync()); Pump(loading);
                count = keyboard.Notes.Length; Pump(Task.Delay(120)); Assert.Equal(count, keyboard.Notes.Length);
                Assert.Equal(110, vm.Speed); Assert.Equal(12, vm.Transpose);
                Assert.Equal(PlaybackPerformanceMode.FpsFriendly, vm.Settings.PerformanceMode);
                Assert.Equal(new[] { 1 }, vm.MidiImport.SelectedTrackIndexes);
                Assert.Equal(OptimizationPreset.Original, vm.MidiImport.Preset);
                vm.MidiImport.Preset = OptimizationPreset.Balanced; Pump(vm.MidiImport.EnsureReadyAsync());
                vm.UpdateSongCommand.Execute(null); Pump(Until(() => vm.UpdateSongCommand.CanExecute(null)));
                var savedMidi = new SongLibraryService(directory).LoadAsync(); Pump(savedMidi);
                Assert.Equal(OptimizationPreset.Balanced, savedMidi.Result.Single(song => song.Id == midiSong.Id).Optimization.Preset);
                Assert.Empty(failures);
                Pump(vm.ShutdownAsync());
                listener.Flush(); PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                Assert.True(string.IsNullOrWhiteSpace(errors.ToString()), errors.ToString());
                Assert.Equal(61, vm.Mapping.All.Count);
                Stage("Bindings validated");
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }
    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("UI integration condition did not complete."); await Task.Delay(10); }
    }
    private sealed class FakePlayer : IKeyboardPlayer
    {
        private int concurrent;
        public int MaxConcurrent, Releases;
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> notes = new();
        public int[] Notes => notes.ToArray();
        public async Task PlayChord(IReadOnlyList<int> chord, PlaybackOptions options, CancellationToken token)
        {
            int active = Interlocked.Increment(ref concurrent); MaxConcurrent = Math.Max(MaxConcurrent, active);
            try { foreach (int note in chord) notes.Enqueue(note); await Task.Delay(2, token); }
            finally { Interlocked.Decrement(ref concurrent); }
        }
        public void ReleaseAllKeys() => Interlocked.Increment(ref Releases);
    }
}

