namespace MockInvestment.Api.Models;

public class ActiveCompetitionResponse
{
    public int CompetitionId { get; set; }

    public int ParticipantId { get; set; }

    public int CompetitionAccountId { get; set; }

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }

    public string CompetitionStatus { get; set; } = "";
}