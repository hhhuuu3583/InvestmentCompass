namespace MockInvestment.Client.Models;

public class TradeHistoryResponse
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public long TotalCount { get; set; }

    public List<TradeHistoryItem> Items { get; set; } = new();
}

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

    public DateTimeOffset TradedAt { get; set; }

    public string TradeTypeText =>
        TradeType switch
        {
            "BUY" => "매수",
            "SELL" => "매도",
            _ => TradeType
        };

    public string LocalTradedAt =>
        TradedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}