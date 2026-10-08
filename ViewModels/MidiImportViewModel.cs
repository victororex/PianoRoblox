using System.Collections.ObjectModel;
using System.Windows.Input;
using PianoRoblox.Core.Models;
using PianoRoblox.Core.Services;
using PianoRoblox.Utilities;

namespace PianoRoblox.ViewModels;

public sealed class TrackSelectionViewModel(MidiTrackInfo info, bool selected) : ObservableObject
{
    private bool isSelected = selected;
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
    public int TrackIndex => info.TrackIndex;
    public string Name => info.Name;
    public string Instrument => info.InstrumentName;
    public string Channels => string.Join(", ", info.Channels.Select(channel => channel + 1));
    public int NoteCount => info.NoteCount;
    public int OutOfRangeCount => info.OutOfRangeCount;
    public string Range => info.LowestNote is { } low && info.HighestNote is { } high
        ? $"{PianoMappingService.Name(low)}–{PianoMappingService.Name(high)}" : "—";
}

/// <summary>Owns MIDI analysis and cancellable, versioned optimization; never sends keyboard input.</summary>
public sealed class MidiImportViewModel : ObservableObject
{
    private readonly MidiService reader = new();
    private readonly MidiOptimizationService optimizer = new();
    private OptimizationSettings settings = new();
    private CancellationTokenSource? calculation;
    private Task pending = Task.CompletedTask;
    private long version;
    private bool updating, busy;
    private Exception? error;
    private string status = "Importe um MIDI para analisar as trilhas.", benchmarkText = "";
    public MidiImportResult? Data { get; private set; }
    public OptimizationResult? Result { get; private set; }
    public MusicSequence? Sequence => Result?.Sequence;
    public ObservableCollection<TrackSelectionViewModel> Tracks { get; } = [];
    public Array Presets { get; } = Enum.GetValues<OptimizationPreset>();
    public bool IsLoaded => Data != null;
    public bool IsBusy { get => busy; private set => Set(ref busy, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string BenchmarkText { get => benchmarkText; private set => Set(ref benchmarkText, value); }
    public OptimizationPreset Preset { get => settings.Preset; set { settings.Preset = value; Changed(); } }
    public bool RemovePercussion { get => settings.RemovePercussion; set { settings.RemovePercussion = value; Changed(); } }
    private OptimizationSettings DisplaySettings => Preset == OptimizationPreset.Custom ? settings :
        OptimizationSettings.ForPreset(Preset == OptimizationPreset.Auto ? Result?.EffectivePreset ?? OptimizationPreset.Balanced : Preset);
    public double MergeWindowMs { get => DisplaySettings.MergeWindowMs; set { settings.MergeWindowMs = value; Changed(); } }
    public int MaxChordNotes { get => DisplaySettings.MaxChordNotes; set { settings.MaxChordNotes = value; Changed(); } }
    public int MinimumVelocity { get => DisplaySettings.MinimumVelocity; set { settings.MinimumVelocity = value; Changed(); } }
    public int MaxNotesPer250Ms { get => DisplaySettings.MaxNotesPer250Ms; set { settings.MaxNotesPer250Ms = value; Changed(); } }
    public int MaxNotesPerSecond { get => DisplaySettings.MaxNotesPerSecond; set { settings.MaxNotesPerSecond = value; Changed(); } }
    public bool MelodyPriority { get => settings.MelodyPriority; set { settings.MelodyPriority = value; Changed(); } }
    public bool IsCustom => Preset == OptimizationPreset.Custom;
    public int[] SelectedTrackIndexes => Tracks.Where(track => track.IsSelected).Select(track => track.TrackIndex).ToArray();
    public string MetadataSummary => Data is { } data
        ? $"{data.Title} • {MainViewModel.FormatTime(data.DurationMs)} • BPM inicial {data.InitialBpm:0.##}\n{data.Tracks.Count} trilhas • {data.Notes.Count} notas MIDI • Complexidade: {data.Analysis.Complexity}" : "";
    public string BeforeSummary => Result is { } result ? Describe(result.Before) : "";
    public string AfterSummary => Result is { } result ? $"{Describe(result.After)}\nRedução: {result.ReductionPercent:0.0}%" : "";
    public string PercussionSummary => Result is { } result
        ? $"Percussão encontrada: {result.PercussionFound} notas • Removidas: {result.PercussionRemoved}" : "";
    public string RecommendationSummary => Result is { } result
        ? $"Preset recomendado: {result.RecommendedPreset} • Aplicado: {result.EffectivePreset}\nBPM e duração preservados. Alterações valem no próximo Play." : "";
    public ICommand BenchmarkCommand { get; }
    public event Action<MusicSequence>? SequenceChanged;

    public MidiImportViewModel(Action<Exception> onError) => BenchmarkCommand = new AsyncCommand(BenchmarkAsync, onError);

    public async Task LoadAsync(string path, OptimizationSettings? options = null, IReadOnlyCollection<int>? selectedTracks = null)
    {
        Clear(); long ticket = version; IsBusy = true; Status = "Lendo MIDI…";
        try
        {
            var data = await Task.Run(() => reader.ReadDetailed(path));
            if (ticket != version) throw new OperationCanceledException();
            updating = true; Data = data; settings = (options ?? new()).Validated();
            var selected = selectedTracks?.ToHashSet();
            foreach (var track in data.Tracks)
            {
                var row = new TrackSelectionViewModel(track, selected?.Contains(track.TrackIndex) ?? (!track.IsPercussion && track.NoteCount > 0));
                row.PropertyChanged += (_, _) => { if (!updating) Schedule(); };
                Tracks.Add(row);
            }
            updating = false; RaiseAll(); Schedule(); await EnsureReadyAsync();
        }
        catch { if (ticket == version) { IsBusy = false; Status = "Falha ao ler MIDI."; } throw; }
        finally { updating = false; }
    }

    public OptimizationSettings CaptureSettings() => settings.Validated();
    public async Task EnsureReadyAsync()
    {
        Task current;
        do { current = pending; await current; } while (current != pending);
        if (error != null) throw new InvalidOperationException("Não foi possível otimizar o MIDI.", error);
    }
    public void Clear()
    {
        version++; calculation?.Cancel(); calculation?.Dispose(); calculation = null;
        updating = true; Tracks.Clear(); updating = false;
        Data = null; Result = null; error = null; pending = Task.CompletedTask; IsBusy = false;
        BenchmarkText = ""; Status = "Importe um MIDI para analisar as trilhas."; RaiseAll();
    }
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        Raise(name); Raise(nameof(IsCustom));
        if (name == nameof(Preset)) RaiseAll();
        if (!updating) Schedule();
    }
    private void Schedule()
    {
        if (Data == null) return;
        version++; calculation?.Cancel(); calculation?.Dispose(); calculation = new();
        BenchmarkText = "";
        pending = RecalculateAsync(Data, CaptureSettings(), SelectedTrackIndexes, version, calculation.Token);
    }
    private async Task RecalculateAsync(MidiImportResult data, OptimizationSettings options, int[] tracks, long ticket, CancellationToken token)
    {
        IsBusy = true; Status = "Analisando e otimizando…"; error = null;
        try
        {
            var result = await Task.Run(() => optimizer.Optimize(data, options, tracks, token), token);
            if (ticket != version) return;
            Result = result; Status = $"Pronto • {result.After.NoteCount} notas na sequência otimizada.";
            RaiseAll(); SequenceChanged?.Invoke(result.Sequence);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (ticket == version) { error = ex; Status = ex.Message; } }
        finally { if (ticket == version) IsBusy = false; }
    }
    private async Task BenchmarkAsync()
    {
        await EnsureReadyAsync(); if (Data == null) return;
        var data = Data; var options = CaptureSettings(); var tracks = SelectedTrackIndexes; long ticket = version;
        BenchmarkText = "Simulando…";
        var rows = await Task.Run(() => optimizer.Benchmark(data, options, tracks));
        if (ticket != version) return;
        BenchmarkText = string.Join("\n", rows.Select(row => $"{row.Preset}: {row.Events} eventos • {row.Notes} notas • pico {row.PeakEventsPerSecond} eventos/s • {row.PeakNotesPerSecond} notas/s • acorde {row.MaxChordNotes} • ~{row.EstimatedSendInputCalls} chamadas SendInput"))
            + "\nEstimativa para Auto / GroupSafe, sem transposição. Não mede FPS, não envia teclas; modo Spread, limpeza e falhas podem aumentar as chamadas.";
    }
    private static string Describe(MidiAnalysis value) => $"{value.NoteCount} notas • {value.EventCount} eventos • {value.TrackCount} trilhas\nMaior acorde: {value.MaxChordNotes} • Pico / 250 ms: {value.PeakNotesPer250Ms}\nPico / segundo: {value.PeakNotesPerSecond} notas • {value.PeakEventsPerSecond} eventos\nMédia: {value.AverageNotesPerSecond:0.0} notas/s";
    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Data), nameof(Result), nameof(Sequence), nameof(IsLoaded), nameof(Preset), nameof(RemovePercussion),
            nameof(MergeWindowMs), nameof(MaxChordNotes), nameof(MinimumVelocity), nameof(MaxNotesPer250Ms), nameof(MaxNotesPerSecond),
            nameof(MelodyPriority), nameof(IsCustom), nameof(MetadataSummary), nameof(BeforeSummary), nameof(AfterSummary),
            nameof(PercussionSummary), nameof(RecommendationSummary) }) Raise(name);
    }
}
