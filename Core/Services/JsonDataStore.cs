using System.Collections.Concurrent;
using System.Text.Json;

namespace PianoRoblox.Core.Services;

/// <summary>Atomic writes, validated backups and preservation of corrupt originals. Never resets failed loads.</summary>
public sealed class JsonDataStore(string root)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public event Action<string>? Warning;

    public async Task<T?> ReadAsync<T>(string name)
    {
        string path = Path.Combine(root, name);
        var gate = Gates.GetOrAdd(path, _ => new(1, 1)); await gate.WaitAsync();
        try { return await ReadCoreAsync<T>(path); }
        finally { gate.Release(); }
    }
    private static async Task<T> DeserializeAsync<T>(string path)
    {
        var result = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Options) ?? throw new JsonException("Documento nulo.");
        if (result is System.Collections.IEnumerable entries && result is not string)
            foreach (var entry in entries) if (entry == null) throw new JsonException("Entrada nula no índice.");
        return result;
    }

    private async Task<T?> ReadCoreAsync<T>(string path)
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return default;
        try { return await DeserializeAsync<T>(path); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            try
            {
                var recovered = await DeserializeAsync<T>(path + ".bak");
                Warning?.Invoke($"{Path.GetFileName(path)} inválido/ausente. Backup válido carregado; o original foi preservado.");
                return recovered;
            }
            catch (Exception backupError) when (backupError is JsonException or IOException)
            { throw new InvalidDataException($"Não foi possível ler {Path.GetFileName(path)} nem seu backup. Arquivos preservados; gravação bloqueada para evitar perda de dados.", ex); }
        }
    }

    public async Task WriteAsync<T>(string name, T value)
    {
        string contents = JsonSerializer.Serialize(value, Options);
        string path = Path.Combine(root, name), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var gate = Gates.GetOrAdd(path, _ => new(1, 1)); await gate.WaitAsync();
        try
        {
            bool corrupt = false;
            if (File.Exists(path) || File.Exists(path + ".bak"))
            {
                try { await DeserializeAsync<T>(path); }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    await ReadCoreAsync<T>(path); corrupt = File.Exists(path);
                }
            }
            Directory.CreateDirectory(root);
            BackupBeforeV3();
            if (corrupt) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(temp, contents);
            if (File.Exists(path)) File.Replace(temp, path, corrupt ? null : path + ".bak");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); gate.Release(); }
    }
    private void BackupBeforeV3()
    {
        string destination = Path.Combine(root, "Backups", "before-v3");
        lock (Gates)
        {
            if (Directory.Exists(destination)) return;
            string staging = destination + "." + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            foreach (string file in Directory.EnumerateFiles(root, "*.json")) File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
            foreach (string file in Directory.EnumerateFiles(root, "*.bak")) File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(staging, "README.txt"), "Índices antes da primeira gravação V3. Songs/ não é renomeada, removida ou regravada pela atualização.");
            Directory.Move(staging, destination);
        }
    }
}
