using System.Text.Json;
using Xunit;

namespace Scrunch.Tests;

// The note session and the notebook underneath it: what a user can observe
// (files on disk, recoverable discards, refused saves), never serializer internals.
public sealed class NoteSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scrunch-tests-" + Guid.NewGuid().ToString("N"));

    private string Folder(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Notebook(Guid id, string text = "kept", int version = 1, string? deletedAt = null) => $$"""
        {
          "Version": {{version}},
          "Notes": [
            {
              "Id": "{{id}}",
              "Text": "{{text}}",
              "Colour": "yellow",
              "Feel": 1,
              "X": 10,
              "Y": 20,
              "Width": 300,
              "Height": 320,
              "Pinned": false,
              "ReducedMotion": false,
              "CreatedAt": "2024-01-01T00:00:00+00:00",
              "UpdatedAt": "2024-01-01T00:00:00+00:00",
              "DeletedAt": {{(deletedAt == null ? "null" : "\"" + deletedAt + "\"")}}
            }
          ]
        }
        """;

    private static NoteSnapshot SnapshotOf(NoteRecord record) => new(record.Text, record.Colour, record.Feel,
        record.X, record.Y, record.Width, record.Height, record.Pinned, record.ReducedMotion);

    [Fact]
    public void CreateAppliesDefaultsAndPersistsImmediately()
    {
        string folder = Folder("create");
        using (var session = new NoteSession(new NoteStore(folder)))
        {
            session.SetDefaults(new NoteDefaults { Colour = "mint", Pinned = true, ReducedMotion = true });
            var record = session.Create(120, 240, out var error);
            Assert.Null(error);
            Assert.NotNull(record);
            Assert.Equal("mint", record.Colour);
            Assert.True(record.Pinned);
            Assert.True(record.ReducedMotion);
            Assert.Equal(120, record.X);
            Assert.Equal(240, record.Y);
            Assert.False(session.Dirty);
            Assert.Equal(new[] { record.Id }, session.LiveNotes().Select(n => n.Id));
        }
        using var reopened = new NoteStore(folder);
        var stored = Assert.Single(reopened.Document.Notes);
        Assert.Equal("mint", stored.Colour);
        Assert.Equal(120, stored.X);
    }

    [Fact]
    public void ApplyUpdatesRecordAndOnlyDirtiesRealChanges()
    {
        using var session = new NoteSession(new NoteStore(Folder("apply")));
        var record = session.Create(0, 0, out _)!;
        var stamp = record.UpdatedAt;

        Assert.False(session.Apply(record.Id, SnapshotOf(record)));
        Assert.False(session.Dirty);
        Assert.Equal(stamp, record.UpdatedAt);

        Assert.True(session.Apply(record.Id, new NoteSnapshot("Milk and bread", "pink", 2, 33, 44, 320, 300, true, true)));
        Assert.True(session.Dirty);
        Assert.Equal("Milk and bread", record.Text);
        Assert.Equal("pink", record.Colour);
        Assert.Equal(2, record.Feel);
        Assert.Equal(33, record.X);
        Assert.Equal(44, record.Y);
        Assert.Equal(320, record.Width);
        Assert.Equal(300, record.Height);
        Assert.True(record.Pinned);
        Assert.True(record.ReducedMotion);
        Assert.True(record.UpdatedAt >= stamp);

        Assert.False(session.Apply(Guid.NewGuid(), SnapshotOf(record)));
    }

    [Fact]
    public void DiscardTombstonesTheNoteAndSavesIt()
    {
        string folder = Folder("discard");
        var when = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);
        Guid id;
        using (var session = new NoteSession(new NoteStore(folder)))
        {
            var record = session.Create(0, 0, out _)!;
            id = record.Id;
            Assert.True(session.Discard(id, when, out var error));
            Assert.Null(error);
            Assert.Equal(when, record.DeletedAt);
            Assert.Empty(session.LiveNotes());
            Assert.True(session.HasDiscards);
            Assert.False(session.Dirty);
            // A discard is a tombstone: the note is never removed from the notebook.
            Assert.Single(session.Document.Notes);
            Assert.True(session.Discard(id, when.AddHours(1), out _));
            Assert.Equal(when, record.DeletedAt);
        }
        using var reopened = new NoteStore(folder);
        Assert.Equal(when, Assert.Single(reopened.Document.Notes, n => n.Id == id).DeletedAt);
    }

    [Fact]
    public void UndoRestoresTheNewestDiscardAndClearsItsTombstone()
    {
        string folder = Folder("undo");
        using (var session = new NoteSession(new NoteStore(folder)))
        {
            var older = session.Create(0, 0, out _)!;
            var newer = session.Create(10, 10, out _)!;
            Assert.True(session.Discard(newer.Id, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), out _));
            Assert.True(session.Discard(older.Id, new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), out _));

            var restored = session.UndoLastDiscard(out var error);
            Assert.Null(error);
            Assert.Equal(older.Id, restored!.Id);
            Assert.Null(restored.DeletedAt);
            Assert.False(session.Dirty);

            Assert.Equal(newer.Id, session.UndoLastDiscard(out _)!.Id);
            Assert.False(session.HasDiscards);
            Assert.Equal(2, session.LiveNotes().Length);
        }
        using var reopened = new NoteStore(folder);
        Assert.All(reopened.Document.Notes, n => Assert.Null(n.DeletedAt));
    }

    [Fact]
    public void UndoWithNothingDiscardedReturnsNull()
    {
        using var session = new NoteSession(new NoteStore(Folder("undo-empty")));
        session.Create(0, 0, out _);
        Assert.Null(session.UndoLastDiscard(out var error));
        Assert.Null(error);
        Assert.False(session.Dirty);
    }

    [Fact]
    public void RefusedSaveReportsAnErrorAndLeavesTheNotebookIntact()
    {
        string folder = Folder("refused");
        using var session = new NoteSession(new NoteStore(folder));
        var record = session.Create(0, 0, out _)!;
        string path = Path.Combine(folder, "notes.json");
        byte[] published = File.ReadAllBytes(path);

        Assert.True(session.Apply(record.Id, new NoteSnapshot("unsaveable", "blue", 0, 1, 2, 300, 320, false, false)));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(session.TrySave(out var error));
            Assert.NotNull(error);
            Assert.Contains("could not be saved", error);
            // Still dirty: the next attempt (the notice's retry button) must write.
            Assert.True(session.Dirty);
        }
        Assert.Equal(published, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));

        Assert.True(session.TrySave(out var retried));
        Assert.Null(retried);
        Assert.Contains("unsaveable", File.ReadAllText(path));
    }

    [Fact]
    public void FailedSaveLeavesNoTemporaryFileBehind()
    {
        string folder = Folder("temp-file");
        using var store = new NoteStore(folder);
        store.Save();
        string path = Path.Combine(folder, "notes.json");
        store.Document.Notes.Add(new NoteRecord { Text = "half written" });
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => store.Save());
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void MigrationCopiesOnlyTheNotebookAndItsBackup()
    {
        string legacy = Folder("legacy");
        string destination = Path.Combine(_root, "migrated");
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(legacy, "notes.json"), Notebook(id, "legacy note"));
        File.WriteAllText(Path.Combine(legacy, "notes.json.bak"), Notebook(id, "legacy backup"));
        File.WriteAllText(Path.Combine(legacy, "notes.json.tmp"), "{ interrupted write");
        File.WriteAllText(Path.Combine(legacy, "notes.json.damaged-01"), "{ damaged");

        StorageMigration.Migrate(legacy, destination);

        Assert.True(File.Exists(Path.Combine(destination, "notes.json")));
        Assert.True(File.Exists(Path.Combine(destination, "notes.json.bak")));
        Assert.False(File.Exists(Path.Combine(destination, "notes.json.tmp")));
        Assert.Empty(Directory.EnumerateFiles(destination, "notes.json.damaged-*"));
        Assert.Contains("legacy note", File.ReadAllText(Path.Combine(destination, "notes.json")));
        Assert.Contains("legacy backup", File.ReadAllText(Path.Combine(destination, "notes.json.bak")));
        // The legacy notebook and every sibling are left untouched.
        Assert.Contains("legacy note", File.ReadAllText(Path.Combine(legacy, "notes.json")));
        Assert.Equal("{ interrupted write", File.ReadAllText(Path.Combine(legacy, "notes.json.tmp")));
        Assert.Equal("{ damaged", File.ReadAllText(Path.Combine(legacy, "notes.json.damaged-01")));
        // No staging folder is left behind on success.
        Assert.Empty(Directory.EnumerateDirectories(_root, "migrated.migration-*"));
    }

    [Fact]
    public void ANewerNotebookIsRefusedAndBothFilesAreLeftUntouched()
    {
        string folder = Folder("v2");
        string path = Path.Combine(folder, "notes.json");
        string backup = path + ".bak";
        File.WriteAllText(path, Notebook(Guid.NewGuid(), "from the future", version: 2));
        File.WriteAllText(backup, Notebook(Guid.NewGuid(), "older copy"));
        byte[] primary = File.ReadAllBytes(path), previous = File.ReadAllBytes(backup);

        Assert.Throws<NotSupportedException>(() => new NoteStore(folder));

        Assert.Equal(primary, File.ReadAllBytes(path));
        Assert.Equal(previous, File.ReadAllBytes(backup));
    }

    [Fact]
    public void ADocumentWithoutNotesIsRejected()
    {
        string folder = Folder("missing-notes");
        File.WriteAllText(Path.Combine(folder, "notes.json"), """{ "Version": 1 }""");
        var failure = Assert.Throws<InvalidDataException>(() => new NoteStore(folder));
        Assert.IsType<JsonException>(failure.InnerException);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }
}
