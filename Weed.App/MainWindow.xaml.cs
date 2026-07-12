using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Weed.Abstractions;
using Weed.Core;
using Weed.PluginHost;
using Weed.Platform.Windows;
using WpfBinding = System.Windows.Data.Binding;

namespace Weed.App;

public partial class MainWindow : Window
{
    private const double StandardLauncherWidth = 680;
    private const double PreviewLauncherWidth = 820;
    private const double PreviewResultsColumnWidth = 330;
    private const double EmptyLauncherHeight = 150;
    private const double BaseLauncherHeight = 156;
    private const double ResultRowChromeHeight = 18;
    private const double ResultRowHeight = 66 + ResultRowChromeHeight;
    private const int VisibleResultRows = 6;
    private const double MaxResultsPanelHeight = VisibleResultRows * ResultRowHeight;
    private const double MinPreviewPanelHeight = 260;
    private const int ResultPageSize = 20;
    private readonly QueryRouter _router;
    private readonly SettingsRepository _settings;
    private readonly IWeedLogger _logger;
    private readonly Action? _hotkeysChanged;
    private readonly LauncherViewModel _state = new();
    private CancellationTokenSource? _queryCts;
    private int _closeOnLostFocusSuspensions;
    private System.Windows.Point? _lastResultsMousePosition;
    private bool _ignoreResultsMouseUntilPointerMoves;
    private string? _hiddenKeywordPrefix;
    private IReadOnlyList<ExternalDependencyStatus> _dependencyStatuses = [];

    public MainWindow(QueryRouter router, SettingsRepository settings, IWeedLogger logger, Action? hotkeysChanged = null)
    {
        _router = router;
        _settings = settings;
        _logger = logger;
        _hotkeysChanged = hotkeysChanged;
        InitializeComponent();
        DataContext = _state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.HasSelectedPreview))
            {
                UpdatePreviewColumn();
            }
        };
        Loaded += async (_, _) => await RefreshResultsAsync();
    }

    private void UpdatePreviewColumn()
    {
        var hasPreview = _state.HasSelectedPreview;
        Width = hasPreview ? PreviewLauncherWidth : StandardLauncherWidth;
        ResultsColumn.Width = hasPreview
            ? new GridLength(PreviewResultsColumnWidth)
            : new GridLength(1, GridUnitType.Star);
        PreviewColumn.Width = hasPreview
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        PreviewPanel.Visibility = hasPreview ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Margin = hasPreview
            ? new Thickness(0, 0, 12, 0)
            : new Thickness(0);
        ApplyResultLayout(_state.Results.Count);
    }

    public void ShowLauncher(string? initialQuery)
    {
        _hiddenKeywordPrefix = null;
        if (initialQuery is not null)
        {
            SearchBox.Text = initialQuery;
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
        else
        {
            SearchBox.Clear();
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        _ = RefreshResultsAsync();
    }

    public void ShowKeywordLauncher(string keyword, string? initialQuery = null)
    {
        _hiddenKeywordPrefix = string.IsNullOrWhiteSpace(keyword)
            ? null
            : TextNormalizer.Normalize(keyword);
        SearchBox.Text = initialQuery ?? string.Empty;
        SearchBox.CaretIndex = SearchBox.Text.Length;

        Show();
        WindowState = WindowState.Normal;
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        _ = RefreshResultsAsync();
    }

    public void ShowSettings()
    {
        var settingsWindow = new SettingsWindow(_router, _settings, _logger, _dependencyStatuses)
        {
            Owner = this,
            HotkeysChanged = _hotkeysChanged
        };
        settingsWindow.Show();
    }

    public void SetDependencyStatuses(IReadOnlyList<ExternalDependencyStatus> statuses) => _dependencyStatuses = statuses;

    public void ShowClipboardPanel()
    {
        ShowKeywordLauncher("clip");
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        await RefreshResultsAsync();
    }

    private async Task RefreshResultsAsync()
    {
        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var token = _queryCts.Token;
        var text = SearchBox.Text;
        var queryText = BuildQueryText(text);

        try
        {
            if (string.IsNullOrWhiteSpace(queryText))
            {
                _state.ClearResults();
                _state.SelectedIndex = -1;
                _state.Status = "Type to search";
                ApplyResultLayout(0);
                return;
            }

            _state.Status = "Searching";
            var results = await _router.QueryAsync(queryText, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _state.SetResults(results, ResultPageSize);

            _state.SelectedIndex = _state.Results.Count > 0 ? 0 : -1;
            _state.Status = ResultStatus();
            ApplyResultLayout(_state.Results.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Query failed.", ex);
            _state.Status = "Query failed";
        }
    }

    private async void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            MoveSelection(5);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            MoveSelection(-5);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            await ExecuteSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var index = KeyToDigitIndex(e.Key);
            if (index >= 0)
            {
                await ExecuteActionAsync(index);
                e.Handled = true;
            }
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_settings.AppSettings.CloseOnLostFocus && _closeOnLostFocusSuspensions == 0 && IsVisible)
        {
            Hide();
        }
    }

    public IDisposable SuspendCloseOnLostFocus()
    {
        _closeOnLostFocusSuspensions++;
        return new DelegateDisposable(() =>
        {
            _closeOnLostFocusSuspensions = Math.Max(0, _closeOnLostFocusSuspensions - 1);
        });
    }

    private void SearchBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SearchBox.SelectAll();
        e.Handled = true;
    }

    private void ResultsList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(ResultsList);
        if (_ignoreResultsMouseUntilPointerMoves)
        {
            if (_lastResultsMousePosition is { } lastSuppressed && IsSameMousePosition(position, lastSuppressed))
            {
                return;
            }

            _ignoreResultsMouseUntilPointerMoves = false;
        }

        if (_lastResultsMousePosition is { } last && IsSameMousePosition(position, last))
        {
            return;
        }

        _lastResultsMousePosition = position;
        var item = FindAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is SearchResultItem result)
        {
            _state.SelectedIndex = _state.Results.IndexOf(result);
            LoadMoreIfSelectionReachedEnd();
        }
    }

    private async void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsScrollbarElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var item = FindAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not SearchResultItem result)
        {
            return;
        }

        _state.SelectedIndex = _state.Results.IndexOf(result);
        e.Handled = true;
        if (result.IsLoadMore)
        {
            LoadMoreResults();
            return;
        }

        await ExecuteResultCommandAsync(result, result.Result.Result.DefaultCommand);
    }

    private void ResultsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsScrollbarElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var item = FindAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not SearchResultItem result)
        {
            return;
        }

        _state.SelectedIndex = _state.Results.IndexOf(result);
        if (result.IsLoadMore)
        {
            e.Handled = true;
            return;
        }

        var menu = BuildResultContextMenu(result);
        if (menu.Items.Count == 0)
        {
            return;
        }

        menu.PlacementTarget = item;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu BuildResultContextMenu(SearchResultItem result)
    {
        var menu = new ContextMenu();
        var actions = OrderedActions(result.Result.Result).ToArray();
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            if (i == 1)
            {
                menu.Items.Add(new Separator());
            }

            var command = action.Command;
            var item = new MenuItem
            {
                Header = action.Title,
                InputGestureText = action.Shortcut ?? string.Empty,
                IsEnabled = !command.Equals("__noop", StringComparison.Ordinal)
            };
            item.Click += async (_, _) => await ExecuteResultCommandAsync(result, command);
            menu.Items.Add(item);
        }

        return menu;
    }

    private static IEnumerable<WeedAction> OrderedActions(WeedResult result)
    {
        var actions = result.Actions;
        var defaultAction = actions.FirstOrDefault(action =>
            action.Command.Equals(result.DefaultCommand, StringComparison.OrdinalIgnoreCase)) ??
            new WeedAction { Command = result.DefaultCommand, Title = "Open", Shortcut = "Enter" };

        yield return defaultAction;
        foreach (var action in actions)
        {
            if (!action.Command.Equals(defaultAction.Command, StringComparison.OrdinalIgnoreCase))
            {
                yield return action;
            }
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse button is released during dispatch.
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.TextBox ||
                source is System.Windows.Controls.Button ||
                source is System.Windows.Controls.ListBox ||
                source is System.Windows.Controls.ListBoxItem ||
                source is System.Windows.Controls.Primitives.ScrollBar ||
                source is System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MoveSelection(int delta)
    {
        if (_state.Results.Count == 0)
        {
            _state.SelectedIndex = -1;
            return;
        }

        _lastResultsMousePosition = Mouse.GetPosition(ResultsList);
        _ignoreResultsMouseUntilPointerMoves = true;

        var currentIndex = _state.SelectedIndex < 0 ? -1 : _state.SelectedIndex;
        var requestedIndex = Math.Max(0, currentIndex + delta);
        _state.EnsureDisplayedThrough(requestedIndex, ResultPageSize);
        _state.SelectedIndex = Math.Clamp(requestedIndex, 0, _state.Results.Count - 1);
        LoadMoreIfSelectionReachedEnd();
        ResultsList.ScrollIntoView(_state.Results[_state.SelectedIndex]);
        _state.Status = ResultStatus();
        ApplyResultLayout(_state.Results.Count);
    }

    private void ResultsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange <= 0 || !_state.HasMoreResults)
        {
            return;
        }

        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 1)
        {
            return;
        }

        var selectedIndex = _state.SelectedIndex;
        _state.LoadMoreResults(ResultPageSize);
        _state.SelectedIndex = Math.Clamp(selectedIndex, 0, _state.Results.Count - 1);
        _state.Status = ResultStatus();
        ApplyResultLayout(_state.Results.Count);
    }

    private void LoadMoreIfSelectionReachedEnd()
    {
        if (!_state.HasMoreResults ||
            _state.SelectedIndex < 0 ||
            _state.SelectedIndex < _state.Results.Count - 1)
        {
            return;
        }

        var selectedIndex = _state.SelectedIndex;
        _state.LoadMoreResults(ResultPageSize);
        _state.SelectedIndex = Math.Clamp(selectedIndex, 0, _state.Results.Count - 1);
    }

    private void ApplyResultLayout(int resultCount)
    {
        ResultsList.Visibility = resultCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollViewer.SetVerticalScrollBarVisibility(
            ResultsList,
            resultCount > VisibleResultRows ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        var resultHeight = Math.Min(
            _state.Results.Take(VisibleResultRows).Sum(item => item.RowHeight + ResultRowChromeHeight),
            MaxResultsPanelHeight);
        if (_state.HasSelectedPreview)
        {
            resultHeight = Math.Max(resultHeight, MinPreviewPanelHeight);
        }

        Height = resultCount <= 0
            ? EmptyLauncherHeight
            : BaseLauncherHeight + resultHeight;
    }

    private static bool IsScrollbarElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ScrollBar ||
                source is System.Windows.Controls.Primitives.Thumb ||
                source is System.Windows.Controls.Primitives.RepeatButton)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T value)
            {
                return value;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private async Task ExecuteSelectedAsync()
    {
        if (_state.SelectedIndex < 0 || _state.SelectedIndex >= _state.Results.Count)
        {
            return;
        }

        var selected = _state.Results[_state.SelectedIndex];
        if (selected.IsLoadMore)
        {
            LoadMoreResults();
            return;
        }

        if (selected.Result.Result.DefaultCommand == "__noop")
        {
            return;
        }

        await ExecuteResultCommandAsync(selected, selected.Result.Result.DefaultCommand);
    }

    private async Task ExecuteActionAsync(int actionIndex)
    {
        if (_state.SelectedIndex < 0 || _state.SelectedIndex >= _state.Results.Count)
        {
            return;
        }

        var selected = _state.Results[_state.SelectedIndex];
        if (selected.IsLoadMore)
        {
            return;
        }

        if (actionIndex >= selected.Result.Result.Actions.Count)
        {
            return;
        }

        await ExecuteResultCommandAsync(selected, selected.Result.Result.Actions[actionIndex].Command);
    }

    private async Task ExecuteResultCommandAsync(SearchResultItem selected, string command)
    {
        using var closeOnLostFocusScope = ShouldKeepLauncherVisibleForCommand(selected.Result.Result, command)
            ? SuspendCloseOnLostFocus()
            : null;
        try
        {
            _state.Status = "Running";
            _logger.Info($"Command started: {selected.Result.Result.PluginId}/{selected.Result.Result.Id} -> {command}");
            var result = await _router.ExecuteAsync(selected.Result.Result, command, CancellationToken.None);

            if (!result.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(result.Message)
                    ? "Command failed"
                    : result.Message;
                _logger.Warn($"Command returned failure: {selected.Result.Result.PluginId}/{selected.Result.Result.Id} -> {command}: {message}");
                _state.Status = message;
                return;
            }

            if (result.Behavior == CommandBehavior.CloseLauncher)
            {
                Hide();
                return;
            }

            if (result.Behavior == CommandBehavior.ShowLauncher)
            {
                ShowLauncher(result.InitialQuery);
                return;
            }

            await RefreshResultsAsync();
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                _state.Status = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Command failed: {command}", ex);
            _state.Status = $"Command failed: {ex.Message}";
        }
    }

    private static bool ShouldKeepLauncherVisibleForCommand(WeedResult result, string command) =>
        result.PluginId.Equals("weed.screenshot", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("screenshot.", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("ocr.", StringComparison.OrdinalIgnoreCase);

    private string BuildQueryText(string visibleText)
    {
        var text = visibleText.Trim();
        if (string.IsNullOrWhiteSpace(_hiddenKeywordPrefix))
        {
            return text;
        }

        return string.IsNullOrWhiteSpace(text)
            ? _hiddenKeywordPrefix
            : $"{_hiddenKeywordPrefix} {text}";
    }

    private void LoadMoreResults()
    {
        var selectedIndex = _state.SelectedIndex;
        _state.LoadMoreResults(ResultPageSize);
        _state.SelectedIndex = Math.Clamp(selectedIndex, 0, _state.Results.Count - 1);
        _state.Status = ResultStatus();
        ApplyResultLayout(_state.Results.Count);
    }

    private string ResultStatus()
    {
        if (_state.TotalResultCount == 0)
        {
            return "No results";
        }

        return _state.HasMoreResults
            ? $"{_state.DisplayedResultCount} of {_state.TotalResultCount} results"
            : $"{_state.TotalResultCount} results";
    }

    private static int KeyToDigitIndex(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 0,
        Key.D2 or Key.NumPad2 => 1,
        Key.D3 or Key.NumPad3 => 2,
        Key.D4 or Key.NumPad4 => 3,
        Key.D5 or Key.NumPad5 => 4,
        Key.D6 or Key.NumPad6 => 5,
        Key.D7 or Key.NumPad7 => 6,
        Key.D8 or Key.NumPad8 => 7,
        Key.D9 or Key.NumPad9 => 8,
        _ => -1
    };

    private static bool IsSameMousePosition(System.Windows.Point left, System.Windows.Point right) =>
        Math.Abs(left.X - right.X) < 0.5 &&
        Math.Abs(left.Y - right.Y) < 0.5;

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public DelegateDisposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose();
        }
    }
}

public sealed class LauncherViewModel : ObservableObject
{
    private readonly List<RankedResult> _allResults = [];
    private int _selectedIndex = -1;
    private string _status = "Ready";
    private int _displayedResultCount;

    public ObservableCollection<SearchResultItem> Results { get; } = [];

    public int TotalResultCount => _allResults.Count;

    public int DisplayedResultCount => _displayedResultCount;

    public bool HasMoreResults => _displayedResultCount < _allResults.Count;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetField(ref _selectedIndex, value))
            {
                OnPropertyChanged(nameof(SelectedDetails));
                OnSelectedPreviewChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string SelectedDetails
    {
        get
        {
            if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
            {
                return string.Empty;
            }

            var selected = Results[SelectedIndex];
            return selected.IsLoadMore ? string.Empty : selected.Result.PluginName;
        }
    }

    public bool HasSelectedPreview => SelectedResultItem?.HasDetailLayout == true;

    public string SelectedPreviewTitle => SelectedResultItem?.Title ?? string.Empty;

    public string SelectedPreviewSubtitle => SelectedResultItem?.Subtitle ?? string.Empty;

    public ImageSource? SelectedPreviewSource => SelectedResultItem?.PreviewSource;

    public string SelectedPreviewText => SelectedResultItem?.DetailText ?? string.Empty;

    public bool SelectedPreviewHasImage => SelectedResultItem?.HasPreviewImage == true;

    public bool SelectedPreviewHasText => SelectedResultItem?.HasDetailText == true;

    private SearchResultItem? SelectedResultItem
    {
        get
        {
            if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
            {
                return null;
            }

            var selected = Results[SelectedIndex];
            return selected.IsLoadMore ? null : selected;
        }
    }

    public void ClearResults()
    {
        _allResults.Clear();
        _displayedResultCount = 0;
        Results.Clear();
        OnPropertyChanged(nameof(TotalResultCount));
        OnPropertyChanged(nameof(DisplayedResultCount));
        OnPropertyChanged(nameof(HasMoreResults));
        OnPropertyChanged(nameof(SelectedDetails));
        OnSelectedPreviewChanged();
    }

    public void SetResults(IEnumerable<RankedResult> results, int pageSize)
    {
        _allResults.Clear();
        _allResults.AddRange(results);
        _displayedResultCount = Math.Min(Math.Max(0, pageSize), _allResults.Count);
        RebuildResults();
    }

    public void LoadMoreResults(int pageSize)
    {
        if (!HasMoreResults)
        {
            return;
        }

        var previousDisplayedResultCount = _displayedResultCount;
        _displayedResultCount = Math.Min(_displayedResultCount + Math.Max(1, pageSize), _allResults.Count);
        AppendResults(previousDisplayedResultCount, _displayedResultCount);
        NotifyResultsChanged();
    }

    public void EnsureDisplayedThrough(int resultIndex, int pageSize)
    {
        while (resultIndex >= Results.Count && HasMoreResults)
        {
            LoadMoreResults(pageSize);
        }
    }

    private void RebuildResults()
    {
        Results.Clear();
        AppendResults(0, _displayedResultCount);
        NotifyResultsChanged();
    }

    private void AppendResults(int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            Results.Add(SearchResultItem.FromRanked(_allResults[i]));
        }
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(TotalResultCount));
        OnPropertyChanged(nameof(DisplayedResultCount));
        OnPropertyChanged(nameof(HasMoreResults));
        OnPropertyChanged(nameof(SelectedDetails));
        OnSelectedPreviewChanged();
    }

    private void OnSelectedPreviewChanged()
    {
        OnPropertyChanged(nameof(HasSelectedPreview));
        OnPropertyChanged(nameof(SelectedPreviewTitle));
        OnPropertyChanged(nameof(SelectedPreviewSubtitle));
        OnPropertyChanged(nameof(SelectedPreviewSource));
        OnPropertyChanged(nameof(SelectedPreviewText));
        OnPropertyChanged(nameof(SelectedPreviewHasImage));
        OnPropertyChanged(nameof(SelectedPreviewHasText));
    }
}

public sealed record SearchResultItem
{
    private const double StandardRowHeight = 66;
    private const int MaxDetailTextLength = 1600;

    public required RankedResult Result { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public string IconGlyph { get; init; } = "*";

    public ImageSource? IconSource { get; init; }

    public ImageSource? PreviewSource { get; init; }

    public string DisplayStyle { get; init; } = "standard";

    public string DetailText { get; init; } = string.Empty;

    public string DetailKind { get; init; } = string.Empty;

    public bool HasDetailLayout { get; init; }

    public bool HasPreviewImage { get; init; }

    public bool HasDetailText { get; init; }

    public double RowHeight { get; init; } = StandardRowHeight;

    public string ActionHint { get; init; } = "Enter";

    public bool IsLoadMore { get; init; }

    public static SearchResultItem FromRanked(RankedResult result)
    {
        var data = result.Result.Data;
        var displayStyle = data.TryGetValue("displayLayout", out var layout) && !string.IsNullOrWhiteSpace(layout)
            ? layout
            : "standard";
        var previewSource = LoadIcon(PreviewImagePath(data));
        var detailText = DetailTextFromData(data);
        var hasPreviewImage = previewSource is not null;
        var hasDetailText = !hasPreviewImage && !string.IsNullOrWhiteSpace(detailText);
        var hasDetailLayout = displayStyle.Equals("detail", StringComparison.OrdinalIgnoreCase) &&
                              (hasPreviewImage || hasDetailText);

        return new()
        {
            Result = result,
            Title = result.Result.Title,
            Subtitle = result.Result.Subtitle ?? string.Empty,
            IconGlyph = result.Result.Icon?.Glyph ?? "*",
            IconSource = LoadIcon(result.Result.Icon?.Path),
            PreviewSource = previewSource,
            DisplayStyle = displayStyle,
            DetailText = detailText,
            DetailKind = data.TryGetValue("detailKind", out var detailKind) ? detailKind : string.Empty,
            HasDetailLayout = hasDetailLayout,
            HasPreviewImage = hasDetailLayout && hasPreviewImage,
            HasDetailText = hasDetailLayout && hasDetailText,
            RowHeight = StandardRowHeight,
            ActionHint = result.Result.Actions.FirstOrDefault(a => a.Command == result.Result.DefaultCommand)?.Shortcut ?? "Enter"
        };
    }

    private static string? PreviewImagePath(IReadOnlyDictionary<string, string> data)
    {
        if (data.TryGetValue("previewImagePath", out var previewImagePath) &&
            !string.IsNullOrWhiteSpace(previewImagePath))
        {
            return previewImagePath;
        }

        return data.TryGetValue("kind", out var kind) &&
               kind.Equals("image", StringComparison.OrdinalIgnoreCase) &&
               data.TryGetValue("objectPath", out var objectPath)
            ? objectPath
            : null;
    }

    private static string DetailTextFromData(IReadOnlyDictionary<string, string> data)
    {
        var text = data.TryGetValue("detailText", out var detailText)
            ? detailText
            : data.TryGetValue("previewText", out var previewText)
                ? previewText
                : string.Empty;

        text = text.Trim();
        return text.Length <= MaxDetailTextLength
            ? text
            : string.Concat(text.AsSpan(0, MaxDetailTextLength), "...");
    }

    private static ImageSource? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsWindow : Window
{
    private readonly QueryRouter _router;
    private readonly SettingsRepository _settings;
    private readonly IWeedLogger _logger;
    private readonly IReadOnlyList<ExternalDependencyStatus> _dependencyStatuses;
    private ContentControl _content = new();
    private readonly List<(SettingsNavItem Item, System.Windows.Controls.Button Button)> _navButtons = [];
    private SettingsNavItem? _selectedNav;

    public Action? HotkeysChanged { get; init; }

    public SettingsWindow(QueryRouter router, SettingsRepository settings, IWeedLogger logger,
        IReadOnlyList<ExternalDependencyStatus>? dependencyStatuses = null)
    {
        _router = router;
        _settings = settings;
        _logger = logger;
        _dependencyStatuses = dependencyStatuses ?? [];
        Title = "Weed Preferences";
        Width = 960;
        Height = 660;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.Resource("BackgroundBrush");
        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"];
        Content = BuildContent();
        ThemeManager.Changed += ThemeManager_Changed;
        Closed += (_, _) => ThemeManager.Changed -= ThemeManager_Changed;
    }

    private sealed record SettingsNavItem(string Id, string Title, string Icon, LoadedPlugin? Plugin = null);

    private sealed class ExternalPluginInstallViewItem
    {
        public required WeedPluginManifest Manifest { get; init; }

        public required string Directory { get; init; }

        public bool IsLoaded { get; init; }

        public string VersionText => $"v{Manifest.Version}";

        public string StatusText => IsLoaded ? "Loaded in this session" : "Installed - restart required";
    }

    private UIElement BuildContent(string? selectedNavId = null)
    {
        _navButtons.Clear();
        _content = new ContentControl();
        var root = new Grid { Background = ThemeManager.Resource("BackgroundBrush") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border
        {
            Background = ThemeManager.Resource("SidebarBrush"),
            BorderBrush = ThemeManager.Resource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var nav = new StackPanel { Margin = new Thickness(12, 14, 12, 14) };
        sidebar.Child = new ScrollViewer
        {
            Content = nav,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(sidebar);

        var header = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(4, 0, 4, 18)
        };
        header.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = ThemeManager.Resource("ControlBrush"),
            Child = new TextBlock
            {
                Text = "W",
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"],
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        header.Children.Add(new TextBlock
        {
            Text = "Preferences",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        nav.Children.Add(header);

        AddNavButton(nav, new SettingsNavItem("general", "General", "M5,12 H19 M12,5 V19 M7,7 L17,17 M17,7 L7,17"));
        AddNavButton(nav, new SettingsNavItem("hotkeys", "Hotkeys", "M4,7 H20 V17 H4 Z M7,10 H9 M11,10 H13 M15,10 H17 M7,14 H17"));
        AddNavButton(nav, new SettingsNavItem("updates", "Updates", "M12,4 V16 M7,11 L12,16 L17,11 M5,20 H19"));
        AddNavButton(nav, new SettingsNavItem("logs", "Logs", "M6,4 H18 V20 H6 Z M9,8 H15 M9,12 H15 M9,16 H13"));
        AddNavButton(nav, new SettingsNavItem("externalPlugins", "External Plugins", "M12,3 V15 M7,10 L12,15 L17,10 M5,21 H19 M5,17 H19"));

        AddNavHeader(nav, "BUILT-IN PLUGINS");
        foreach (var plugin in _router.Plugins
                     .Where(p => p.Source == PluginSource.BuiltIn)
                     .OrderBy(p => p.Manifest.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AddNavButton(nav, new SettingsNavItem(plugin.Manifest.Id, plugin.Manifest.Name, string.Empty, plugin));
        }

        var externalPlugins = _router.Plugins
            .Where(p => p.Source == PluginSource.External)
            .OrderBy(p => p.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (externalPlugins.Length > 0)
        {
            AddNavHeader(nav, "EXTERNAL PLUGINS");
            foreach (var plugin in externalPlugins)
            {
                AddNavButton(nav, new SettingsNavItem(plugin.Manifest.Id, plugin.Manifest.Name, string.Empty, plugin));
            }
        }

        _content.Margin = new Thickness(34, 28, 34, 28);
        Grid.SetColumn(_content, 1);
        root.Children.Add(_content);

        var selectedItem = _navButtons.FirstOrDefault(entry => entry.Item.Id == selectedNavId).Item
            ?? _navButtons.First().Item;
        SelectNav(selectedItem);

        return root;
    }

    private void ThemeManager_Changed()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            var selectedNavId = _selectedNav?.Id;
            Content = null;
            Background = ThemeManager.Resource("BackgroundBrush");
            Foreground = ThemeManager.Resource("TextPrimaryBrush");
            Content = BuildContent(selectedNavId);
        });
    }

    private void AddNavHeader(System.Windows.Controls.Panel nav, string text)
    {
        nav.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 22, 0, 8)
        });
    }

    private void AddNavButton(System.Windows.Controls.Panel nav, SettingsNavItem item)
    {
        var button = new System.Windows.Controls.Button
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Content = BuildNavContent(item)
        };
        button.Click += (_, _) => SelectNav(item);
        _navButtons.Add((item, button));
        nav.Children.Add(button);
    }

    private UIElement BuildNavContent(SettingsNavItem item)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = ThemeManager.Resource("SurfaceElevatedBrush"),
            Child = item.Plugin is null ? NavPath(item.Icon) : PluginIconElement(item.Plugin, 18)
        });
        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
        };
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        return row;
    }

    private UIElement NavPath(string geometry)
    {
        return new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry),
            Stroke = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"],
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void SelectNav(SettingsNavItem item)
    {
        _selectedNav = item;
        foreach (var (navItem, button) in _navButtons)
        {
            var selected = navItem.Id == item.Id;
            button.Background = selected ? ThemeManager.Resource("SelectedNavBrush") : System.Windows.Media.Brushes.Transparent;
            button.BorderBrush = selected ? ThemeManager.Resource("SelectedNavBorderBrush") : System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = new Thickness(1);
        }

        _content.Content = item.Plugin is not null
            ? BuildPluginPage(item.Plugin)
            : item.Id switch
            {
                "hotkeys" => BuildHotkeysTab(),
                "updates" => BuildUpdatesTab(),
                "logs" => BuildLogsTab(),
                "externalPlugins" => BuildExternalPluginsTab(),
                _ => BuildGeneralTab()
            };
    }

    private UIElement PageShell(string title, string subtitle, IEnumerable<UIElement> sections)
    {
        var panel = new StackPanel { MaxWidth = 720 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24)
        });

        foreach (var section in sections)
        {
            panel.Children.Add(section);
        }

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private UIElement Section(string title, params UIElement[] rows)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        });

        var body = new StackPanel();
        foreach (var row in rows)
        {
            body.Children.Add(row);
        }

        panel.Children.Add(body);
        return panel;
    }

    private UIElement SettingRow(string title, string description, UIElement control)
    {
        var row = new Border
        {
            BorderBrush = ThemeManager.Resource("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 12)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            MinWidth = 300,
            MaxWidth = 420
        });

        var copy = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            copy.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        grid.Children.Add(copy);
        if (control is FrameworkElement element)
        {
            element.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            element.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        row.Child = grid;
        return row;
    }

    private UIElement TextValue(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right
        };
    }

    private System.Windows.Controls.TextBox StyledTextBox(string text, double width = 240)
    {
        return new System.Windows.Controls.TextBox
        {
            Text = text,
            Width = width,
            Height = 34,
            MinHeight = 34,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = ThemeManager.Resource("ControlBrush"),
            BorderBrush = ThemeManager.Resource("ControlBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
        };
    }

    private System.Windows.Controls.TextBox HotkeyCaptureBox(string keys, double width, Action<string> onChanged)
    {
        var box = StyledTextBox(HotkeyText.Normalize(keys), width);
        box.IsReadOnly = true;
        box.Cursor = System.Windows.Input.Cursors.Hand;
        box.ToolTip = "Focus and press the shortcut";
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key is Key.Tab)
            {
                box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            }

            if (e.Key is Key.Escape)
            {
                Keyboard.ClearFocus();
                return;
            }

            if (e.Key is Key.Back or Key.Delete)
            {
                box.Text = string.Empty;
                onChanged(string.Empty);
                return;
            }

            var hotkey = ComposeHotkeyText(Keyboard.Modifiers, EventKey(e));
            if (string.IsNullOrWhiteSpace(hotkey))
            {
                return;
            }

            box.Text = hotkey;
            box.CaretIndex = box.Text.Length;
            onChanged(hotkey);
        };
        return box;
    }

    public static string ComposeHotkeyText(ModifierKeys modifiers, Key key)
    {
        if (IsModifierKey(key) || key is Key.None)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyDisplayText(key));
        return HotkeyText.Normalize(string.Join("+", parts));
    }

    private static Key EventKey(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == Key.ImeProcessed ? e.ImeProcessedKey : key;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin;

    private static string KeyDisplayText(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        Key.OemPlus => "Plus",
        Key.OemMinus => "Minus",
        Key.OemComma => "Comma",
        Key.OemPeriod => "Period",
        Key.OemQuestion => "Slash",
        Key.OemSemicolon => "Semicolon",
        Key.OemQuotes => "Quote",
        Key.OemOpenBrackets => "OpenBracket",
        Key.OemCloseBrackets => "CloseBracket",
        Key.OemPipe => "Backslash",
        Key.OemTilde => "Tilde",
        _ => key.ToString()
    };

    private System.Windows.Controls.ComboBox StyledComboBox(double width = 180)
    {
        var itemStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem));
        itemStyle.Setters.Add(new Setter(FontSizeProperty, 13.0));
        itemStyle.Setters.Add(new Setter(MinHeightProperty, 32.0));
        itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(10, 6, 10, 6)));
        itemStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));
        itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));

        return new System.Windows.Controls.ComboBox
        {
            Width = width,
            Height = 34,
            MinHeight = 34,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            ItemContainerStyle = itemStyle,
            Background = ThemeManager.Resource("ControlBrush"),
            BorderBrush = ThemeManager.Resource("ControlBorderBrush"),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
        };
    }

    private UIElement SegmentedControl(IReadOnlyList<string> options, string selected, Action<string> onChanged)
    {
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var buttons = new List<System.Windows.Controls.Button>();
        void Refresh()
        {
            foreach (var button in buttons)
            {
                var active = string.Equals(button.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase);
                button.Background = active ? ThemeManager.Resource("SelectionBrush") : ThemeManager.Resource("ControlBrush");
                button.BorderBrush = active ? ThemeManager.Resource("AccentBrush") : ThemeManager.Resource("ControlBorderBrush");
            }
        }

        foreach (var option in options)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = option,
                Tag = option,
                MinHeight = 34,
                MinWidth = 72,
                FontSize = 13,
                Padding = new Thickness(10, 4, 10, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(buttons.Count == 0 ? 0 : 4, 0, 0, 0)
            };
            button.Click += (_, _) =>
            {
                selected = option;
                onChanged(option);
                Refresh();
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        Refresh();
        return panel;
    }

    private UIElement BuildGeneralTab()
    {
        var tray = new System.Windows.Controls.CheckBox
        {
            IsChecked = _settings.AppSettings.ShowTrayIcon
        };
        tray.Checked += (_, _) => _settings.SetAppSettings(_settings.AppSettings with { ShowTrayIcon = true });
        tray.Unchecked += (_, _) => _settings.SetAppSettings(_settings.AppSettings with { ShowTrayIcon = false });

        var executablePath = StartupManager.CurrentExecutablePath();
        var launchAtStartup = new System.Windows.Controls.CheckBox
        {
            IsChecked = executablePath is not null && StartupManager.IsEnabled(executablePath)
        };
        launchAtStartup.Checked += (_, _) =>
        {
            if (launchAtStartup.Tag is not true) SetLaunchAtStartup(launchAtStartup, true);
        };
        launchAtStartup.Unchecked += (_, _) =>
        {
            if (launchAtStartup.Tag is not true) SetLaunchAtStartup(launchAtStartup, false);
        };

        var hotkey = HotkeyCaptureBox(_settings.AppSettings.MainHotkey, 180, value =>
        {
            _settings.SetAppSettings(_settings.AppSettings with { MainHotkey = value });
            HotkeysChanged?.Invoke();
        });

        return PageShell(
            "General",
            "Core launcher behavior and startup preferences.",
            [
                Section("Appearance",
                    SettingRow("Theme", "Choose how Weed should render native surfaces.",
                        SegmentedControl(["system", "dark", "light"], _settings.AppSettings.Theme, value =>
                        {
                            _settings.SetAppSettings(_settings.AppSettings with { Theme = value });
                            ThemeManager.Apply(value);
                        }))),
                Section("Startup",
                    SettingRow("Tray icon", "Keep Weed available from the notification area.", tray),
                    SettingRow("Launch at startup", "Register Weed for the current Windows user.", launchAtStartup)),
                Section("Launcher",
                    SettingRow("Main hotkey", "Global shortcut used to open the launcher.", hotkey))
            ]);
    }

    private UIElement BuildUpdatesTab()
    {
        var auto = new System.Windows.Controls.CheckBox
        {
            IsChecked = _settings.AppSettings.AutoCheckUpdates
        };
        auto.Checked += (_, _) => _settings.SetAppSettings(_settings.AppSettings with { AutoCheckUpdates = true });
        auto.Unchecked += (_, _) => _settings.SetAppSettings(_settings.AppSettings with { AutoCheckUpdates = false });

        var manifestBox = StyledTextBox(_settings.AppSettings.UpdateManifestUrl, 300);
        manifestBox.LostFocus += (_, _) => _settings.SetAppSettings(_settings.AppSettings with
        {
            UpdateManifestUrl = manifestBox.Text.Trim()
        });

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        var check = new System.Windows.Controls.Button
        {
            Content = "Check now",
            Padding = new Thickness(14, 6, 14, 6),
            MinHeight = 34,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var download = new System.Windows.Controls.Button
        {
            Content = "Download package",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 34,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        buttons.Children.Add(check);
        buttons.Children.Add(download);

        var status = new TextBlock
        {
            Text = $"Current version: {UpdateService.CurrentVersion}",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"]
        };

        UpdateCheckResult? lastResult = null;
        check.Click += async (_, _) =>
        {
            try
            {
                check.IsEnabled = false;
                download.IsEnabled = false;
                status.Text = "Checking for updates...";
                var manifestLocation = manifestBox.Text.Trim();
                _settings.SetAppSettings(_settings.AppSettings with { UpdateManifestUrl = manifestLocation });
                lastResult = await new UpdateService().CheckAsync(manifestLocation, CancellationToken.None);
                status.Text = FormatUpdateStatus(lastResult);
                download.IsEnabled = lastResult.IsUpdateAvailable && lastResult.Manifest is not null;
            }
            catch (Exception ex)
            {
                _logger.Error("Manual update check failed.", ex);
                status.Text = $"Update check failed: {ex.Message}";
            }
            finally
            {
                check.IsEnabled = true;
            }
        };

        download.Click += async (_, _) =>
        {
            if (lastResult?.Manifest is null)
            {
                return;
            }

            try
            {
                download.IsEnabled = false;
                status.Text = "Downloading update package...";
                var package = await new UpdateService().DownloadPackageAsync(
                    lastResult.Manifest,
                    _settings.Paths.Updates,
                    CancellationToken.None,
                    manifestBox.Text.Trim());
                status.Text = package.Message;
                if (package.Verified && File.Exists(package.PackagePath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{package.PackagePath}\"")
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Update package download failed.", ex);
                status.Text = $"Update download failed: {ex.Message}";
            }
            finally
            {
                download.IsEnabled = lastResult?.IsUpdateAvailable == true && lastResult.Manifest is not null;
            }
        };

        return PageShell(
            "Updates",
            "Release manifest checks and verified update downloads.",
            [
                Section("Automatic Checks",
                    SettingRow("Check on startup", "Look for updates whenever Weed starts.", auto),
                    SettingRow("Manifest", "URL or local update manifest path.", manifestBox)),
                Section("Manual Check",
                    SettingRow("Actions", "Check the manifest and download a verified package.", buttons),
                    SettingRow("Status", "Most recent update result.", status))
            ]);
    }

    private UIElement BuildLogsTab()
    {
        Directory.CreateDirectory(_settings.Paths.Logs);

        var latestStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"]
        };
        var tailBox = DiagnosticTextBox(string.Empty, 320);
        string? latestLog = null;

        void Refresh()
        {
            latestLog = LatestLogFile();
            latestStatus.Text = LogSummary(latestLog);
            tailBox.Text = ReadLogTail(latestLog, 400);
            tailBox.ScrollToEnd();
        }

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        var refresh = PluginCommandButton("Refresh", () =>
        {
            Refresh();
            return Task.CompletedTask;
        });
        var openFolder = PluginCommandButton("Open folder", () =>
        {
            Directory.CreateDirectory(_settings.Paths.Logs);
            Process.Start(new ProcessStartInfo(_settings.Paths.Logs) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        var openLatest = PluginCommandButton("Open latest", () =>
        {
            latestLog ??= LatestLogFile();
            if (!string.IsNullOrWhiteSpace(latestLog) && File.Exists(latestLog))
            {
                Process.Start(new ProcessStartInfo(latestLog) { UseShellExecute = true });
            }

            return Task.CompletedTask;
        });
        openFolder.Margin = new Thickness(8, 0, 0, 0);
        openLatest.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(refresh);
        actions.Children.Add(openFolder);
        actions.Children.Add(openLatest);

        Refresh();

        return PageShell(
            "Logs",
            "Runtime diagnostics, command history, and plugin errors.",
            [
                Section("Storage",
                    SettingRow("Folder", "Local directory used for rolling log files.", TextValue(_settings.Paths.Logs)),
                    SettingRow("Latest", "Newest log file and retained size.", latestStatus)),
                Section("Actions",
                    SettingRow("Tools", "Refresh the tail or open logs in Explorer.", actions)),
                Section("Latest Tail",
                    tailBox)
            ]);
    }

    private string? LatestLogFile()
    {
        try
        {
            return Directory.EnumerateFiles(_settings.Paths.Logs, "weed-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private string LogSummary(string? latestLog)
    {
        try
        {
            var files = Directory.EnumerateFiles(_settings.Paths.Logs, "weed-*.log")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            if (files.Length == 0)
            {
                return "No logs yet.";
            }

            var totalBytes = files.Sum(file => file.Length);
            var latestName = string.IsNullOrWhiteSpace(latestLog) ? files[0].Name : Path.GetFileName(latestLog);
            return $"{latestName}{Environment.NewLine}{files.Length} files, {FormatBytes(totalBytes)} retained";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string ReadLogTail(string? path, int lines)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "No logs yet.";
        }

        try
        {
            return string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(lines));
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private DataTemplate ExternalPluginItemTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(2));

        var header = new FrameworkElementFactory(typeof(DockPanel));
        header.SetValue(DockPanel.LastChildFillProperty, true);

        var version = new FrameworkElementFactory(typeof(TextBlock));
        version.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(ExternalPluginInstallViewItem.VersionText)));
        version.SetValue(TextBlock.FontSizeProperty, 11.0);
        version.SetValue(TextBlock.ForegroundProperty, ThemeManager.Resource("TextSecondaryBrush"));
        version.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 2, 0, 0));
        version.SetValue(DockPanel.DockProperty, Dock.Right);
        header.AppendChild(version);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new WpfBinding("Manifest.Name"));
        name.SetValue(TextBlock.FontSizeProperty, 14.0);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, ThemeManager.Resource("TextPrimaryBrush"));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        header.AppendChild(name);
        root.AppendChild(header);

        var status = new FrameworkElementFactory(typeof(TextBlock));
        status.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(ExternalPluginInstallViewItem.StatusText)));
        status.SetValue(TextBlock.FontSizeProperty, 12.0);
        status.SetValue(TextBlock.ForegroundProperty, ThemeManager.Resource("TextSecondaryBrush"));
        status.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
        status.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        root.AppendChild(status);

        return new DataTemplate(typeof(ExternalPluginInstallViewItem)) { VisualTree = root };
    }

    private Style ExternalPluginItemContainerStyle()
    {
        var style = new Style(typeof(System.Windows.Controls.ListBoxItem));
        style.Setters.Add(new Setter(BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(BorderBrushProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 10, 12, 10)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 4)));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FocusVisualStyleProperty, null));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ItemBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new WpfBinding(nameof(Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty, new WpfBinding(nameof(BorderBrush))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new WpfBinding(nameof(BorderThickness))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.PaddingProperty, new WpfBinding(nameof(Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        presenter.SetBinding(ContentPresenter.ContentProperty, new WpfBinding(nameof(ContentControl.Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new WpfBinding(nameof(ContentControl.ContentTemplate))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(System.Windows.Controls.ListBoxItem)) { VisualTree = border };
        style.Setters.Add(new Setter(TemplateProperty, template));

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, ThemeManager.Resource("HoverBrush")));
        hover.Setters.Add(new Setter(BorderBrushProperty, ThemeManager.Resource("ControlBorderBrush")));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = System.Windows.Controls.ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, ThemeManager.Resource("SelectedNavBrush")));
        selected.Setters.Add(new Setter(BorderBrushProperty, ThemeManager.Resource("SelectedNavBorderBrush")));
        style.Triggers.Add(selected);
        return style;
    }

    private UIElement BuildExternalPluginsTab()
    {
        var refreshInstalled = new System.Windows.Controls.Button
        {
            Content = "Refresh",
            Padding = new Thickness(12, 6, 12, 6),
            MinHeight = 34,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var openFolder = new System.Windows.Controls.Button
        {
            Content = "Open folder",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 34,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var uninstallPlugin = new System.Windows.Controls.Button
        {
            Content = "Uninstall",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 34,
            FontSize = 13,
            IsEnabled = false,
            Foreground = ThemeManager.Resource("DangerBrush"),
            Background = ThemeManager.Resource("DangerSurfaceBrush"),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var installedActions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        installedActions.Children.Add(refreshInstalled);
        installedActions.Children.Add(openFolder);
        installedActions.Children.Add(uninstallPlugin);

        var installedList = new System.Windows.Controls.ListBox
        {
            Width = 420,
            Height = 230,
            Padding = new Thickness(4),
            ItemTemplate = ExternalPluginItemTemplate(),
            ItemContainerStyle = ExternalPluginItemContainerStyle(),
            Background = ThemeManager.Resource("ControlBrush"),
            BorderBrush = ThemeManager.Resource("ControlBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
        };
        var installedStatus = new TextBlock
        {
            Text = "Refresh reads the local external plugin folder.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"]
        };

        var importPackage = new System.Windows.Controls.Button
        {
            Content = "Import package",
            Padding = new Thickness(12, 6, 12, 6),
            MinHeight = 34,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var importFolder = new System.Windows.Controls.Button
        {
            Content = "Import folder",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 34,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        actions.Children.Add(importPackage);
        actions.Children.Add(importFolder);

        var replaceExisting = new System.Windows.Controls.CheckBox
        {
            IsChecked = false
        };
        var status = new TextBlock
        {
            Text = "No plugin imported in this session.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"]
        };

        void RefreshInstalled()
        {
            var items = ReadExternalPluginInstallations();
            installedList.ItemsSource = items;
            uninstallPlugin.IsEnabled = false;
            if (items.Count == 0)
            {
                installedStatus.Text = "No external plugins found in the local plugin folder.";
                return;
            }

            installedList.SelectedIndex = 0;
            if (installedList.SelectedItem is ExternalPluginInstallViewItem selected)
            {
                installedStatus.Text = FormatExternalPluginInstallItem(selected);
                uninstallPlugin.IsEnabled = true;
            }
        }

        installedList.SelectionChanged += (_, _) =>
        {
            if (installedList.SelectedItem is ExternalPluginInstallViewItem selected)
            {
                installedStatus.Text = FormatExternalPluginInstallItem(selected);
                uninstallPlugin.IsEnabled = true;
            }
            else
            {
                uninstallPlugin.IsEnabled = false;
            }
        };

        refreshInstalled.Click += (_, _) => RefreshInstalled();

        importPackage.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Weed plugin package or DLL",
                Filter = "Weed plugin packages (*.zip;*.dll)|*.zip;*.dll|ZIP packages (*.zip)|*.zip|Plugin DLLs (*.dll)|*.dll|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                await ImportExternalPluginAsync(dialog.FileName, replaceExisting.IsChecked == true, status);
                RefreshInstalled();
            }
        };

        importFolder.Click += async (_, _) =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose a Weed plugin folder, published folder, or source project folder",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await ImportExternalPluginAsync(dialog.SelectedPath, replaceExisting.IsChecked == true, status);
                RefreshInstalled();
            }
        };

        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(_settings.Paths.Plugins);
            Process.Start(new ProcessStartInfo(_settings.Paths.Plugins) { UseShellExecute = true });
        };

        uninstallPlugin.Click += async (_, _) =>
        {
            if (installedList.SelectedItem is not ExternalPluginInstallViewItem selected)
            {
                return;
            }

            var confirmation = System.Windows.MessageBox.Show(
                this,
                $"Uninstall {selected.Manifest.Name} v{selected.Manifest.Version}?{Environment.NewLine}{Environment.NewLine}" +
                "The plugin package will be removed. Plugin settings and data are preserved. " +
                "Restart Weed to unload a plugin that is active in this session.",
                "Uninstall External Plugin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            uninstallPlugin.IsEnabled = false;
            installedStatus.Text = $"Uninstalling {selected.Manifest.Name}...";
            var result = await new ExternalPluginUninstaller().UninstallAsync(
                selected.Manifest.Id,
                selected.Directory,
                _settings.Paths.Plugins,
                CancellationToken.None);
            RefreshInstalled();
            installedStatus.Text = result.Message;
            System.Windows.MessageBox.Show(
                this,
                result.Message,
                "External Plugin Uninstall",
                MessageBoxButton.OK,
                result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        };

        RefreshInstalled();

        return PageShell(
            "External Plugins",
            "Import, inspect, or uninstall external plugin packages. Restart Weed after installation, replacement, or removal.",
            [
                Section("Installed",
                    SettingRow("Actions", "Refresh, open the plugin folder, or uninstall the selected package.", installedActions),
                    SettingRow("Plugins", "External plugins installed under the user plugin directory.", installedList),
                    SettingRow("Status", "Selected plugin and load state.", installedStatus)),
                Section("Import",
                    SettingRow("Actions", "Import a ZIP, DLL, compiled folder, or source folder.", actions),
                    SettingRow("Replace existing", "Allow the importer to replace a plugin folder with the same manifest id.", replaceExisting),
                    SettingRow("Plugin directory", "External plugins are copied here.", TextValue(_settings.Paths.Plugins)),
                    SettingRow("Status", "Most recent import result.", status))
            ]);
    }

    private IReadOnlyList<ExternalPluginInstallViewItem> ReadExternalPluginInstallations()
    {
        Directory.CreateDirectory(_settings.Paths.Plugins);
        var loaded = _router.Plugins
            .Where(plugin => plugin.Source == PluginSource.External)
            .Select(plugin => plugin.Manifest.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = new List<ExternalPluginInstallViewItem>();

        foreach (var manifestPath in Directory.EnumerateFiles(_settings.Paths.Plugins, "manifest.json", SearchOption.AllDirectories))
        {
            if (IsIgnoredExternalPluginPath(_settings.Paths.Plugins, manifestPath))
            {
                continue;
            }

            try
            {
                var pluginDirectory = Path.GetDirectoryName(manifestPath) ?? _settings.Paths.Plugins;
                if (ExternalPluginUninstaller.IsPendingRemoval(pluginDirectory))
                {
                    continue;
                }

                var manifest = JsonSerializer.Deserialize<WeedPluginManifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    continue;
                }

                items.Add(new ExternalPluginInstallViewItem
                {
                    Manifest = manifest,
                    Directory = pluginDirectory,
                    IsLoaded = loaded.Contains(manifest.Id)
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"Skipped malformed external plugin manifest at {manifestPath}: {ex.Message}");
            }
        }

        return items
            .OrderBy(item => item.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsIgnoredExternalPluginPath(string pluginsRoot, string manifestPath)
    {
        var relative = Path.GetRelativePath(pluginsRoot, manifestPath);
        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part.StartsWith(".", StringComparison.Ordinal));
    }

    private static string FormatExternalPluginInstallItem(ExternalPluginInstallViewItem item)
    {
        var lines = new List<string>
        {
            item.IsLoaded ? "Loaded in this session." : "Installed on disk. Restart Weed to load changes.",
            $"Id: {item.Manifest.Id}",
            $"Directory: {item.Directory}"
        };

        if (!string.IsNullOrWhiteSpace(item.Manifest.Assembly))
        {
            lines.Add($"Assembly: {item.Manifest.Assembly}");
        }

        if (!string.IsNullOrWhiteSpace(item.Manifest.EntryType))
        {
            lines.Add($"Entry: {item.Manifest.EntryType}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task ImportExternalPluginAsync(string sourcePath, bool replaceExisting, TextBlock status)
    {
        try
        {
            status.Text = "Importing plugin...";
            var result = await new ExternalPluginImporter().ImportAsync(
                sourcePath,
                _settings.Paths.Plugins,
                replaceExisting,
                CancellationToken.None);
            status.Text = result.TargetDirectory is null
                ? result.Message
                : $"{result.Message}{Environment.NewLine}{result.TargetDirectory}";
            System.Windows.MessageBox.Show(
                result.Message,
                "Weed Plugin Import",
                MessageBoxButton.OK,
                result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.Error("External plugin import failed.", ex);
            status.Text = $"Import failed: {ex.Message}";
        }
    }

    private static string FormatUpdateStatus(UpdateCheckResult result)
    {
        if (result.Manifest is null)
        {
            return result.Message;
        }

        var lines = new List<string>
        {
            result.Message,
            $"Current: {result.CurrentVersion}",
            $"Available: {result.Manifest.Version}",
            $"Package: {result.Manifest.PackageUrl}"
        };
        if (!string.IsNullOrWhiteSpace(result.Manifest.Sha256))
        {
            lines.Add($"SHA256: {result.Manifest.Sha256}");
        }

        if (!string.IsNullOrWhiteSpace(result.Manifest.Notes))
        {
            lines.Add($"Notes: {result.Manifest.Notes}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private UIElement BuildPluginPage(LoadedPlugin plugin)
    {
        var enabled = new System.Windows.Controls.CheckBox
        {
            IsChecked = _settings.IsPluginEnabled(plugin.Manifest.Id)
        };
        enabled.Checked += (_, _) => _settings.SetPluginEnabled(plugin.Manifest.Id, true);
        enabled.Unchecked += (_, _) => _settings.SetPluginEnabled(plugin.Manifest.Id, false);

        var providers = new[]
        {
            plugin.QueryProvider is not null ? "query" : string.Empty,
            plugin.CommandHandler is not null ? "commands" : string.Empty,
            plugin.ResidentPlugin is not null ? "resident" : string.Empty
        }.Where(static item => item.Length > 0);

        var settingRows = new List<UIElement>();
        if (plugin.Instance is IPluginSettingsProvider provider)
        {
            settingRows.AddRange(provider.GetSettings().Select(setting => BuildPluginSettingRow(plugin, setting)));
        }

        if (plugin.Manifest.Id.Equals("weed.appLauncher", StringComparison.OrdinalIgnoreCase))
        {
            settingRows.Add(SettingRow(
                "Application index",
                "Re-scan Start Menu shortcuts and update the local cache.",
                PluginCommandButton("Refresh", async () =>
                {
                    var result = await _router.ExecutePluginCommandAsync(plugin.Manifest.Id, "app.refreshIndex", null, CancellationToken.None);
                    System.Windows.MessageBox.Show(
                        result.Message ?? "Application index refreshed.",
                        plugin.Manifest.Name,
                        MessageBoxButton.OK,
                        result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
                })));
        }

        if (settingRows.Count == 0)
        {
            settingRows.Add(SettingRow("No plugin settings", "This plugin does not expose host-rendered settings.", TextValue("")));
        }

        var pluginRows = new List<UIElement>
        {
            SettingRow("Enabled", "Disabled plugins are skipped by query routing and commands.", enabled)
        };
        if (SupportsImplicitQuery(plugin))
        {
            pluginRows.Add(SettingRow(
                "Priority",
                "Higher values move this plugin's implicit results ahead of equal matches.",
                BuildPriorityControl(plugin.Manifest.Id)));
        }

        pluginRows.AddRange(BuildKeywordActivationRows(plugin));

        return PageShell(
            plugin.Manifest.Name,
            $"{plugin.Manifest.Id}  v{plugin.Manifest.Version}",
            [
                Section("Plugin", pluginRows.ToArray()),
                Section("Settings", settingRows.ToArray()),
                BuildPluginAdvancedSection(plugin, string.Join(", ", providers.DefaultIfEmpty("none")))
            ]);
    }

    private UIElement BuildPriorityControl(string pluginId)
    {
        var priorityText = new TextBlock
        {
            Text = _settings.GetPluginPriority(pluginId).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
            Width = 34,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var priority = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = _settings.GetPluginPriority(pluginId),
            Width = 210,
            TickFrequency = 10
        };
        priority.ValueChanged += (_, _) =>
        {
            var value = (int)Math.Round(priority.Value);
            priorityText.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _settings.SetPluginPriority(pluginId, value);
        };
        var priorityControl = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        priorityControl.Children.Add(priority);
        priorityControl.Children.Add(priorityText);
        return priorityControl;
    }

    private IEnumerable<UIElement> BuildKeywordActivationRows(LoadedPlugin plugin)
    {
        foreach (var activation in plugin.Manifest.Activations.Where(a =>
                     a.Type.Equals("keyword", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(a.Keyword)))
        {
            var fallback = TextNormalizer.Normalize(activation.Keyword ?? string.Empty);
            var key = ActivationSettings.KeywordSettingKey(activation);
            var current = ActivationSettings.NormalizeKeyword(
                _settings.GetPluginSetting(plugin.Manifest.Id, key, fallback),
                fallback);
            var input = StyledTextBox(current, 140);
            input.LostFocus += (_, _) =>
            {
                var normalized = ActivationSettings.NormalizeKeyword(input.Text, fallback);
                input.Text = normalized;
                _settings.SetPluginSetting(plugin.Manifest.Id, key, normalized);
            };

            var title = activation.Command is null ? "Keyword" : $"Keyword: {activation.Command}";
            var description = $"Default keyword: {fallback}. Type this prefix in the launcher to invoke this plugin.";
            yield return SettingRow(title, description, input);
        }
    }

    private static bool SupportsImplicitQuery(LoadedPlugin plugin) =>
        ShouldShowPriorityControl(plugin.Manifest);

    public static bool ShouldShowPriorityControl(WeedPluginManifest manifest) =>
        manifest.Activations.Any(activation =>
            activation.Type.Equals("implicitQuery", StringComparison.OrdinalIgnoreCase));

    private static System.Windows.Controls.Button PluginCommandButton(string text, Func<Task> action)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            MinWidth = 82,
            MinHeight = 34,
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private UIElement BuildPluginAdvancedSection(LoadedPlugin plugin, string runtime)
    {
        var content = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        content.Children.Add(SettingRow("Runtime", "Capabilities exposed by this plugin.", TextValue(runtime)));
        content.Children.Add(SettingRow("Permissions", "Host capabilities requested by the plugin.",
            TextValue(plugin.Manifest.Permissions.Count == 0 ? "None" : string.Join(", ", plugin.Manifest.Permissions))));
        content.Children.Add(SettingRow("Activations", "Keywords, hotkeys, and implicit query providers.",
            TextValue(plugin.Manifest.Activations.Count == 0 ? "None" : string.Join(Environment.NewLine, plugin.Manifest.Activations.Select(ActivationText)))));
        content.Children.Add(SettingRow("Dependencies", "External programs required by this plugin.",
            TextValue(DependencyText(plugin))));
        content.Children.Add(DiagnosticBlock("Manifest JSON", ManifestText(plugin)));
        content.Children.Add(DiagnosticBlock("Log Tail", LogTail(plugin)));

        return new Border
        {
            BorderBrush = ThemeManager.Resource("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            Margin = new Thickness(0, 0, 0, 28),
            Child = new Expander
            {
                Header = "Advanced",
                IsExpanded = false,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
                Content = content
            }
        };
    }

    private UIElement BuildPluginSettingRow(LoadedPlugin plugin, PluginSettingDefinition setting)
    {
        return SettingRow(setting.Label, setting.Description ?? string.Empty, BuildPluginSettingEditor(plugin, setting));
    }

    private UIElement BuildPluginSettingEditor(LoadedPlugin plugin, PluginSettingDefinition setting)
    {
        return setting.Kind switch
        {
            PluginSettingKind.Boolean => BuildBooleanPluginSetting(plugin, setting),
            PluginSettingKind.Integer => BuildIntegerPluginSetting(plugin, setting),
            PluginSettingKind.Select => BuildSelectPluginSetting(plugin, setting),
            _ => BuildTextPluginSetting(plugin, setting)
        };
    }

    private UIElement BuildBooleanPluginSetting(LoadedPlugin plugin, PluginSettingDefinition setting)
    {
        var box = new System.Windows.Controls.CheckBox
        {
            IsChecked = _settings.GetPluginSetting(plugin.Manifest.Id, setting.Key, DefaultBool(setting))
        };
        box.Checked += (_, _) => _settings.SetPluginSetting(plugin.Manifest.Id, setting.Key, true);
        box.Unchecked += (_, _) => _settings.SetPluginSetting(plugin.Manifest.Id, setting.Key, false);
        return box;
    }

    private UIElement BuildIntegerPluginSetting(LoadedPlugin plugin, PluginSettingDefinition setting)
    {
        var current = Math.Clamp(_settings.GetPluginSetting(plugin.Manifest.Id, setting.Key, DefaultInt(setting)),
            setting.Min ?? int.MinValue,
            setting.Max ?? int.MaxValue);
        if (setting.Min is null || setting.Max is null)
        {
            var inputOnly = StyledTextBox(current.ToString(System.Globalization.CultureInfo.InvariantCulture), 120);
            inputOnly.LostFocus += (_, _) => SaveIntegerSetting(plugin, setting, inputOnly.Text, inputOnly);
            return inputOnly;
        }

        var input = StyledTextBox(current.ToString(System.Globalization.CultureInfo.InvariantCulture), 58);
        var slider = new Slider
        {
            Minimum = setting.Min.Value,
            Maximum = setting.Max.Value,
            Value = current,
            Width = 180,
            TickFrequency = Math.Max(1, (setting.Max.Value - setting.Min.Value) / 10.0)
        };
        slider.ValueChanged += (_, _) =>
        {
            var value = (int)Math.Round(slider.Value);
            input.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _settings.SetPluginSetting(plugin.Manifest.Id, setting.Key, value);
        };
        input.LostFocus += (_, _) =>
        {
            var value = SaveIntegerSetting(plugin, setting, input.Text, input);
            slider.Value = value;
        };

        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        panel.Children.Add(slider);
        input.Margin = new Thickness(10, 0, 0, 0);
        panel.Children.Add(input);
        return panel;
    }

    private int SaveIntegerSetting(LoadedPlugin plugin, PluginSettingDefinition setting, string text, System.Windows.Controls.TextBox input)
    {
        if (!int.TryParse(text, out var value))
        {
            value = DefaultInt(setting);
        }

        value = Math.Clamp(value, setting.Min ?? int.MinValue, setting.Max ?? int.MaxValue);
        input.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _settings.SetPluginSetting(plugin.Manifest.Id, setting.Key, value);
        return value;
    }

    private UIElement BuildSelectPluginSetting(LoadedPlugin plugin, PluginSettingDefinition setting)
    {
        if (setting.Options.Count == 0)
        {
            return BuildTextPluginSetting(plugin, setting);
        }

        var current = _settings.GetPluginSetting(plugin.Manifest.Id, setting.Key, setting.DefaultValue ?? setting.Options[0].Value);
        var box = StyledComboBox(190);
        box.ItemsSource = setting.Options;
        box.DisplayMemberPath = nameof(PluginSettingOption.Label);
        box.SelectedValuePath = nameof(PluginSettingOption.Value);
        box.SelectedValue = current;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedValue is not null)
            {
                _settings.SetPluginSetting(plugin.Manifest.Id, setting.Key, box.SelectedValue.ToString() ?? string.Empty);
            }
        };
        return box;
    }

    private UIElement BuildTextPluginSetting(LoadedPlugin plugin, PluginSettingDefinition setting)
    {
        var box = StyledTextBox(
            _settings.GetPluginSetting(plugin.Manifest.Id, setting.Key, setting.DefaultValue ?? string.Empty),
            setting.Kind == PluginSettingKind.Path ? 300 : 220);
        box.LostFocus += (_, _) => _settings.SetPluginSetting(plugin.Manifest.Id, setting.Key, box.Text.Trim());
        return box;
    }

    private UIElement DiagnosticBlock(string title, string text)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0)
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(DiagnosticTextBox(text, 150));
        return panel;
    }

    private static System.Windows.Controls.TextBox DiagnosticTextBox(string text, double height)
    {
        return new System.Windows.Controls.TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = height,
            Padding = new Thickness(8),
            Background = ThemeManager.Resource("ControlBrush"),
            BorderBrush = ThemeManager.Resource("ControlBorderBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private UIElement DiagnosticExpander(string title, string text)
    {
        return new Border
        {
            BorderBrush = ThemeManager.Resource("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 10),
            Child = new Expander
            {
                Header = title,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
                Content = new System.Windows.Controls.TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Height = 220,
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(8),
                    Background = ThemeManager.Resource("ControlBrush"),
                    BorderBrush = ThemeManager.Resource("ControlBorderBrush"),
                    BorderThickness = new Thickness(1)
                }
            }
        };
    }

    private string ManifestText(LoadedPlugin plugin) => System.Text.Json.JsonSerializer.Serialize(plugin.Manifest, new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true
    });

    private string LogTail(LoadedPlugin plugin)
    {
        try
        {
            var latest = Directory.EnumerateFiles(_settings.Paths.Logs, "weed-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null)
            {
                return "No logs yet.";
            }

            var lines = File.ReadLines(latest)
                .Where(line => line.Contains(plugin.Manifest.Id, StringComparison.OrdinalIgnoreCase) ||
                               line.Contains(plugin.Manifest.Name, StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("[Host]", StringComparison.OrdinalIgnoreCase))
                .TakeLast(200);
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }

    private static bool DefaultBool(PluginSettingDefinition setting) =>
        bool.TryParse(setting.DefaultValue, out var value) && value;

    private static int DefaultInt(PluginSettingDefinition setting) =>
        int.TryParse(setting.DefaultValue, out var value) ? value : 0;

    private static string ActivationText(PluginActivationManifest activation) =>
        $"{activation.Type}: {activation.Keyword ?? activation.Provider ?? activation.Command ?? activation.DefaultKeys ?? ""}";

    private static UIElement PluginIconElement(LoadedPlugin plugin, double size = 24)
    {
        var path = ResolveIconPath(plugin.Manifest.Icon, plugin.PluginDirectory);
        var image = LoadImage(path);
        if (image is not null)
        {
            return new System.Windows.Controls.Image
            {
                Source = image,
                Width = size,
                Height = size,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = plugin.Manifest.Name.Length == 0 ? "*" : plugin.Manifest.Name[0].ToString().ToUpperInvariant(),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"],
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static string? ResolveIconPath(string? icon, string? pluginDirectory)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        if (Path.IsPathRooted(icon))
        {
            return icon;
        }

        var root = string.IsNullOrWhiteSpace(pluginDirectory) ? AppContext.BaseDirectory : pluginDirectory;
        return Path.Combine(root, icon);
    }

    private static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string DependencyKey(string pluginId, string dependencyId) => $"{pluginId}:{dependencyId}";

    private string DependencyText(LoadedPlugin plugin)
    {
        if (plugin.Manifest.ExternalDependencies.Count == 0) return "None";
        var statuses = _dependencyStatuses.ToDictionary(status => DependencyKey(status.PluginId, status.DependencyId), StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, plugin.Manifest.ExternalDependencies.Select(dependency =>
        {
            var state = statuses.TryGetValue(DependencyKey(plugin.Manifest.Id, dependency.Id), out var status)
                ? status.Available ? "Available" : status.Message
                : "Not checked";
            return $"{dependency.Name}: {state}";
        }));
    }

    private void SetLaunchAtStartup(System.Windows.Controls.CheckBox checkBox, bool enabled)
    {
        try
        {
            var executablePath = StartupManager.CurrentExecutablePath() ?? throw new InvalidOperationException("The Weed executable path is unavailable.");

            StartupManager.SetEnabled(enabled, executablePath);
            _settings.SetAppSettings(_settings.AppSettings with { LaunchAtStartup = enabled });
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to update startup setting.", ex);
            checkBox.Tag = true;
            checkBox.IsChecked = !enabled;
            checkBox.Tag = null;
            System.Windows.MessageBox.Show($"Failed to update startup setting: {ex.Message}", "Weed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private UIElement BuildHotkeysTab()
    {
        var rows = new List<UIElement>();
        foreach (var hotkey in _settings.Hotkeys)
        {
            var controls = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var hotkeyKey = hotkey.Key;
            var keyBox = HotkeyCaptureBox(hotkey.Value.Keys, 150, value =>
            {
                var current = _settings.Hotkeys.TryGetValue(hotkeyKey, out var existing)
                    ? existing
                    : hotkey.Value;
                _settings.Hotkeys[hotkeyKey] = current with { Keys = value };
                _settings.Save();
                HotkeysChanged?.Invoke();
            });

            var enabled = new System.Windows.Controls.CheckBox
            {
                IsChecked = hotkey.Value.Enabled,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            enabled.Checked += (_, _) =>
            {
                var current = _settings.Hotkeys.TryGetValue(hotkeyKey, out var existing)
                    ? existing
                    : hotkey.Value;
                _settings.Hotkeys[hotkeyKey] = current with { Enabled = true };
                _settings.Save();
                HotkeysChanged?.Invoke();
            };
            enabled.Unchecked += (_, _) =>
            {
                var current = _settings.Hotkeys.TryGetValue(hotkeyKey, out var existing)
                    ? existing
                    : hotkey.Value;
                _settings.Hotkeys[hotkeyKey] = current with { Enabled = false };
                _settings.Save();
                HotkeysChanged?.Invoke();
            };
            controls.Children.Add(keyBox);
            controls.Children.Add(enabled);

            rows.Add(SettingRow(hotkey.Key, "Plugin or app shortcut.", controls));
        }

        if (rows.Count == 0)
        {
            rows.Add(SettingRow("No hotkeys", "Plugins have not registered configurable hotkeys yet.", TextValue("")));
        }

        return PageShell(
            "Hotkeys",
            "Global shortcuts registered by Weed and its plugins.",
            [Section("Shortcuts", rows.ToArray())]);
    }
}
