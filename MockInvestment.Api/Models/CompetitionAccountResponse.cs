
namespace MockInvestment.Api.Models;

public class CompetitionAccountResponse
{
    public int CompetitionAccountId { get; set; }

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public decimal StockEvaluation { get; set; }

    public decimal TotalAsset { get; set; }

    public decimal TotalProfit { get; set; }

    public decimal ProfitRate { get; set; }

    public List<CompetitionHoldingResponse> Holdings { get; set; } = new();
}

public class CompetitionHoldingResponse
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = "";

    public string StockName { get; set; } = "";

    public int Quantity { get; set; }

    public decimal AverageBuyPrice { get; set; }

    public decimal CurrentPrice { get; set; }

    public decimal EvaluationAmount { get; set; }

    public decimal Profit { get; set; }

    public decimal ProfitRate { get; set; }
}
