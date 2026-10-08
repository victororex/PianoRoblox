namespace PianoRoblox.Core.Services;

/// <summary>Publishes a complete copy atomically, so an interrupted copy never becomes the active library.</summary>
public sealed class DataMigrationService
{
    public const string SuccessMessage = "Dados da versão anterior migrados para PianoRoblox.";

    public bool MigrateIfNeeded(string applicationDataRoot)
    {
        string previous = Path.Combine(applicationDataRoot, "GakuranAutoPiano"); // V1 compatibility path.
        string destination = Path.Combine(applicationDataRoot, "PianoRoblox");
        if (Directory.Exists(destination) || !Directory.Exists(previous)) return false;

        // A fresh staging name deliberately ignores leftovers from a terminated process.
        string staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(previous, staging);
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Directory.Exists(destination)) { return false; } // Another app instance won.
            return true;
        }
        finally
        {
            // Never delete or modify the old library or an already published destination.
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            // Junctions are not library data; do not follow links outside the old directory.
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            string target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo directory) CopyDirectory(directory.FullName, target);
            else File.Copy(entry.FullName, target);
        }
    }
}
