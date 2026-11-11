namespace MockInvestment.Api.Models;

public class CompetitionFinalRankingResponse
{
    public int Rank { get; set; }

    public int ParticipantId { get; set; }

    public int UserId { get; set; }

    public string Nickname { get; set; } = "";

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public decimal StockEvaluation { get; set; }

    public decimal TotalAsset { get; set; }

    public decimal TotalProfit { get; set; }

    public decimal ProfitRate { get; set; }

    public DateTime FinalizedAt { get; set; }
}