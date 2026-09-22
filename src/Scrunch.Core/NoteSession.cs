using System.Text.Json;

namespace Scrunch;

// The persisted per-note fields, captured from a live editor window. Immutable:
// the session decides whether anything actually changed before touching a record.
public sealed record NoteSnapshot(string Text, string Colour, int Feel, int X, int Y,
    double Width, double Height, bool Pinned, bool ReducedMotion);

// Owns the notebook and the whole save policy: what is dirty, what a durable
// mutation rolls back to when the disk refuses, and the message a user sees.
// No Windows UI: the shell only routes intent in and notices out.
public sealed class NoteSession : IDisposable
{
    private readonly NoteStore _store;
    private bool _dirty;
    private bool _closed;

    public NoteSession(NoteStore store) => _store = store;

    public NoteDocument Document => _store.Document;
    public string DirectoryPath => _store.DirectoryPath;
    public string? RecoveryMessage => _store.RecoveryMessage;
    public bool Dirty => _dirty;
    public bool HasDiscards => Document.Notes.Any(n => n.DeletedAt != null);

    public NoteRecord[] LiveNotes() => Document.Notes.Where(n => n.DeletedAt == null).ToArray();
    public NoteRecord? FindLive(Guid id) => Document.Notes.FirstOrDefault(n => n.Id == id && n.DeletedAt == null);

    public void SetDefaults(NoteDefaults defaults) { Document.Defaults = defaults; _dirty = true; }
    public void SetTypography(NoteTypography typography) { Document.Typography = typography; _dirty = true; }

    // A new note is only handed back once it is on disk: a notebook that cannot
    // be written must not sprout windows whose text has nowhere to go.
    public NoteRecord? Create(int x, int y, out string? error)
    {
        if (_closed) { error = null; return null; }
        var defaults = Document.Defaults;
        var record = new NoteRecord { X = x, Y = y, Colour = defaults.Colour, Pinned = defaults.Pinned, ReducedMotion = defaults.ReducedMotion };
        Document.Notes.Add(record);
        _dirty = true;
        if (TrySave(out error)) return record;
        Document.Notes.Remove(record);
        return null;
    }

    // Reports whether the editor actually moved the record, so continuous idle
    // callbacks (focus, layout, position echoes) never schedule a save.
    public bool Apply(Guid id, NoteSnapshot snapshot)
    {
        if (_closed) return false;
        var record = Document.Notes.FirstOrDefault(n => n.Id == id);
        if (record == null) return false;
        if (record.Text == snapshot.Text && record.Colour == snapshot.Colour && record.Feel == snapshot.Feel &&
            record.X == snapshot.X && record.Y == snapshot.Y &&
            record.Width == snapshot.Width && record.Height == snapshot.Height &&
            record.Pinned == snapshot.Pinned && record.ReducedMotion == snapshot.ReducedMotion) return false;
        record.Text = snapshot.Text; record.Colour = snapshot.Colour; record.Feel = snapshot.Feel;
        record.X = snapshot.X; record.Y = snapshot.Y;
        record.Width = snapshot.Width; record.Height = snapshot.Height;
        record.Pinned = snapshot.Pinned; record.ReducedMotion = snapshot.ReducedMotion;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        _dirty = true;
        return true;
    }

    // A discard is a tombstone, never a deletion, and it is committed before the
    // window animates away. A refused save leaves the note exactly as it was.
    public bool Discard(Guid id, DateTimeOffset when, out string? error)
    {
        error = null;
        if (_closed) return false;
        var record = Document.Notes.FirstOrDefault(n => n.Id == id);
        if (record == null) return false;
        if (record.DeletedAt != null) return true;
        record.DeletedAt = when;
        _dirty = true;
        if (TrySave(out error)) return true;
        record.DeletedAt = null;
        return false;
    }

    // Newest tombstone wins and there is no age limit: a discarded note stays
    // recoverable across restarts (README, "Data and privacy").
    public NoteRecord? UndoLastDiscard(out string? error)
    {
        error = null;
        if (_closed) return null;
        var record = Document.Notes.Where(n => n.DeletedAt != null).OrderByDescending(n => n.DeletedAt).FirstOrDefault();
        if (record == null) return null;
        var previous = record.DeletedAt;
        record.DeletedAt = null;
        _dirty = true;
        if (TrySave(out error)) return record;
        record.DeletedAt = previous;
        return null;
    }

    public bool TrySave(out string? error)
    {
        error = null;
        // Late window callbacks can arrive after the lease is gone. There is
        // nothing left to write then: the final save already ran in PrepareQuit.
        if (_closed || !_dirty) return true;
        try { _store.Save(); _dirty = false; return true; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "Your latest changes could not be saved. Keep Scrunch open and check available disk space and access. " + failure.Message;
            return false;
        }
    }

    public void Dispose() { _closed = true; _store.Dispose(); }
}
