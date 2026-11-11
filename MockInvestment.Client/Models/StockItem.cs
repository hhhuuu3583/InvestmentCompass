namespace MockInvestment.Client.Models;

public class StockItem
{
    public int StockId { get; set; }

    public string StockCode { get; set; } = string.Empty;

    public string StockName { get; set; } = string.Empty;

    public string Market { get; set; } = string.Empty;
}

public class StockListResponse
{
    public List<StockItem> Items { get; set; } = new();
}