namespace MockInvestment.Api.Models;

public class CompetitionParticipantListResponse
{
    public int ParticipantId { get; set; }

    public int CompetitionId { get; set; }

    public int UserId { get; set; }

    public string LoginId { get; set; } = "";

    public string Nickname { get; set; } = "";

    public DateTime JoinedAt { get; set; }

    public int CompetitionAccountId { get; set; }

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }
}
