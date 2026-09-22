using Xunit;

namespace Scrunch.Tests;

// Observable notebook behaviour only: files on disk, recovery and migration.
// Nothing here depends on how the document is serialized.
public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scrunch-tests-" + Guid.NewGuid().ToString("N"));

    private string Dir(string name) => Path.Combine(_root, name);

    private static string Notebook(string directory) => Path.Combine(directory, "notes.json");

    // Two saves so the notebook also owns a backup, as a used notebook does.
    private static Guid Seed(string directory)
    {
        var id = Guid.NewGuid();
        using var store = new NoteStore(directory);
        store.Document.Notes.Add(new NoteRecord
        {
            Id = id,
            Text = "Milk 🥛\n日本語",
            X = -1000,
            Y = 220,
            Width = 340,
            Height = 240,
            Pinned = true,
            Colour = "mint",
            Feel = 0,
            ReducedMotion = true
        });
        store.Document.Defaults = new NoteDefaults { Colour = "lavender", Pinned = true, ReducedMotion = true };
        store.Document.Typography = new NoteTypography { Font = "inter", Size = 28 };
        store.Save();
        store.Document.Notes[0].Text += "\nA walk";
        store.Save();
        return id;
    }

    private string Legacy(string name, string text = "Migration fixture")
    {
        string directory = Dir(name);
        using var store = new NoteStore(directory);
        store.Document.Notes.Add(new NoteRecord { Text = text });
        store.Save();
        store.Document.Defaults.Pinned = true;
        store.Save();
        return directory;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void FirstLaunchHasNoInventedNotes()
    {
        using var store = new NoteStore(Dir("fresh"));
        Assert.Empty(store.Document.Notes);
    }

    [Fact]
    public void ASecondProcessCannotBecomeAWriter()
    {
        string directory = Dir("lease");
        using var store = new NoteStore(directory);
        Assert.ThrowsAny<IOException>(() => new NoteStore(directory));
    }

    [Fact]
    public void ReplacingASaveKeepsABackup()
    {
        string directory = Dir("backup");
        Seed(directory);
        Assert.True(File.Exists(Notebook(directory) + ".bak"), "Replacing a save keeps a backup");
    }

    [Fact]
    public void RestartRoundTripPreservesTextGeometryAndPreferences()
    {
        string directory = Dir("round-trip");
        var id = Seed(directory);
        using var store = new NoteStore(directory);
        var note = Assert.Single(store.Document.Notes);
        Assert.Equal(id, note.Id);
        Assert.Equal("Milk 🥛\n日本語\nA walk", note.Text);
        Assert.Equal(-1000, note.X);
        Assert.Equal(220, note.Y);
        Assert.Equal(340d, note.Width);
        Assert.Equal(240d, note.Height);
        Assert.True(note.Pinned);
        Assert.Equal("mint", note.Colour);
        Assert.Equal(0, note.Feel);
        Assert.True(note.ReducedMotion);
    }

    [Fact]
    public void NewNoteDefaultsSurviveRestartAlongsideSavedNotes()
    {
        string directory = Dir("defaults");
        Seed(directory);
        using var store = new NoteStore(directory);
        Assert.Equal("lavender", store.Document.Defaults.Colour);
        Assert.True(store.Document.Defaults.Pinned);
        Assert.True(store.Document.Defaults.ReducedMotion);
        Assert.Single(store.Document.Notes);
    }

    [Fact]
    public void FontAndSizeSurviveRestartWithoutChangingNoteContents()
    {
        string directory = Dir("typography");
        Seed(directory);
        using var store = new NoteStore(directory);
        Assert.Equal("inter", store.Document.Typography.Font);
        Assert.Equal(28d, store.Document.Typography.Size);
        Assert.Equal("Milk 🥛\n日本語\nA walk", store.Document.Notes.Single().Text);
    }

    [Fact]
    public void DiscardRemainsRecoverableAfterRestart()
    {
        string directory = Dir("discard");
        Seed(directory);
        using (var store = new NoteStore(directory))
        {
            store.Document.Notes[0].DeletedAt = DateTimeOffset.UtcNow;
            store.Save();
        }
        using (var store = new NoteStore(directory))
            Assert.NotNull(store.Document.Notes.Single().DeletedAt);
    }

    [Fact]
    public void AnInterruptedTemporaryWriteDoesNotReplaceTheCommittedState()
    {
        string directory = Dir("interrupted");
        Seed(directory);
        File.WriteAllText(Notebook(directory) + ".tmp", "interrupted write");
        using var store = new NoteStore(directory);
        Assert.Null(store.Document.Notes.Single().DeletedAt);
        Assert.Equal("Milk 🥛\n日本語\nA walk", store.Document.Notes.Single().Text);
    }

    [Fact]
    public void AFailedSaveLeavesTheCommittedNotesIntactAndReportsFailure()
    {
        string directory = Dir("failed-save");
        Seed(directory);
        using var store = new NoteStore(directory);
        string committed = File.ReadAllText(Notebook(directory));
        // Hold the temporary file open so the atomic replacement cannot happen.
        using var held = new FileStream(Notebook(directory) + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        store.Document.Notes[0].Text = "This write must fail";
        Assert.ThrowsAny<IOException>(store.Save);
        Assert.Equal(committed, File.ReadAllText(Notebook(directory)));
    }

    [Fact]
    public void CorruptPrimaryRecoversThePreviousCopyWithAWarning()
    {
        string directory = Dir("corrupt");
        var id = Seed(directory);
        File.WriteAllText(Notebook(directory), "broken JSON");
        using var store = new NoteStore(directory);
        Assert.NotNull(store.RecoveryMessage);
        Assert.Equal(id, store.Document.Notes.Single().Id);
    }

    [Fact]
    public void RecoveryPreservesTheDamagedInputForInspection()
    {
        string directory = Dir("damaged");
        Seed(directory);
        File.WriteAllText(Notebook(directory), "broken JSON");
        using var store = new NoteStore(directory);
        store.Save();
        Assert.Contains(Directory.GetFiles(directory, "notes.json.damaged-*"), f => File.ReadAllText(f) == "broken JSON");
    }

    [Fact]
    public void ANewerSchemaIsNeverSilentlyReplacedByAnOlderBackup()
    {
        string directory = Dir("future");
        Seed(directory);
        File.WriteAllText(Notebook(directory), "{\"Version\":99,\"Notes\":[]}");
        Assert.Throws<NotSupportedException>(() => new NoteStore(directory));
        Assert.Contains("99", File.ReadAllText(Notebook(directory)));
    }

    [Fact]
    public void InvalidDimensionsFailClosedWithoutAnAvailableBackup()
    {
        string directory = Dir("invalid");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Notebook(directory),
            "{\"Version\":1,\"Notes\":[{\"Id\":\"" + Guid.NewGuid() + "\",\"Text\":\"\",\"Width\":-1}]}");
        Assert.Throws<InvalidDataException>(() => new NoteStore(directory));
    }

    [Fact]
    public void MissingSchemaFieldsCannotMasqueradeAsAnEmptyNotebook()
    {
        string directory = Dir("incomplete");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Notebook(directory), "{}");
        Assert.Throws<InvalidDataException>(() => new NoteStore(directory));
    }

    [Fact]
    public void ExistingFilesWithoutDefaultsRetainTheOriginalNewNoteBehaviour()
    {
        string directory = Dir("legacy-defaults");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Notebook(directory), "{\"Version\":1,\"Notes\":[]}");
        using var store = new NoteStore(directory);
        Assert.Equal("yellow", store.Document.Defaults.Colour);
        Assert.False(store.Document.Defaults.Pinned);
        Assert.False(store.Document.Defaults.ReducedMotion);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Notes\":[]}")]
    [InlineData("{\"Version\":1,\"Notes\":[],\"Typography\":null}")]
    [InlineData("{\"Version\":1,\"Notes\":[],\"Typography\":{\"Font\":\"missing-font\",\"Size\":500}}")]
    public void MissingOrInvalidTypographyKeepsTheOriginalHandwritingAndSize(string document)
    {
        string directory = Dir("legacy-typography-" + document.Length);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Notebook(directory), document);
        using var store = new NoteStore(directory);
        Assert.Equal("drawably", store.Document.Typography.Font);
        Assert.Equal(23d, store.Document.Typography.Size);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Notes\":[],\"Defaults\":null}")]
    [InlineData("{\"Version\":1,\"Notes\":[],\"Defaults\":{\"Colour\":\"unknown\"}}")]
    public void MissingOrUnknownDefaultColourFallsBackToYellowWithoutStrandingNotes(string document)
    {
        string directory = Dir("legacy-colour-" + document.Length);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Notebook(directory), document);
        using var store = new NoteStore(directory);
        Assert.Equal("yellow", store.Document.Defaults.Colour);
    }

    [Fact]
    public void MigrationRefusesAnActiveLegacyWriter()
    {
        string source = Legacy("active-source");
        string destination = Dir("active-target");
        using var held = new NoteStore(source);
        Assert.ThrowsAny<IOException>(() => StorageMigration.Migrate(source, destination));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void MigrationPreservesSourceBytesPreferencesAndBackups()
    {
        string source = Legacy("bytes-source");
        string destination = Dir("bytes-target");
        string original = File.ReadAllText(Notebook(source));
        StorageMigration.Migrate(source, destination);
        Assert.Equal(original, File.ReadAllText(Notebook(source)));
        Assert.Equal(original, File.ReadAllText(Notebook(destination)));
        Assert.True(File.Exists(Notebook(destination) + ".bak"), "Migration copies the existing backup");
        using var migrated = new NoteStore(destination);
        Assert.True(migrated.Document.Defaults.Pinned);
    }

    [Fact]
    public void RepeatedMigrationNeverOverwritesDestinationNotes()
    {
        string source = Legacy("repeat-source");
        string destination = Dir("repeat-target");
        StorageMigration.Migrate(source, destination);
        using (var store = new NoteStore(destination))
        {
            store.Document.Notes[0].Text = "Newer destination";
            store.Save();
        }
        StorageMigration.Migrate(source, destination);
        using (var store = new NoteStore(destination))
            Assert.Equal("Newer destination", store.Document.Notes[0].Text);
    }

    [Fact]
    public void MigratedCorruptionStillUsesTheOriginalRecoveryRules()
    {
        string source = Legacy("recovery-source");
        File.WriteAllText(Notebook(source), "corrupt primary");
        string destination = Dir("recovery-target");
        StorageMigration.Migrate(source, destination);
        using var store = new NoteStore(destination);
        Assert.NotNull(store.RecoveryMessage);
        Assert.Single(store.Document.Notes);
        store.Save();
        Assert.Single(Directory.GetFiles(destination, "notes.json.damaged-*"));
    }

    [Fact]
    public void MigrationRejectsAFutureSchemaInsteadOfReplacingItWithAnOldBackup()
    {
        string source = Legacy("future-source");
        File.WriteAllText(Notebook(source), "{\"Version\":99,\"Notes\":[]}");
        Assert.Throws<NotSupportedException>(() => StorageMigration.Migrate(source, Dir("future-target")));
    }

    [Fact]
    public void MigrationLeavesAnExistingNonNotebookFolderUntouched()
    {
        string source = Legacy("occupied-source");
        string destination = Dir("occupied-target");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
        Assert.ThrowsAny<IOException>(() => StorageMigration.Migrate(source, destination));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "keep.txt")));
    }
}
