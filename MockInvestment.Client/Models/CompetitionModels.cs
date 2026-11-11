namespace MockInvestment.Client.Models;

public class Competition
{
    public int CompetitionId { get; set; }

    public string Title { get; set; } = "";

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public decimal InitialBalance { get; set; }

    public string Status { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}