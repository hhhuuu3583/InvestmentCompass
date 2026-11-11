namespace MockInvestment.Api.Models;

public class TradeHistoryItem
{
    public long TradeId { get; set; }

    public int StockId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string StockName { get; set; } = string.Empty;

    public string TradeType { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTime TradedAt { get; set; }
}