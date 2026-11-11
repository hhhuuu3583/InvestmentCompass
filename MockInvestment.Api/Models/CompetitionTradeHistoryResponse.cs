
namespace MockInvestment.Api.Models;

public class CompetitionTradeHistoryResponse
{
    public long CompetitionTradeId { get; set; }

    public int CompetitionId { get; set; }

    public int StockId { get; set; }

    public string StockCode { get; set; } = "";

    public string StockName { get; set; } = "";

    public string TradeType { get; set; } = "";

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTime QuotedAt { get; set; }

    public DateTime TradedAt { get; set; }
}
