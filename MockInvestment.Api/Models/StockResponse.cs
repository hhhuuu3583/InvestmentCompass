namespace MockInvestment.Api.Models;

public class StockResponse
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string StockName { get; set; } = string.Empty;

    public string Market { get; set; } = string.Empty;
}