namespace MockInvestment.Api.Models;

public class PortfolioData
{
    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public List<HoldingData> Holdings { get; set; } = new();
}

public class HoldingData
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string StockName { get; set; } = string.Empty;

    public string Market { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal AverageBuyPrice { get; set; }
}