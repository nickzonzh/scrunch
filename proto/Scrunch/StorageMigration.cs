namespace Scrunch;

// Copy, validate, then atomically publish the new directory. The legacy notebook
// and all its backups are left intact. A destination notebook always wins.
internal static class StorageMigration
{
    internal static void Migrate(string legacy, string destination)
    {
        bool HasNotebook(string path) => File.Exists(Path.Combine(path, "notes.json")) || File.Exists(Path.Combine(path, "notes.json.bak"));
        if (HasNotebook(destination) || !HasNotebook(legacy)) return;
        if (Directory.Exists(destination))
            throw new IOException("The Scrunch data folder already exists without a notebook. Your previous notes were left untouched; inspect both folders before retrying migration.");

        // Reuse the exact reader and exclusive-writer lease. This rejects a newer
        // schema, supports backup recovery, and refuses migration while an old app
        // is writing. Do not Save(): preserve source bytes and recovery semantics.
        using var source = new NoteStore(legacy);
        string staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        foreach (var file in Directory.EnumerateFiles(legacy, "notes.json*"))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A linked notebook file needs manual migration. The original was left untouched.");
            string copy = Path.Combine(staging, Path.GetFileName(file));
            using var input = File.OpenRead(file);
            using var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        using (var validation = new NoteStore(staging)) { }
        // If another process created destination meanwhile, Move fails instead of
        // merging or overwriting. Interrupted staging copies remain recoverable.
        Directory.Move(staging, destination);
    }
}
