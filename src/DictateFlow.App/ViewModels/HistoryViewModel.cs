using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.History;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>One history row prepared for display: local-time stamp and a single-line preview.</summary>
public sealed class HistoryEntryItem
{
    /// <summary>Initializes a new instance of the <see cref="HistoryEntryItem"/> class.</summary>
    /// <param name="entry">The stored entry (UTC timestamp) to present.</param>
    public HistoryEntryItem(HistoryEntry entry)
    {
        Entry = entry;
        TimestampText = entry.TimestampUtc.ToLocalTime().ToString("g");
        var singleLine = entry.FinalText.ReplaceLineEndings(" ").Trim();
        Preview = singleLine.Length <= 120 ? singleLine : $"{singleLine[..120].TrimEnd()}…";
    }

    /// <summary>Gets the underlying stored entry.</summary>
    public HistoryEntry Entry { get; }

    /// <summary>Gets the delivery time formatted in local time.</summary>
    public string TimestampText { get; }

    /// <summary>Gets the single-line text preview shown in the list.</summary>
    public string Preview { get; }

    /// <summary>Gets the full final text (shown in the detail pane and copied by Copy).</summary>
    public string FullText => Entry.FinalText;
}

/// <summary>
/// View model backing the History window: a debounced search over the stored final texts,
/// newest first, with per-entry Copy and Delete plus a confirmed Clear all. All timestamps
/// are converted to local time for display only.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    /// <summary>How long typing must pause before the search runs.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>Maximum number of entries loaded into the list at once.</summary>
    private const int SearchLimit = 500;

    private readonly IHistoryRepository _repository;
    private readonly ILogger<HistoryViewModel> _logger;
    private CancellationTokenSource? _debounceCts;

    /// <summary>Initializes a new instance of the <see cref="HistoryViewModel"/> class.</summary>
    /// <param name="repository">Reads and mutates the stored history.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public HistoryViewModel(IHistoryRepository repository, ILogger<HistoryViewModel> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Gets or sets the entries currently shown, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<HistoryEntryItem> _entries = [];

    /// <summary>Gets or sets the entry selected in the list; its full text fills the detail pane.</summary>
    [ObservableProperty]
    private HistoryEntryItem? _selectedEntry;

    /// <summary>Gets or sets the search box text; changes re-run the search after a short pause.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Gets or sets the status line under the list (entry count or an error).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Debounces typing into a search run.</summary>
    /// <param name="value">The new search text.</param>
    partial void OnSearchTextChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = DebouncedSearchAsync(_debounceCts.Token);
    }

    /// <summary>Waits out the debounce interval, then refreshes; a newer keystroke cancels the wait.</summary>
    private async Task DebouncedSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Loads the entries matching the current search text.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _repository.SearchAsync(SearchText, SearchLimit, cancellationToken);
            Entries = [.. entries.Select(e => new HistoryEntryItem(e))];
            StatusMessage = Entries.Count == 1 ? "1 entry" : $"{Entries.Count} entries";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search or the window closed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "History search failed");
            StatusMessage = "Could not load history.";
        }
    }

    /// <summary>Copies an entry's full text to the clipboard.</summary>
    /// <param name="entry">The entry to copy.</param>
    [RelayCommand]
    private void Copy(HistoryEntryItem? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(entry.FullText);
            StatusMessage = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process.
            _logger.LogWarning(ex, "Could not copy a history entry to the clipboard");
            StatusMessage = "Could not access the clipboard — try again.";
        }
    }

    /// <summary>Deletes one entry and refreshes the list.</summary>
    /// <param name="entry">The entry to delete.</param>
    [RelayCommand]
    private async Task DeleteAsync(HistoryEntryItem? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            await _repository.DeleteAsync(entry.Entry.Id);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete history entry {Id}", entry.Entry.Id);
            StatusMessage = "Could not delete the entry.";
        }
    }

    /// <summary>Deletes all history after an explicit confirmation.</summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        var confirmed = MessageBox.Show(
            "Delete all history? This cannot be undone.",
            "DictateFlow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _repository.ClearAsync();
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not clear the history");
            StatusMessage = "Could not clear the history.";
        }
    }
}
