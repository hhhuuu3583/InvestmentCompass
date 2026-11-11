namespace MockInvestment.Client.Models;

public class CandleResponse
{
    public int StockId { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string PriceType { get; set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; set; }
    public List<CandleItem> Items { get; set; } = new();
}

public class CandleItem
{
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public DateTimeOffset? Time { get; set; }
}