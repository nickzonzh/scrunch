using Noot_Proto;
using System.Text.Json;

string directory = Path.Combine(Path.GetTempPath(), "noot-storage-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
var id = Guid.NewGuid();
string path = Path.Combine(directory, "notes.json");
using (var store = new NoteStore(directory))
{
    Check(store.Document.Notes.Count == 0, "First launch has no invented notes");
    bool locked = false;
    try { using var second = new NoteStore(directory); } catch (IOException) { locked = true; }
    Check(locked, "A second process cannot become a writer");
    store.Document.Notes.Add(new NoteRecord { Id = id, Text = "Milk 🥛\n日本語", X = -1000, Y = 220, Width = 340, Height = 240, Pinned = true, Colour = "mint", Feel = 0, ReducedMotion = true });
    store.Document.Defaults = new NoteDefaults { Colour = "lavender", Pinned = true, ReducedMotion = true };
    store.Save();
    store.Document.Notes[0].Text += "\nA walk";
    store.Save();
    Check(File.Exists(path + ".bak"), "Replacing a save keeps a backup");
}
using (var store = new NoteStore(directory))
{
    var note = store.Document.Notes.Single();
    Check(store.Document.Defaults.Colour == "lavender" && store.Document.Defaults.Pinned && store.Document.Defaults.ReducedMotion, "New-note defaults survive restart alongside saved notes");
    Check(note.Id == id && note.Text == "Milk 🥛\n日本語\nA walk" && note.X == -1000 && note.Y == 220 && note.Width == 340 && note.Height == 240 && note.Pinned && note.Colour == "mint" && note.Feel == 0 && note.ReducedMotion, "Restart round-trip preserves text, geometry and preferences");
    note.DeletedAt = DateTimeOffset.UtcNow; store.Save();
}
using (var store = new NoteStore(directory))
{
    Check(store.Document.Notes.Single().DeletedAt != null, "Discard remains recoverable after restart");
    store.Document.Notes[0].DeletedAt = null; store.Save();
}
File.WriteAllText(path + ".tmp", "interrupted write");
using (var store = new NoteStore(directory)) Check(store.Document.Notes[0].DeletedAt == null, "An interrupted temporary write does not replace the committed state");
using (var store = new NoteStore(directory))
using (var held = new FileStream(path + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    string committed = File.ReadAllText(path);
    store.Document.Notes[0].Text = "This write must fail";
    bool failed = false;
    try { store.Save(); } catch (IOException) { failed = true; }
    Check(failed && File.ReadAllText(path) == committed, "A failed save leaves the committed notes intact and reports failure");
}
File.WriteAllText(path, "broken JSON");
using (var store = new NoteStore(directory))
{
    Check(store.RecoveryMessage != null && store.Document.Notes.Single().Id == id, "Corrupt primary recovers the previous copy with a warning");
    store.Save();
    Check(Directory.GetFiles(directory, "notes.json.damaged-*").Any(f => File.ReadAllText(f) == "broken JSON"), "Recovery preserves damaged input for inspection");
}
File.WriteAllText(path, "{\"Version\":99,\"Notes\":[]}");
bool futureRejected = false;
try { using var store = new NoteStore(directory); } catch (NotSupportedException) { futureRejected = true; }
Check(futureRejected && File.ReadAllText(path).Contains("99"), "A newer schema is never silently replaced by an older backup");
string isolated = Path.Combine(directory, "invalid");
Directory.CreateDirectory(isolated);
File.WriteAllText(Path.Combine(isolated, "notes.json"), JsonSerializer.Serialize(new NoteDocument { Notes = new() { new NoteRecord { Width = -1 } } }));
bool invalidRejected = false;
try { using var store = new NoteStore(isolated); } catch (InvalidDataException) { invalidRejected = true; }
Check(invalidRejected, "Invalid dimensions fail closed without an available backup");
File.WriteAllText(Path.Combine(isolated, "notes.json"), "{}");
bool incompleteRejected = false;
try { using var store = new NoteStore(isolated); } catch (InvalidDataException) { incompleteRejected = true; }
Check(incompleteRejected, "Missing schema fields cannot masquerade as an empty notebook");
string legacy = Path.Combine(directory, "legacy");
Directory.CreateDirectory(legacy);
string legacyPath = Path.Combine(legacy, "notes.json");
File.WriteAllText(legacyPath, "{\"Version\":1,\"Notes\":[]}");
using (var store = new NoteStore(legacy))
    Check(store.Document.Defaults.Colour == "yellow" && !store.Document.Defaults.Pinned && !store.Document.Defaults.ReducedMotion, "Existing Noot files without defaults retain the original new-note behaviour");
File.WriteAllText(legacyPath, "{\"Version\":1,\"Notes\":[],\"Defaults\":null}");
using (var store = new NoteStore(legacy)) Check(store.Document.Defaults.Colour == "yellow", "Null optional defaults do not strand saved notes");
File.WriteAllText(legacyPath, "{\"Version\":1,\"Notes\":[],\"Defaults\":{\"Colour\":\"unknown\"}}");
using (var store = new NoteStore(legacy)) Check(store.Document.Defaults.Colour == "yellow", "Unknown default colour safely falls back to yellow");
Console.WriteLine($"{checks} storage checks passed. Evidence: {directory}");
