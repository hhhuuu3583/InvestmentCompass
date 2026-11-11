namespace MockInvestment.Client.Models;

public class QuoteResponse
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string StockName { get; set; } = string.Empty;

    public string Market { get; set; } = string.Empty;

    public decimal CurrentPrice { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public bool IsCached { get; set; }
}