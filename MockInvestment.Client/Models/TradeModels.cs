namespace MockInvestment.Client.Models;

public class TradeRequest
{
    public int StockId { get; set; }

    public int Quantity { get; set; }

    public Guid ClientOrderId { get; set; }
}

public class TradeResponse
{
    public long TradeId { get; set; }

    public Guid ClientOrderId { get; set; }

    public int StockId { get; set; }

    public string TradeType { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTime TradedAt { get; set; }

    public bool IsReplay { get; set; }
}