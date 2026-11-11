
namespace MockInvestment.Api.Models;

public class CreateCompetitionRequest
{
    public string Title { get; set; } = "";

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public decimal InitialBalance { get; set; }
}
