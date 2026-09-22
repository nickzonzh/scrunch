using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scrunch;

public sealed class NoteRecord
{
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired] public string Text { get; set; } = "";
    public string Colour { get; set; } = "yellow";
    public int Feel { get; set; } = 1;
    public int X { get; set; } = 640;
    public int Y { get; set; } = 320;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 320;
    public bool Pinned { get; set; }
    public bool ReducedMotion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class NoteDefaults
{
    public string Colour { get; set; } = "yellow";
    public bool Pinned { get; set; }
    public bool ReducedMotion { get; set; }
}

public sealed class NoteDocument
{
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public List<NoteRecord> Notes { get; set; } = new();
    public NoteDefaults Defaults { get; set; } = new();
    public NoteTypography Typography { get; set; } = new();
}

public sealed class NoteTypography
{
    public const double MinimumSize = 12;
    public const double MaximumSize = 36;
    public string Font { get; set; } = "drawably";
    public double Size { get; set; } = 23;
    [JsonIgnore] public string ResourceKey => Font == "inter" ? "ShellFontFamily" : "NoteFontFamily";

    public void Normalize()
    {
        if (Font is not ("drawably" or "inter")) Font = "drawably";
        if (!double.IsFinite(Size) || Size < MinimumSize || Size > MaximumSize) Size = 23;
    }
}

// Trimming disables reflection serialization: the notebook is serialized through
// generated metadata only. [JsonRequired] and [JsonIgnore] keep working.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(NoteDocument))]
internal sealed partial class NoteJsonContext : JsonSerializerContext;

// One writer, durable atomic replacement, and the previous successful snapshot.
// Shared with the headless storage checks; no Windows UI dependency.
public sealed class NoteStore : IDisposable
{
    private readonly FileStream _lease;
    private readonly string _path;
    public string DirectoryPath => Path.GetDirectoryName(_path)!;
    private bool _primaryValid;
    public NoteDocument Document { get; private set; } = new();
    public string? RecoveryMessage { get; private set; }
    public NoteStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "notes.json");
        _lease = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { Load(); }
        catch { _lease.Dispose(); throw; }
    }

    private static NoteDocument Read(string path)
    {
        var document = JsonSerializer.Deserialize(File.ReadAllText(path), NoteJsonContext.Default.NoteDocument)
            ?? throw new InvalidDataException("The note file is empty.");
        // Version policy, in one place: a newer file is refused outright and both
        // files are left untouched. When a v2 ever exists, an older document is
        // upgraded in place right here. No migration framework.
        if (document.Version != 1) throw new NotSupportedException("This note file needs a newer version of Scrunch.");
        // Older files have no defaults. Invalid optional preferences must not strand valid notes.
        document.Defaults ??= new();
        document.Typography ??= new();
        document.Typography.Normalize();
        if (!new[] { "yellow", "pink", "mint", "blue", "lavender", "peach" }.Contains(document.Defaults.Colour))
            document.Defaults.Colour = "yellow";
        if (document.Notes == null || document.Notes.Any(n => n == null || n.Id == Guid.Empty || n.Text == null ||
            !double.IsFinite(n.Width) || !double.IsFinite(n.Height) || n.Width < 220 || n.Width > 440 ||
            n.Height < 180 || n.Height > 440 || n.Feel < 0 || n.Feel > 2 ||
            !new[] { "yellow", "pink", "mint", "blue", "lavender", "peach" }.Contains(n.Colour)) ||
            document.Notes.Select(n => n.Id).Distinct().Count() != document.Notes.Count)
            throw new InvalidDataException("The note file contains invalid notes.");
        return document;
    }

    private void Load()
    {
        if (File.Exists(_path))
        {
            try { Document = Read(_path); _primaryValid = true; return; }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                if (!File.Exists(_path + ".bak")) throw new InvalidDataException("Scrunch could not read your notes. The original file has been left untouched.", e);
            }
        }
        if (File.Exists(_path + ".bak"))
        {
            Document = Read(_path + ".bak");
            RecoveryMessage = "Recovered the previous saved copy. The latest edits may be missing; the damaged file will be kept for recovery.";
        }
    }

    public void Save()
    {
        string temporary = _path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, Document, NoteJsonContext.Default.NoteDocument);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path) && _primaryValid) File.Replace(temporary, _path, _path + ".bak");
            else
            {
                if (File.Exists(_path)) File.Copy(_path, _path + ".damaged-" + Guid.NewGuid().ToString("N"));
                File.Move(temporary, _path, overwrite: true);
            }
        }
        catch
        {
            // A failed write must not leave a half-written or empty notes.json.tmp
            // beside the notebook: it is indistinguishable from a damaged file.
            try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw;
        }
        _primaryValid = true;
    }

    public void Dispose() => _lease.Dispose();
}
