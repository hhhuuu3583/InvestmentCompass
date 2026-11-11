namespace MockInvestment.Api.Models;

public class RankingAccountData
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public List<HoldingData> Holdings { get; set; } = new();
}