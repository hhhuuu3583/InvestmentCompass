namespace MockInvestment.Api.Models;

public class CompetitionApplication
{
    public int ApplicationId { get; set; }

    public int CompetitionId { get; set; }

    public int UserId { get; set; }

    public string LoginId { get; set; } = "";

    public string Nickname { get; set; } = "";

    public string Status { get; set; } = "대기";

    public DateTime AppliedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}