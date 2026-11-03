using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 Project Notes review panel.
/// Displays timecode-anchored review notes, resolved/active tracking,
/// jump-to-timecode navigation, and quick note creation.
/// </summary>
public sealed class ProjectNotesWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Action onProjectChanged;
    private readonly StackPanel notesContainer = new();
    private readonly CheckBox hideResolvedCheck = new() { Content = "Hide resolved", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock summaryText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11 };
    private readonly TextBox newNoteBox = new() { Text = "", Height = 56, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
    private readonly TextBox authorBox = new() { Text = "Editor", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox colorCombo = new() { Width = 110, Height = 24, Margin = new Thickness(0, 0, 8, 0) };

    public ProjectNotesWindow(EditorViewModel viewModel, Action projectChanged)
    {
        vm = viewModel;
        onProjectChanged = projectChanged;
        Style = (Style)FindResource(typeof(Window));
        Title = "Project Notes";
        Width = 460;
        Height = 560;
        MinWidth = 360;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var textBrush = (Brush)FindResource("TextBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Notes list
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Add note section

        // Toolbar
        var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var clearAllBtn = new Button
        {
            Content = "Clear All",
            Padding = new Thickness(8, 2, 8, 2)
        };
        DockPanel.SetDock(clearAllBtn, Dock.Right);
        clearAllBtn.Click += (_, _) =>
        {
            if (vm.Project.Notes.Count == 0) return;
            if (MessageBox.Show(this, "Are you sure you want to delete all notes in this project?", "Project Notes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                vm.Editor.ClearNotes();
                onProjectChanged();
                RefreshList();
            }
        };

        var topStack = new StackPanel { Orientation = Orientation.Horizontal };
        hideResolvedCheck.Foreground = textBrush;
        hideResolvedCheck.Checked += (_, _) => RefreshList();
        hideResolvedCheck.Unchecked += (_, _) => RefreshList();
        topStack.Children.Add(hideResolvedCheck);
        topStack.Children.Add(summaryText);

        topBar.Children.Add(clearAllBtn);
        topBar.Children.Add(topStack);
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        // Notes list (Scrollable)
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = headerBrush,
            Padding = new Thickness(4)
        };
        notesContainer.Orientation = Orientation.Vertical;
        scroll.Content = notesContainer;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // Add Note Section
        var bottomGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bottomGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bottomGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        newNoteBox.Background = headerBrush;
        newNoteBox.Foreground = textBrush;
        newNoteBox.BorderBrush = borderBrush;
        newNoteBox.Padding = new Thickness(6);
        Grid.SetRow(newNoteBox, 0);
        bottomGrid.Children.Add(newNoteBox);

        var actionDock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var addBtn = new Button
        {
            Content = "Add at Playhead",
            Padding = new Thickness(10, 4, 10, 4)
        };
        DockPanel.SetDock(addBtn, Dock.Right);
        addBtn.Click += (_, _) =>
        {
            var text = newNoteBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            vm.Editor.AddNote(text, vm.Position, authorBox.Text.Trim(), colorCombo.SelectedIndex);
            newNoteBox.Text = "";
            onProjectChanged();
            RefreshList();
        };
        actionDock.Children.Add(addBtn);

        var inputsStack = new StackPanel { Orientation = Orientation.Horizontal };
        var userLabel = new TextBlock { Text = "User:", Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        authorBox.Background = headerBrush;
        authorBox.Foreground = textBrush;
        authorBox.BorderBrush = borderBrush;

        for (var i = 0; i < TimelineMarker.MarkerColors.Length; i++)
        {
            var hex = TimelineMarker.MarkerColors[i];
            var item = new ComboBoxItem();
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var rect = new Rectangle { Width = 14, Height = 14, Fill = new BrushConverter().ConvertFromString(hex) as Brush, Margin = new Thickness(0, 0, 6, 0) };
            var name = new TextBlock { Text = $"Color {i + 1}", Foreground = textBrush };
            sp.Children.Add(rect); sp.Children.Add(name);
            item.Content = sp;
            colorCombo.Items.Add(item);
        }
        colorCombo.SelectedIndex = 0;

        inputsStack.Children.Add(userLabel);
        inputsStack.Children.Add(authorBox);
        inputsStack.Children.Add(colorCombo);
        actionDock.Children.Add(inputsStack);

        Grid.SetRow(actionDock, 1);
        bottomGrid.Children.Add(actionDock);

        Grid.SetRow(bottomGrid, 2);
        root.Children.Add(bottomGrid);

        Content = root;
        RefreshList();
    }

    private void RefreshList()
    {
        notesContainer.Children.Clear();
        var notes = vm.Project.Notes;
        var activeCount = notes.Count(n => n.Status == ProjectNoteStatus.Active);
        var resolvedCount = notes.Count(n => n.Status == ProjectNoteStatus.Resolved);
        summaryText.Text = $"{activeCount} active, {resolvedCount} resolved";

        var textBrush = (Brush)FindResource("TextBrush");
        var panelBrush = (Brush)FindResource("PanelBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");

        var hideResolved = hideResolvedCheck.IsChecked == true;

        foreach (var note in notes)
        {
            if (hideResolved && note.Status == ProjectNoteStatus.Resolved) continue;

            var noteBorder = new Border
            {
                Background = panelBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 6, 8, 6)
            };

            var noteGrid = new Grid();
            noteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); // Color strip
            noteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content

            // Color indicator strip
            var colorHex = TimelineMarker.MarkerColors[Math.Clamp(note.OverrideColorId, 0, TimelineMarker.MarkerColors.Length - 1)];
            var colorStrip = new Rectangle
            {
                Fill = new BrushConverter().ConvertFromString(colorHex) as Brush,
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(colorStrip, 0);
            noteGrid.Children.Add(colorStrip);

            var contentStack = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };

            // Header row: Jump timecode button, user, timestamp, resolve toggle, delete button
            var headerDock = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 4) };

            var tcBtn = new Button
            {
                Content = $"⏱ {note.TimecodeMarker}",
                Padding = new Thickness(6, 1, 6, 1),
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Jump playhead to this note's frame"
            };
            tcBtn.Click += (_, _) =>
            {
                vm.Position = note.Frame;
                onProjectChanged();
            };
            DockPanel.SetDock(tcBtn, Dock.Left);
            headerDock.Children.Add(tcBtn);

            var labelText = !string.IsNullOrWhiteSpace(note.OverrideLabel) ? note.OverrideLabel : "Note";
            var userText = new TextBlock
            {
                Text = $"{labelText} • {note.Timestamp}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(userText, Dock.Left);
            headerDock.Children.Add(userText);

            var delBtn = new Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                FontSize = 10,
                ToolTip = "Delete note"
            };
            DockPanel.SetDock(delBtn, Dock.Right);
            delBtn.Click += (_, _) =>
            {
                vm.Editor.RemoveNote(note.Id);
                onProjectChanged();
                RefreshList();
            };
            headerDock.Children.Add(delBtn);

            var resolveCheck = new CheckBox
            {
                Content = "Resolved",
                IsChecked = note.Status == ProjectNoteStatus.Resolved,
                FontSize = 10,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(resolveCheck, Dock.Right);
            resolveCheck.Checked += (_, _) =>
            {
                vm.Editor.SetNoteStatus(note.Id, ProjectNoteStatus.Resolved);
                onProjectChanged();
                RefreshList();
            };
            resolveCheck.Unchecked += (_, _) =>
            {
                vm.Editor.SetNoteStatus(note.Id, ProjectNoteStatus.Active);
                onProjectChanged();
                RefreshList();
            };
            headerDock.Children.Add(resolveCheck);

            contentStack.Children.Add(headerDock);

            // Note body
            var bodyText = new TextBlock
            {
                Text = note.Note,
                Foreground = note.Status == ProjectNoteStatus.Resolved ? Brushes.Gray : textBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                TextDecorations = note.Status == ProjectNoteStatus.Resolved ? TextDecorations.Strikethrough : null
            };
            contentStack.Children.Add(bodyText);

            Grid.SetColumn(contentStack, 1);
            noteGrid.Children.Add(contentStack);

            noteBorder.Child = noteGrid;
            notesContainer.Children.Add(noteBorder);
        }

        if (notesContainer.Children.Count == 0)
        {
            notesContainer.Children.Add(new TextBlock
            {
                Text = hideResolved ? "No active project notes." : "No project notes yet. Type a comment below to anchor a note to the timeline.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(8, 16, 8, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }
}
