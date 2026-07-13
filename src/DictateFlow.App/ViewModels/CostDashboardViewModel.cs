using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Usage;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>One dashboard card (Today / This month / Lifetime) with display-formatted metrics.</summary>
public sealed class CostPeriodItem
{
    /// <summary>Initializes a new instance of the <see cref="CostPeriodItem"/> class.</summary>
    /// <param name="title">Card heading.</param>
    /// <param name="period">The aggregated figures to present.</param>
    /// <param name="currency">Display currency code.</param>
    public CostPeriodItem(string title, CostPeriod period, string currency)
    {
        Title = title;
        Words = period.Words.ToString("N0");
        SpeechRequests = period.SpeechRequests.ToString("N0");
        SpeechMinutes = period.SpeechMinutes.ToString("N1");
        SpeechCost = FormatCost(period.SpeechCost, currency);
        LlmRequests = period.LlmRequests.ToString("N0");
        PromptTokens = period.PromptTokens.ToString("N0");
        CompletionTokens = period.CompletionTokens.ToString("N0");
        LlmCost = FormatCost(period.LlmCost, currency);
        TotalCost = FormatCost(period.TotalCost, currency);
    }

    /// <summary>Gets the card heading.</summary>
    public string Title { get; }

    /// <summary>Gets the number of raw dictated words.</summary>
    public string Words { get; }

    /// <summary>Gets the number of speech calls.</summary>
    public string SpeechRequests { get; }

    /// <summary>Gets the minutes of audio transcribed.</summary>
    public string SpeechMinutes { get; }

    /// <summary>Gets the estimated speech cost.</summary>
    public string SpeechCost { get; }

    /// <summary>Gets the number of LLM calls.</summary>
    public string LlmRequests { get; }

    /// <summary>Gets the prompt tokens sent.</summary>
    public string PromptTokens { get; }

    /// <summary>Gets the completion tokens received.</summary>
    public string CompletionTokens { get; }

    /// <summary>Gets the estimated LLM cost.</summary>
    public string LlmCost { get; }

    /// <summary>Gets the combined estimated cost.</summary>
    public string TotalCost { get; }

    /// <summary>Formats a cost with enough decimals for sub-cent amounts.</summary>
    private static string FormatCost(double cost, string currency)
        => $"{cost:0.0000} {currency}";
}

/// <summary>
/// View model backing the Cost Dashboard window: the Today / This month / Lifetime cards
/// computed by <see cref="ICostService"/>, refreshed on demand. Costs are estimates from
/// the configured pricing rates, applied when each call was recorded.
/// </summary>
public partial class CostDashboardViewModel : ObservableObject
{
    private readonly ICostService _costService;
    private readonly ILogger<CostDashboardViewModel> _logger;

    /// <summary>Initializes a new instance of the <see cref="CostDashboardViewModel"/> class.</summary>
    /// <param name="costService">Aggregates the persisted usage records.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public CostDashboardViewModel(ICostService costService, ILogger<CostDashboardViewModel> logger)
    {
        _costService = costService;
        _logger = logger;
    }

    /// <summary>Gets or sets the three dashboard cards, in Today / This month / Lifetime order.</summary>
    [ObservableProperty]
    private IReadOnlyList<CostPeriodItem> _periods = [];

    /// <summary>Gets or sets the status line (last refresh time or an error).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Recomputes the summary from the database.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _costService.GetSummaryAsync(cancellationToken);
            Periods =
            [
                new CostPeriodItem("Today", summary.Today, summary.Currency),
                new CostPeriodItem("This month", summary.ThisMonth, summary.Currency),
                new CostPeriodItem("Lifetime", summary.Lifetime, summary.Currency),
            ];
            StatusMessage = $"Updated {DateTime.Now:T}. Estimates from your configured rates — see Settings → Pricing.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cost summary refresh failed");
            StatusMessage = "Could not load the cost summary.";
        }
    }
}
