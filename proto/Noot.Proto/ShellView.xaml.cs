using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Noot_Proto;

public sealed partial class ShellView : UserControl
{
    private readonly ObservableCollection<NoteListRow> _rows = new();
    private IReadOnlyList<NoteRecord> _notes = Array.Empty<NoteRecord>();
    private bool _loadingDefaults;
    public event Action? NewRequested;
    public event Action<Guid>? NoteRequested;
    public event Action? UndoRequested;
    public event Action? QuitRequested;
    public event Action? DataFolderRequested;
    public event Action<NoteDefaults>? DefaultsChanged;

    public ShellView()
    {
        InitializeComponent();
        NotesList.ItemsSource = _rows;
        _loadingDefaults = true;
        foreach (var key in PaperTokens.Colours.Keys)
            DefaultColour.Items.Add(new ComboBoxItem { Content = char.ToUpperInvariant(key[0]) + key[1..], Tag = key });
        _loadingDefaults = false;
        var version = typeof(ShellView).Assembly.GetName().Version;
        VersionLabel.Text = $"Scrunch · {version?.Major}.{version?.Minor}.{version?.Build}";
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => NewRequested?.Invoke());
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, () => { ShowHome(); SearchBox.Focus(FocusState.Keyboard); });
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => UndoRequested?.Invoke());
        AddShortcut(VirtualKey.Escape, VirtualKeyModifiers.None, () =>
        {
            if (SettingsPanel.Visibility == Visibility.Visible) ShowHome();
            else SearchBox.Text = "";
        });
    }

    public void Configure(NoteDefaults defaults, bool shortcutRegistered)
    {
        _loadingDefaults = true;
        DefaultColour.SelectedItem = DefaultColour.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == defaults.Colour);
        DefaultPinned.IsOn = defaults.Pinned;
        DefaultReducedMotion.IsOn = defaults.ReducedMotion;
        _loadingDefaults = false;
        ShortcutLabel.Text = shortcutRegistered ? "Ctrl + Alt + N · New note while Scrunch is running"
            : "Ctrl + Alt + N is in use by another app. Use New note or Ctrl + N in Scrunch.";
    }

    public void UpdateNotes(IEnumerable<NoteRecord> notes)
    {
        _notes = notes.ToArray();
        UndoButton.IsEnabled = _notes.Any(n => n.DeletedAt != null);
        FilterNotes();
    }

    private void FilterNotes()
    {
        string query = SearchBox.Text.Trim();
        var active = _notes.Where(n => n.DeletedAt == null).ToArray();
        // Stable creation order: editing a note never makes rows jump under the pointer.
        var matching = active.Where(n => query.Length == 0 || n.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).ToArray();
        var ids = matching.Select(n => n.Id).ToHashSet();
        for (int i = _rows.Count - 1; i >= 0; i--) if (!ids.Contains(_rows[i].Id)) _rows.RemoveAt(i);
        for (int i = 0; i < matching.Length; i++)
        {
            var row = _rows.FirstOrDefault(r => r.Id == matching[i].Id);
            if (row == null) _rows.Insert(i, new NoteListRow(matching[i]));
            else { if (_rows.IndexOf(row) != i) _rows.Move(_rows.IndexOf(row), i); row.Update(matching[i]); }
        }
        CountLabel.Text = query.Length > 0 ? $"{matching.Length} of {active.Length} notes" : active.Length == 1 ? "1 note" : $"{active.Length} notes";
        EmptyLabel.Text = active.Length == 0 ? "A little thing to remember? Create a note." : "No matching notes. Try another search.";
        EmptyLabel.Visibility = matching.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesList.Visibility = matching.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowHome()
    {
        SettingsPanel.Visibility = Visibility.Collapsed; HomePanel.Visibility = Visibility.Visible;
        SettingsButton.Focus(FocusState.Programmatic);
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke();
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();
    private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
    private void DataFolder_Click(object sender, RoutedEventArgs e) => DataFolderRequested?.Invoke();
    private void Back_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HomePanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible;
        BackButton.Focus(FocusState.Programmatic);
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => FilterNotes();
    private void Note_Click(object sender, ItemClickEventArgs e) => NoteRequested?.Invoke(((NoteListRow)e.ClickedItem).Id);
    private void Notes_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && NotesList.SelectedItem is NoteListRow row)
        { NoteRequested?.Invoke(row.Id); e.Handled = true; }
    }
    private void Defaults_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingDefaults && DefaultColour.SelectedItem is ComboBoxItem item)
            DefaultsChanged?.Invoke(new NoteDefaults { Colour = (string)item.Tag, Pinned = DefaultPinned.IsOn, ReducedMotion = DefaultReducedMotion.IsOn });
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        shortcut.Invoked += (_, e) => { action(); e.Handled = true; };
        KeyboardAccelerators.Add(shortcut);
    }
}

public sealed class NoteListRow : INotifyPropertyChanged
{
    public Guid Id { get; }
    public string Title { get; private set; } = "";
    public string Preview { get; private set; } = "";
    public string AccessibleName { get; private set; } = "";
    public SolidColorBrush Swatch { get; private set; } = new();
    private string _text = "\0";
    private string _colour = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public NoteListRow(NoteRecord note) { Id = note.Id; Update(note); }
    public void Update(NoteRecord note)
    {
        if (_text == note.Text && _colour == note.Colour) return;
        _text = note.Text; _colour = note.Colour;
        var lines = note.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Title = lines.Length == 0 ? "New note" : Clip(lines[0]);
        Preview = lines.Length > 1 ? Clip(string.Join(" ", lines.Skip(1))) : lines.Length == 0 ? "Ready when you are" : "Open note";
        AccessibleName = $"{Title}, {note.Colour} note. {Preview}";
        Swatch = new SolidColorBrush(PaperTokens.Colours[note.Colour]);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private static string Clip(string text) => text.Length > 120 ? text[..120] + "…" : text;
}
