using System.Text.Json;
using System.Text.Json.Serialization;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Core.Services;

public sealed class SequenceJsonService(PianoMappingService mapping)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public sealed record JsonNote(int Midi, double? DurationMs = null, int Velocity = 100, int TrackIndex = -1, double Priority = .5);
    public sealed record KeyEvent(double TimeMs, string[]? Keys = null, JsonNote[]? Notes = null);
    public sealed record Document(string Title, List<KeyEvent> Events, double Bpm = 120, double DurationMs = 0);

    public async Task ExportAsync(string path, MusicSequence sequence)
    {
        var doc = new Document(sequence.Title, sequence.Events.Select(e => new KeyEvent(e.TimeMs,
            e.Notes.Select(n => mapping.FromMidi(n)?.KeyboardKey.ToString() ?? throw new InvalidDataException("Nota fora do alcance.")).ToArray(),
            e.GetNotes().Select(n => new JsonNote(n.MidiNote, n.DurationMs, n.Velocity, n.TrackIndex, n.Priority)).ToArray()))
            .ToList(), sequence.InitialBpm, sequence.DurationMs);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(doc, Options));
    }

    public async Task<MusicSequence> ImportAsync(string path)
    {
        var doc = JsonSerializer.Deserialize<Document>(await File.ReadAllTextAsync(path), Options)
            ?? throw new InvalidDataException("JSON vazio.");
        if (doc.Events == null || doc.Events.Count == 0) throw new InvalidDataException("JSON sem eventos.");
        var events = new List<MusicEvent>();
        foreach (var e in doc.Events)
        {
            if (e == null || !double.IsFinite(e.TimeMs) || e.TimeMs < 0) throw new InvalidDataException("Evento JSON inválido.");
            MusicNote[] notes;
            if (e.Notes is { Length: > 0 })
            {
                if (e.Notes.Any(n => n == null || n.Midi is < 0 or > 127 || n.Velocity is < 0 or > 127 ||
                    !double.IsFinite(n.Priority) || n.Priority is < 0 or > 1 ||
                    (n.DurationMs.HasValue && (!double.IsFinite(n.DurationMs.Value) || n.DurationMs.Value < 0))))
                    throw new InvalidDataException("Nota ou duração JSON inválida.");
                notes = e.Notes.Select(n => new MusicNote(n.Midi, n.DurationMs, n.Velocity, n.TrackIndex, n.Priority)).ToArray();
            }
            else
            {
                if (e.Keys == null || e.Keys.Length == 0) throw new InvalidDataException("Evento JSON sem notas.");
                notes = e.Keys.Select(k => k is { Length: 1 } && mapping.FromKey(k[0]) is { } note
                    ? new MusicNote(note.MidiNote) : throw new InvalidDataException($"Tecla inválida: {k}")).ToArray();
            }
            events.Add(new(e.TimeMs, notes.Select(n => n.MidiNote).Distinct().ToArray()) { NoteDetails = notes });
        }
        if (!double.IsFinite(doc.Bpm) || doc.Bpm <= 0 || !double.IsFinite(doc.DurationMs) || doc.DurationMs < 0)
            throw new InvalidDataException("BPM ou duração inválida.");
        events = events.GroupBy(e => e.TimeMs).OrderBy(g => g.Key).Select(g =>
        {
            var notes = g.SelectMany(e => e.NoteDetails).GroupBy(n => n.MidiNote).Select(group =>
                group.OrderByDescending(n => n.Velocity).First() with { DurationMs = group.Max(n => n.DurationMs) }).ToArray();
            return new MusicEvent(g.Key, notes.Select(n => n.MidiNote).ToArray()) { NoteDetails = notes, Priority = notes.Max(n => n.Priority) };
        }).ToList();
        double end = events.Max(e => e.TimeMs + e.NoteDetails.Max(n => n.DurationMs ?? 100));
        return new(doc.Title ?? "Sequência", doc.Bpm, Math.Max(doc.DurationMs, end), events);
    }
}
