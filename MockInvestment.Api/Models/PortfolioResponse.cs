namespace MockInvestment.Api.Models;

public class PortfolioResponse
{
    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public decimal? TotalEvaluationAmount { get; set; }

    public decimal? TotalUnrealizedProfitLoss { get; set; }

    public decimal? TotalAssets { get; set; }

    public decimal? TotalProfitLoss { get; set; }

    public decimal? TotalReturnRate { get; set; }

    public string ValuationStatus { get; set; } = string.Empty;

    public DateTimeOffset CalculatedAt { get; set; }

    public List<HoldingResponse> Holdings { get; set; } = new();
}

public class HoldingResponse
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string StockName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal AverageBuyPrice { get; set; }

    public decimal? CurrentPrice { get; set; }

    public decimal? EvaluationAmount { get; set; }

    public decimal? UnrealizedProfitLoss { get; set; }

    public DateTimeOffset? FetchedAt { get; set; }

    public string QuoteStatus { get; set; } = string.Empty;
}