using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Services;
using PianoRoblox.Utilities;
using InputMode = PianoRoblox.Core.Models.InputMode;

namespace PianoRoblox.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public PianoMappingService Mapping { get; } = new();
    public MidiImportViewModel MidiImport { get; }
    public LibraryViewModel Library { get; }
    private readonly TimelineViewModel timeline;
    private readonly PlaybackVisualBuffer visualBuffer = new();
    private readonly SemaphoreSlim songSwitchGate = new(1, 1);
    private long playbackGeneration;
    private bool loadingMidi, switching;
    private Song? loadedSong;
    private PlaybackState lastVisualState;
    private readonly SheetParser parser;
    private readonly SequenceJsonService json;
    private readonly SongLibraryService library;
    private readonly Action<Exception>? errorReporter;
    private readonly PlaybackService playback;
    private readonly DispatcherTimer timer;
    private readonly Dispatcher dispatcher = Application.Current.Dispatcher;
    private MusicSequence? source, prepared;
    private string? sourcePath;
    private string sourceKind = "Sheet", sourceSheet = "";
    private bool preparing;
    private int currentIndex = -1;
    private long highlightUntil;
    private HashSet<int> activeNotes = [];
    public HashSet<int> ActiveNotes { get => activeNotes; private set => Set(ref activeNotes, value); }
    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<Song> Songs { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<TimelineRow> Timeline => timeline.Rows;
    public Song? SelectedSong { get; set; }
    public Array RangePolicies { get; } = Enum.GetValues<RangePolicy>();
    public Array ChordModes { get; } = Enum.GetValues<ChordMode>();
    public Array InputModes { get; } = Enum.GetValues<InputMode>();
    public Array PerformanceModes { get; } = Enum.GetValues<PlaybackPerformanceMode>();
    public string ProfileName => Mapping.Profile.Name;
    public double[] SpaceOptions { get; } = [.0625, .125, .25, .5, 1];
    public double[] DashOptions { get; } = [.25, .5, 1];
    public int[] TransposeOptions { get; } = [-24, -12, 0, 12, 24];
    public int[] CountdownOptions { get; } = [0, 1, 2, 3, 5, 10];
    private string title = "Escolha sua próxima música", artist = "", origin = "", sheetText = "[6rtu]--[rtu]--[etu]--[etu]";
    public string Title { get => title; set => Set(ref title, value); }
    public string Artist { get => artist; set => Set(ref artist, value); }
    public string Origin { get => origin; set => Set(ref origin, value); }
    public string SheetText { get => sheetText; set => Set(ref sheetText, value); }
    private string status = "Pronto. Importe um MIDI ou analise uma sheet.", statistics = "Nenhuma música carregada.", sheetStatistics = "", rangeStatus = "";
    public string Status { get => status; private set => Set(ref status, value); }
    public string Statistics { get => statistics; private set => Set(ref statistics, value); }
    public string SheetStatistics { get => sheetStatistics; private set => Set(ref sheetStatistics, value); }
    public string RangeStatus { get => rangeStatus; private set => Set(ref rangeStatus, value); }
    private double speed = 100, sheetBpm = 120, spaceBeats = .25, dashBeats = .5, positionMs, durationMs, seekMs;
    public double Speed { get => speed; set { if (double.IsFinite(value) && Set(ref speed, Math.Clamp(value, 50, 150))) { playback.Speed = speed; Raise(nameof(RealDuration)); } } }
    public double SheetBpm { get => sheetBpm; set => Set(ref sheetBpm, value); }
    public double SpaceBeats { get => spaceBeats; set => Set(ref spaceBeats, value); }
    public double DashBeats { get => dashBeats; set => Set(ref dashBeats, value); }
    public double PositionMs { get => positionMs; private set { Set(ref positionMs, value); Raise(nameof(CurrentTime)); } }
    public double DurationMs { get => durationMs; private set { Set(ref durationMs, value); Raise(nameof(TotalTime)); Raise(nameof(RealDuration)); Raise(nameof(DurationMaximum)); } }
    public double DurationMaximum => Math.Max(1, DurationMs);
    public double SeekMs { get => seekMs; set { Set(ref seekMs, value); Raise(nameof(SeekTime)); } }
    public string CurrentTime => FormatTime(PositionMs);
    public string TotalTime => FormatTime(DurationMs);
    public string SeekTime => FormatTime(SeekMs);
    public string RealDuration => $"Duração a {Speed:0}%: {FormatTime(DurationMs * 100 / Speed)}";
    private int transpose, selectedTab;
    public int Transpose { get => transpose; set { if (Set(ref transpose, Math.Clamp(value, -24, 24))) UpdateRange(); } }
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    public string PlayerState => playback.State switch
    {
        PlaybackState.Countdown => "CONTAGEM", PlaybackState.Playing => "TOCANDO", PlaybackState.Paused => "PAUSADO",
        PlaybackState.Completed => "CONCLUÍDO", _ => "PRONTO"
    };
    public ICommand ImportMidiCommand { get; }
    public ICommand ImportJsonCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand SeekCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand PlaySheetCommand { get; }
    public ICommand SaveSongCommand { get; }
    public ICommand LoadSongCommand { get; }
    public ICommand DeleteSongCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ScaleCommand { get; }
    public ICommand ChordCommand { get; }
    public ICommand DemoCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand UpdateSongCommand { get; }

    public MainViewModel(SongLibraryService? songLibrary = null, IKeyboardPlayer? keyboardPlayer = null, Func<bool>? isFocused = null,
        Action<Exception>? errorReporter = null)
    {
        this.errorReporter = errorReporter;
        parser = new(Mapping); json = new(Mapping); library = songLibrary ?? new();
        timeline = new(Mapping);
        MidiImport = new(HandleError);
        Library = new(library, Songs, song => SwitchSongAsync(song, false, false),
            (song, between) => SwitchSongAsync(song, true, between), HandleError, Log);
        var focus = new RobloxWindowService();
        var keyboard = keyboardPlayer ?? new KeyboardPlayer(Mapping, new KeyboardService(focus.IsFocused));
        playback = new(keyboard, isFocused ?? focus.IsFocused);
        playback.Status += message => Dispatch(() => { Status = message; Log(message); Raise(nameof(PlayerState)); });
        playback.Failed += ex => Dispatch(() => HandleError(ex));
        playback.EventPlayed += visualBuffer.Capture;
        MidiImport.SequenceChanged += MidiSequenceChanged;
        ImportMidiCommand = Command(ImportMidiAsync);
        ImportJsonCommand = Command(ImportJsonAsync);
        ExportJsonCommand = Command(ExportJsonAsync);
        PlayCommand = Command(() => PlayAsync());
        PauseCommand = Command(() => { TogglePause(); return Task.CompletedTask; });
        StopCommand = Command(StopAsync);
        RestartCommand = Command(async () => { await StopAsync(); await PlayAsync(0); });
        SeekCommand = Command(async () => { await StopAsync(); await PlayAsync(SeekMs); });
        PasteCommand = Command(() => { if (Clipboard.ContainsText()) SheetText = Clipboard.GetText(); SelectedTab = 1; return Task.CompletedTask; });
        ClearCommand = Command(() => { SheetText = ""; SheetStatistics = ""; return Task.CompletedTask; });
        AnalyzeCommand = Command(AnalyzeAsync);
        PlaySheetCommand = Command(async () => { await AnalyzeAsync(); await PlayAsync(); });
        SaveSongCommand = Command(SaveSongAsync);
        LoadSongCommand = Command(LoadSongAsync);
        DeleteSongCommand = Command(DeleteSongAsync);
        SaveSettingsCommand = Command(SaveSettingsAsync);
        NextCommand = Command(async () => { await StopAsync(); long ticket = playbackGeneration; await Library.AdvanceAsync(false, () => ticket == playbackGeneration); });
        PreviousCommand = Command(async () =>
        {
            if (Library.HasContext) await Library.PreviousAsync(playback.PositionMs);
            else { await StopAsync(); await PlayAsync(0, Settings.CountdownBetweenSongs); }
        });
        PlayPauseCommand = Command(async () =>
        {
            if (playback.State is PlaybackState.Playing or PlaybackState.Paused) TogglePause();
            else await PlayAsync();
        });
        UpdateSongCommand = Command(UpdateSongAsync);
        ScaleCommand = Command(() => TestAsync([60, 62, 64, 65, 67, 69, 71, 72], false));
        ChordCommand = Command(() => TestAsync([60, 64, 67], true));
        DemoCommand = Command(async () =>
        {
            await StopAsync(); ClearMidiContext(); sourcePath = null; sourceKind = "JSON";
            SetSequence(new("Teste de três acordes", 60, 2500, [new(0, [60, 64, 67]), new(1000, [62, 65, 69]), new(2000, [64, 67, 71])]));
        });
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            PositionMs = Math.Min(DurationMs, playback.PositionMs);
            if (visualBuffer.TryTake(out int index, out var musicEvent) && musicEvent != null)
            {
                currentIndex = index; ActiveNotes = musicEvent.Notes.ToHashSet();
                highlightUntil = Environment.TickCount64 + 120; RefreshTimeline(index);
                if (Settings.DetailedLog) Log($"Evento visual {index + 1}: {string.Join(' ', musicEvent.Notes.Select(PianoMappingService.Name))}");
            }
            if (ActiveNotes.Count > 0 && (Environment.TickCount64 > highlightUntil || playback.State is not PlaybackState.Playing)) ActiveNotes = [];
            if (lastVisualState != playback.State) { lastVisualState = playback.State; Raise(nameof(PlayerState)); }
        };
        timer.Start();
    }

    public async Task InitializeAsync()
    {
        try { Settings = await library.LoadSettingsAsync(); Speed = Settings.DefaultSpeed; Raise(nameof(Settings)); ApplyTheme(); }
        catch (Exception ex) { HandleError(ex); }
        try { foreach (var song in await library.LoadAsync()) Songs.Add(song); }
        catch (Exception ex) { HandleError(ex); }
        try { await Library.InitializeAsync(); }
        catch (Exception ex) { HandleError(ex); }
        if (library.MigratedFromV1) Log(library.MigrationMessage!);
        Log("Aplicativo iniciado. Nenhuma tecla é enviada antes do Play.");
    }

    private ICommand Command(Func<Task> action) => new AsyncCommand(action, HandleError);
    private void Dispatch(Action action) { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(action); }
    public void Log(string message)
    {
        Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 500) Logs.RemoveAt(Logs.Count - 1);
    }
    public void HandleError(Exception ex)
    {
        Status = "Não foi possível concluir: " + ex.Message;
        Log(ex.ToString());
        if (errorReporter != null) errorReporter(ex);
        else MessageBox.Show(Status, "PianoRoblox", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private async Task ImportMidiAsync()
    {
        var dialog = new OpenFileDialog { Filter = "MIDI (*.mid;*.midi)|*.mid;*.midi" };
        if (dialog.ShowDialog() != true) return;
        await StopAsync();
        ClearPlaybackContext(); loadingMidi = true;
        try { await MidiImport.LoadAsync(dialog.FileName); }
        finally { loadingMidi = false; }
        sourcePath = dialog.FileName; sourceKind = "MIDI"; SetSequence(MidiImport.Sequence!);
        Origin = Path.GetFileName(dialog.FileName); Artist = ""; SelectedTab = 6;
        Log($"MIDI carregado: {dialog.FileName}");
    }
    private async Task ImportJsonAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Sequência PianoRoblox (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        await StopAsync();
        var loaded = await json.ImportAsync(dialog.FileName);
        ClearMidiContext();
        sourcePath = dialog.FileName; sourceKind = "JSON"; SetSequence(loaded); Origin = Path.GetFileName(dialog.FileName);
    }
    private async Task AnalyzeAsync()
    {
        await StopAsync();
        var parsed = parser.Parse(SheetText, SheetBpm, SpaceBeats, DashBeats);
        SheetStatistics = $"{parsed.Sequence.NoteCount} notas • {parsed.Sequence.ChordCount} acordes • {parsed.UnmappedCount} fora do mapeamento\n" +
            $"Caracteres inválidos: {(parsed.InvalidCharacters.Length == 0 ? "nenhum" : parsed.InvalidCharacters)} • BPM: {SheetBpm:0.##} • Duração estimada: {FormatTime(parsed.Sequence.DurationMs)}";
        if (parsed.Errors.Count > 0) throw new InvalidDataException(string.Join("\n", parsed.Errors.Take(8)));
        if (parsed.Sequence.NoteCount == 0) throw new InvalidDataException("A sheet não contém notas.");
        ClearMidiContext();
        sourcePath = null; sourceKind = "Sheet"; sourceSheet = SheetText;
        SetSequence(parsed.Sequence); Log("Sheet analisada com timing aproximado.");
    }
    private void SetSequence(MusicSequence sequence)
    {
        source = sequence; prepared = null; Title = sequence.Title;
        visualBuffer.Clear();
        DurationMs = sequence.DurationMs; SeekMs = 0; currentIndex = -1;
        Statistics = $"{sourceKind} • BPM inicial {sequence.InitialBpm:0.##} • {sequence.NoteCount} notas • {sequence.ChordCount} acordes\n" +
            $"Menor: {(sequence.LowestNote is { } low ? PianoMappingService.Name(low) : "—")} • Maior: {(sequence.HighestNote is { } high ? PianoMappingService.Name(high) : "—")} • Fora de C2–C7: {sequence.OutOfRangeCount}";
        UpdateRange(); RefreshTimeline(0); Status = "Música carregada. Pronta para tocar.";
    }
    private void UpdateRange()
    {
        if (source == null) return;
        var count = source.Events.Sum(e => e.Notes.Count(n => n + Transpose is < 36 or > 96));
        RangeStatus = count > 0 ? $"{count} notas ficam fora de C2–C7 após transposição ({Transpose:+0;-0;0})." : "Todas as notas estão no alcance C2–C7.";
    }
    private MusicSequence Prepare()
    {
        if (source == null) throw new InvalidOperationException("Importe um MIDI, uma sequência JSON ou analise uma sheet primeiro.");
        Settings.Validate();
        var policy = Settings.RangePolicy;
        if (policy == RangePolicy.Ask && source.Events.Any(e => e.Notes.Any(n => n + Transpose is < 36 or > 96)))
        {
            var choice = MessageBox.Show("Há notas fora do alcance.\nSim: transpor por oitava.\nNão: ignorar.\nCancelar: voltar.", "Notas fora de C2–C7", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) throw new OperationCanceledException("Conversão cancelada.");
            policy = choice == MessageBoxResult.Yes ? RangePolicy.Octave : RangePolicy.Ignore;
        }
        var result = SequenceTransformer.Apply(source with { Title = Title }, Transpose, policy, Mapping.Profile);
        if (result.Ignored > 0) Log($"{result.Ignored} notas foram ignoradas porque estão fora do alcance do piano.");
        if (result.Folded > 0) Log($"{result.Folded} notas foram transpostas por oitava.");
        if (result.Sequence.Events.Count == 0) throw new InvalidOperationException("Nenhuma nota tocável após a conversão.");
        return result.Sequence;
    }
    private PlaybackOptions Options() => new(Settings.ChordSpreadMs, Settings.KeyHoldMs, Settings.ChordMode, Settings.InputMode, Settings.RequireRobloxFocus,
        Settings.PerformanceMode);
    private async Task PlayAsync(double? start = null, int? countdown = null, bool fromSwitch = false)
    {
        if (preparing || loadingMidi || (switching && !fromSwitch)) return;
        if (playback.State == PlaybackState.Paused && start == null) { playback.Resume(); return; }
        if (playback.State is PlaybackState.Playing or PlaybackState.Countdown) return;
        preparing = true;
        long generation = playbackGeneration;
        try
        {
            if (sourceKind == "MIDI") { await MidiImport.EnsureReadyAsync(); source = MidiImport.Sequence ?? source; }
            if (generation != playbackGeneration) return;
            prepared = Prepare(); currentIndex = -1; RefreshTimeline(0);
            var sequence = prepared;
            if (Settings.ChordMode == ChordMode.Simultaneous && sequence.Events.Any(e => e.Notes.Any(n => Mapping.FromMidi(n)!.RequiresShift)))
                Log("Acordes com Shift usarão sequência protegida mesmo no modo simultâneo.");
            var task = playback.StartAsync(sequence, Options(), countdown ?? Settings.CountdownSeconds, start ?? 0);
            _ = ObservePlaybackAsync(task, generation);
        }
        catch (OperationCanceledException) { Status = "Operação cancelada."; }
        finally { preparing = false; }
    }
    public async Task StopAsync()
    {
        Interlocked.Increment(ref playbackGeneration);
        await playback.StopAsync(); visualBuffer.Clear(); ActiveNotes = []; Raise(nameof(PlayerState));
    }
    private void TogglePause() { if (playback.State == PlaybackState.Paused) playback.Resume(); else playback.Pause(); Raise(nameof(PlayerState)); }
    public void HandleHotkey(string action)
    {
        switch (action)
        {
            case "Play": PlayCommand.Execute(null); break;
            case "Pause": TogglePause(); break;
            case "Stop": StopCommand.Execute(null); break;
            case "Faster": Speed += 5; break;
            case "Slower": Speed -= 5; break;
            case "Normal": Speed = 100; break;
            case "Next": NextCommand.Execute(null); break;
            case "Previous": PreviousCommand.Execute(null); break;
        }
    }
    private void RefreshTimeline(int index)
    {
        timeline.SetSequence(prepared ?? source);
        timeline.Update(index, currentIndex >= 0);
    }
    public async Task TestAsync(int[] notes, bool chord)
    {
        await StopAsync(); Settings.Validate();
        ClearPlaybackContext();
        var events = chord ? new List<MusicEvent> { new(0, notes) } : notes.Select((n, i) => new MusicEvent(i * 500, [n])).ToList();
        prepared = new("Teste de teclado", 120, events[^1].TimeMs + 300, events);
        DurationMs = prepared.DurationMs; RefreshTimeline(0);
        long generation = playbackGeneration;
        await playback.StartAsync(prepared, Options(), Settings.CountdownSeconds);
        if (generation == playbackGeneration && source != null) { DurationMs = source.DurationMs; prepared = null; RefreshTimeline(0); }
    }
    private async Task ExportJsonAsync()
    {
        if (sourceKind == "MIDI") { await MidiImport.EnsureReadyAsync(); source = MidiImport.Sequence ?? source; }
        var sequence = Prepare();
        var dialog = new SaveFileDialog { Filter = "Sequência PianoRoblox (*.json)|*.json", FileName = "sequencia-pianoroblox.json" };
        if (dialog.ShowDialog() != true) return;
        await json.ExportAsync(dialog.FileName, sequence);
        Log("Sequência exportada com transposição aplicada e tempos originais; velocidade não altera os tempos salvos.");
    }
    private async Task SaveSongAsync()
    {
        if (sourceKind == "MIDI") { await MidiImport.EnsureReadyAsync(); source = MidiImport.Sequence ?? source; }
        if (source == null) throw new InvalidOperationException("Carregue uma música antes de adicionar à biblioteca.");
        Settings.Validate();
        var song = new Song { Title = Title, Artist = Artist, Origin = Origin, Kind = sourceKind, Bpm = source.InitialBpm,
            Transpose = Transpose, Speed = Speed, ChordSpread = Settings.ChordSpreadMs, DurationMs = source.DurationMs, SpaceBeats = SpaceBeats, DashBeats = DashBeats };
        ApplySongSettings(song);
        string extension = sourceKind == "MIDI" ? ".mid" : sourceKind == "Sheet" ? ".txt" : ".json";
        song.FileName = song.Id + extension;
        string path = library.GetSongPath(song);
        if (sourceKind == "MIDI") File.Copy(sourcePath ?? throw new FileNotFoundException("MIDI original indisponível."), path);
        else if (sourceKind == "Sheet") await File.WriteAllTextAsync(path, sourceSheet);
        else await json.ExportAsync(path, source with { Title = Title });
        var next = Songs.Append(song).ToList(); await library.SaveAsync(next); Songs.Add(song);
        loadedSong = song;
        Log("Música adicionada à biblioteca local.");
    }
    private async Task LoadSongAsync()
    {
        if ((Library.SelectedSong ?? SelectedSong) is not { } song) throw new InvalidOperationException("Selecione uma música na biblioteca.");
        await SwitchSongAsync(song, false, false);
    }
    private void ClearPlaybackContext() { loadedSong = null; Library.Queue.Clear(); }
    private void ClearMidiContext() { MidiImport.Clear(); ClearPlaybackContext(); }
    private void MidiSequenceChanged(MusicSequence sequence)
    {
        if (loadingMidi || sourceKind != "MIDI") return;
        // A running song keeps its immutable snapshot; edits are applied on its next start.
        if (playback.State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Countdown)
            source = sequence with { Title = Title };
        else SetSequence(sequence with { Title = Title });
    }
    private async Task SwitchSongAsync(Song song, bool play, bool betweenSongs)
    {
        long ticket = Interlocked.Increment(ref playbackGeneration);
        await songSwitchGate.WaitAsync();
        try
        {
            if (ticket != playbackGeneration) return;
            switching = true;
            await playback.StopAsync(); visualBuffer.Clear(); ActiveNotes = [];
            await LoadSongDataAsync(song);
            if (ticket != playbackGeneration) return;
            if (play) await PlayAsync(0, betweenSongs ? Settings.CountdownBetweenSongs : Settings.CountdownSeconds, true);
        }
        finally { switching = false; songSwitchGate.Release(); }
    }
    private async Task ObservePlaybackAsync(Task task, long generation)
    {
        try
        {
            await task;
            if (generation != playbackGeneration || playback.State != PlaybackState.Completed) return;
            if (playback.SkippedEventCount > 0) Log($"Proteção contra rajadas: {playback.SkippedEventCount} eventos atrasados/excessivos ignorados.");
            if (Library.HasContext) await Library.AdvanceAsync(true, () => generation == playbackGeneration && playback.State == PlaybackState.Completed);
        }
        catch (Exception ex) { HandleError(ex); }
    }
    private void ApplySongSettings(Song song)
    {
        song.Title = Title; song.Artist = Artist; song.Origin = Origin;
        song.Transpose = Transpose; song.Speed = Speed; song.ChordSpread = Settings.ChordSpreadMs;
        song.PerformanceMode = Settings.PerformanceMode; song.PianoProfileId = Mapping.Profile.Id;
        if (sourceKind == "MIDI") { song.Optimization = MidiImport.CaptureSettings(); song.SelectedTracks = MidiImport.SelectedTrackIndexes; }
    }
    private async Task UpdateSongAsync()
    {
        if (loadedSong == null || !Songs.Contains(loadedSong)) throw new InvalidOperationException("Carregue uma música da biblioteca antes de salvar seus ajustes.");
        Settings.Validate();
        if (sourceKind == "MIDI") await MidiImport.EnsureReadyAsync();
        // Persist a copy first: a failed write must not silently mutate the in-memory library.
        var updated = System.Text.Json.JsonSerializer.Deserialize<Song>(System.Text.Json.JsonSerializer.Serialize(loadedSong))!;
        ApplySongSettings(updated);
        await library.SaveAsync(Songs.Select(song => song.Id == updated.Id ? updated : song));
        int index = Songs.IndexOf(loadedSong); Songs[index] = updated; loadedSong = updated;
        Library.SelectedSong = updated; Log("Ajustes da música salvos na biblioteca.");
    }
    private async Task LoadSongDataAsync(Song song)
    {
        string path = library.GetSongPath(song); MusicSequence sequence;
        if (song.Kind == "MIDI")
        {
            loadingMidi = true;
            try { await MidiImport.LoadAsync(path, song.Optimization, song.SelectedTracks); }
            finally { loadingMidi = false; }
            sequence = MidiImport.Sequence!;
        }
        else if (song.Kind == "Sheet")
        {
            SheetText = await File.ReadAllTextAsync(path); SheetBpm = song.Bpm; SpaceBeats = song.SpaceBeats; DashBeats = song.DashBeats;
            var result = parser.Parse(SheetText, SheetBpm, SpaceBeats, DashBeats);
            if (result.Errors.Count > 0) throw new InvalidDataException("A sheet da biblioteca contém erros.");
            sequence = result.Sequence; sourceSheet = SheetText;
        }
        else sequence = await json.ImportAsync(path);
        if (song.Kind != "MIDI") MidiImport.Clear();
        sourcePath = path; sourceKind = song.Kind; SetSequence(sequence with { Title = song.Title });
        Artist = song.Artist; Origin = song.Origin; Transpose = song.Transpose; Speed = song.Speed; Settings.ChordSpreadMs = Math.Clamp(song.ChordSpread, 0, 50);
        Settings.PerformanceMode = Enum.IsDefined(song.PerformanceMode) ? song.PerformanceMode : PlaybackPerformanceMode.Normal;
        loadedSong = song;
        Raise(nameof(Settings)); SelectedTab = 0; Log("Música carregada da biblioteca.");
    }
    private async Task DeleteSongAsync()
    {
        if ((Library.SelectedSong ?? SelectedSong) is not { } song) return;
        await library.SaveAsync(Songs.Where(s => s.Id != song.Id)); Songs.Remove(song);
        Log("Entrada removida. O arquivo local foi preservado na pasta Songs.");
    }
    public async Task SaveSettingsAsync()
    {
        Settings.Validate(); await library.SaveSettingsAsync(Settings); Raise(nameof(Settings)); ApplyTheme(); UpdateRange(); Log("Configurações salvas.");
    }
    private void ApplyTheme() => App.ApplyTheme(Settings.DarkTheme);
    public async Task ShutdownAsync() { timer.Stop(); MidiImport.Clear(); await StopAsync(); playback.Dispose(); }
    public void EmergencyRelease() { Interlocked.Increment(ref playbackGeneration); playback.Dispose(); }
    public static string FormatTime(double ms) { var time = TimeSpan.FromMilliseconds(Math.Max(0, ms)); return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}"; }
}

