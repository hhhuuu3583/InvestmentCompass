namespace MockInvestment.Api.Models;

public class CompetitionParticipantRequest
{
    public int UserId { get; set; }
}

public class CompetitionParticipantResponse
{
    public int ParticipantId { get; set; }

    public int CompetitionAccountId { get; set; }

    public int CompetitionId { get; set; }

    public int UserId { get; set; }

    public decimal InitialBalance { get; set; }

    public decimal CashBalance { get; set; }
}