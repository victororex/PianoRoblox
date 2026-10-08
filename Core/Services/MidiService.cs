using PianoRoblox.Core.Models;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Standards;
using System.Text.RegularExpressions;

namespace PianoRoblox.Core.Services;

public sealed class MidiService
{
    // V1 callers keep the same API; the detailed representation is reusable for V2 imports.
    public MusicSequence Read(string path) => ToOriginal(ReadDetailed(path));
    public MusicSequence Convert(MidiFile file, string title) => ToOriginal(Analyze(file, title));
    private static MusicSequence ToOriginal(MidiImportResult import) => new MidiOptimizationService()
        .Optimize(import, OptimizationSettings.ForPreset(OptimizationPreset.Original)).Sequence;

    public MidiImportResult ReadDetailed(string path) => Analyze(MidiFile.Read(path), Path.GetFileNameWithoutExtension(path));

    public MidiImportResult Analyze(MidiFile file, string title)
    {
        MidiFileFormat? format = null;
        try { format = file.OriginalFormat; }
        catch (InvalidOperationException) { /* In-memory MidiFile instances have no original file header. */ }
        if (format == MidiFileFormat.MultiSequence)
            throw new InvalidDataException("MIDI formato 2 possui sequências independentes. Exporte como MIDI formato 0 ou 1.");
        var tempoMap = file.GetTempoMap();
        double Milliseconds(long tick) => TimeConverter.ConvertTo<MetricTimeSpan>(tick, tempoMap).TotalMicroseconds / 1000d;
        var chunks = file.GetTrackChunks().ToArray();
        var names = chunks.Select((track, index) => track.Events.OfType<SequenceTrackNameEvent>()
            .Select(e => e.Text).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"Track {index + 1}").ToArray();
        var timed = new List<(long Tick, int Track, int Order, MidiEvent Event)>();
        for (int track = 0; track < chunks.Length; track++)
        {
            long tick = 0; int order = 0;
            foreach (var midiEvent in chunks[track].Events)
            {
                tick += midiEvent.DeltaTime;
                timed.Add((tick, track, order++, midiEvent));
            }
        }
        // Programs belong to channels, even when their changes are in a separate MIDI track.
        var programs = new int[16];
        var notes = new List<MidiNoteEvent>();
        var active = new Dictionary<(int Track, int Channel, int Pitch), Queue<int>>();
        foreach (var entry in timed.OrderBy(e => e.Tick).ThenBy(e => e.Track).ThenBy(e => e.Order))
        {
            if (entry.Event is ProgramChangeEvent program)
                programs[(int)program.Channel] = (int)program.ProgramNumber;
            else if (entry.Event is NoteOnEvent on && on.Velocity > 0)
            {
                int channel = (int)on.Channel, pitch = (int)on.NoteNumber;
                var key = (entry.Track, channel, pitch);
                if (!active.TryGetValue(key, out var pending)) active[key] = pending = new();
                pending.Enqueue(notes.Count);
                int programNumber = programs[channel];
                notes.Add(new(Milliseconds(entry.Tick), pitch, (int)on.Velocity, channel, entry.Track,
                    names[entry.Track], programNumber, InstrumentName(programNumber, channel == 9), 0, channel == 9));
            }
            else
            {
                (int Channel, int Pitch)? key = entry.Event switch
                {
                    NoteOffEvent off => ((int)off.Channel, (int)off.NoteNumber),
                    NoteOnEvent zero when zero.Velocity == 0 => ((int)zero.Channel, (int)zero.NoteNumber),
                    _ => null
                };
                if (key is { } released && active.TryGetValue((entry.Track, released.Channel, released.Pitch), out var pending) && pending.Count > 0)
                {
                    int index = pending.Dequeue();
                    notes[index] = notes[index] with { DurationMs = Math.Max(0, Milliseconds(entry.Tick) - notes[index].TimeMs) };
                }
            }
        }
        if (notes.Count == 0) throw new InvalidDataException("O MIDI não contém Note On com velocity maior que zero.");
        notes = notes.OrderBy(n => n.TimeMs).ThenBy(n => n.TrackIndex).ThenBy(n => n.MidiNote).ToList();
        double duration = Math.Max(Milliseconds(timed.Count == 0 ? 0 : timed.Max(e => e.Tick)), notes[^1].TimeMs + 100);
        var byTrack = notes.GroupBy(n => n.TrackIndex).ToDictionary(g => g.Key, g => g.ToArray());
        var tracks = Enumerable.Range(0, chunks.Length).Select(index =>
        {
            var trackNotes = byTrack.GetValueOrDefault(index) ?? [];
            return new MidiTrackInfo(index, names[index], string.Join(" / ", trackNotes.Select(n => n.InstrumentName).Distinct()),
                trackNotes.Select(n => n.Channel).Distinct().Order().ToArray(), trackNotes.Select(n => n.ProgramNumber).Distinct().Order().ToArray(),
                trackNotes.Length, trackNotes.Select(n => (int?)n.MidiNote).Min(), trackNotes.Select(n => (int?)n.MidiNote).Max(),
                trackNotes.Count(n => n.MidiNote is < 36 or > 96), trackNotes.Count(n => n.IsPercussion));
        }).ToArray();
        return new(title, tempoMap.GetTempoAtTime(new MidiTimeSpan(0)).BeatsPerMinute, duration, notes, tracks,
            new MidiComplexityAnalyzer().Analyze(notes, duration));
    }

    private static string InstrumentName(int program, bool percussion) => percussion ? "Drum Kit" :
        Regex.Replace(((GeneralMidiProgram)program).ToString(), "(?<=[a-z0-9])(?=[A-Z])", " ");
}

