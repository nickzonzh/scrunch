using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Scrunch;

public sealed partial class ShellView : UserControl
{
    private readonly ObservableCollection<NoteListRow> _rows = new();
    private IReadOnlyList<NoteRecord> _notes = Array.Empty<NoteRecord>();
    private bool _loadingDefaults;
    private bool _loadingStartup;
    private bool _loadingTypography = true;
    public event Action? NewRequested;
    public event Action<Guid>? NoteRequested;
    public event Action? UndoRequested;
    public event Action? QuitRequested;
    public event Action? DataFolderRequested;
    public event Action<NoteDefaults>? DefaultsChanged;
    public event Action<NoteTypography>? TypographyChanged;
    public event Action? FitRequested;
    public event Action? DismissRequested;

    public ShellView()
    {
        InitializeComponent();
        NotesList.ItemsSource = _rows;
        _loadingDefaults = true;
        foreach (var key in PaperTokens.Colours.Keys)
            DefaultColour.Items.Add(new ComboBoxItem { Content = char.ToUpperInvariant(key[0]) + key[1..], Tag = key });
        _loadingDefaults = false;
        NoteFont.Items.Add(new ComboBoxItem { Content = "Drawably Pen", Tag = "drawably" });
        NoteFont.Items.Add(new ComboBoxItem { Content = "Inter", Tag = "inter" });
        NoteFontSize.Minimum = NoteTypography.MinimumSize;
        NoteFontSize.Maximum = NoteTypography.MaximumSize;
        ConfigureTypography(new NoteTypography());
        var version = typeof(ShellView).Assembly.GetName().Version;
        VersionLabel.Text = $"Scrunch · {version?.Major}.{version?.Minor}.{version?.Build}";
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => NewRequested?.Invoke());
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, () => { ShowHome(); SearchBox.Focus(FocusState.Keyboard); });
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => UndoRequested?.Invoke());
        AddShortcut(VirtualKey.Escape, VirtualKeyModifiers.None, () =>
        {
            if (SettingsPanel.Visibility == Visibility.Visible) ShowHome();
            else if (SearchBox.Text.Length != 0) SearchBox.Text = "";
            else DismissRequested?.Invoke();
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
        ShortcutNotice.Visibility = shortcutRegistered ? Visibility.Collapsed : Visibility.Visible;
        FitRequested?.Invoke();
    }

    public void UpdateNotes(IEnumerable<NoteRecord> notes)
    {
        _notes = notes.ToArray();
        UndoButton.IsEnabled = _notes.Any(n => n.DeletedAt != null);
        FilterNotes();
    }

    public void ConfigureTypography(NoteTypography typography)
    {
        _loadingTypography = true;
        NoteFont.SelectedItem = NoteFont.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == typography.Font);
        NoteFontSize.Value = typography.Size;
        UpdateFontPreview(typography);
        _loadingTypography = false;
    }

    private void Typography_Changed(object sender, SelectionChangedEventArgs e) => ChangeTypography();
    private void FontSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e) => ChangeTypography();
    private void ChangeTypography()
    {
        if (_loadingTypography || NoteFont.SelectedItem is not ComboBoxItem font || !double.IsFinite(NoteFontSize.Value)) return;
        var typography = new NoteTypography { Font = (string)font.Tag, Size = NoteFontSize.Value };
        UpdateFontPreview(typography);
        TypographyChanged?.Invoke(typography);
    }

    private void UpdateFontPreview(NoteTypography typography)
    {
        FontPreview.FontFamily = (FontFamily)Application.Current.Resources[typography.ResourceKey];
        FontPreview.FontSize = typography.Size;
    }

    public void SetTrayStatus(bool available)
    {
        TrayNotice.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        FitRequested?.Invoke();
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NotesList,
            query.Length > 0 ? $"Notes, {matching.Length} of {active.Length} matching" : $"Notes, {active.Length}");
        EmptyLabel.Text = active.Length == 0 ? "A little thing to remember? Create a note." : "No matching notes. Try another search.";
        EmptyLabel.Visibility = matching.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesList.Visibility = matching.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FitRequested?.Invoke();
    }

    public void ShowHome()
    {
        SettingsPanel.Visibility = Visibility.Collapsed; HomePanel.Visibility = Visibility.Visible;
        SettingsButton.Focus(FocusState.Programmatic);
        FitRequested?.Invoke();
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke();
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();
    private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
    private void DataFolder_Click(object sender, RoutedEventArgs e) => DataFolderRequested?.Invoke();
    private void DismissShortcutNotice_Click(object sender, RoutedEventArgs e)
    {
        ShortcutNotice.Visibility = Visibility.Collapsed;
        SettingsButton.Focus(FocusState.Keyboard);
        FitRequested?.Invoke();
    }
    private void Back_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void Settings_Click(object sender, RoutedEventArgs e)
        => ShowSettings();

    public void ShowSettings()
    {
        RefreshStartup();
        HomePanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible;
        BackButton.Focus(FocusState.Programmatic);
        FitRequested?.Invoke();
    }
    private void RefreshStartup()
    {
        _loadingStartup = true;
        var state = StartupRegistration.Read();
        StartWithWindows.IsEnabled = state.Available;
        StartWithWindows.IsOn = state.Enabled;
        StartupMessage.Text = state.Message;
        RemoveStartup.Visibility = state.BlockedByWindows ? Visibility.Visible : Visibility.Collapsed;
        _loadingStartup = false;
    }
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingStartup) return;
        ChangeStartup(StartWithWindows.IsOn);
    }
    private void RemoveStartup_Click(object sender, RoutedEventArgs e) => ChangeStartup(false);
    private void ChangeStartup(bool enabled)
    {
        string? error = null;
        try { StartupRegistration.SetEnabled(enabled); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        { error = "Could not change start at sign-in. " + e.Message; }
        RefreshStartup();
        if (error != null) StartupMessage.Text = error;
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
