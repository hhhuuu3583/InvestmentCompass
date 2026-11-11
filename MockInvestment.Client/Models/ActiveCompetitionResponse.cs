namespace MockInvestment.Client.Models;

public class ActiveCompetitionResponse
{
    public bool IsParticipating { get; set; }

    public string Message { get; set; } = "";

    public ActiveCompetitionInfo? Competition { get; set; }
}

public class ActiveCompetitionInfo
{
    public int CompetitionId { get; set; }
    public int ParticipantId { get; set; }
    public int CompetitionAccountId { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal CashBalance { get; set; }
    public string CompetitionStatus { get; set; } = "";
}