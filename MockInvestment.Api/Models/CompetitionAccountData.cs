
namespace MockInvestment.Api.Models;

public class CompetitionAccountData
{
    public int CompetitionAccountId { get; set; }

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public List<CompetitionHoldingData> Holdings { get; set; } = new();
}

public class CompetitionHoldingData
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = "";

    public string StockName { get; set; } = "";

    public int Quantity { get; set; }

    public decimal AverageBuyPrice { get; set; }
}
