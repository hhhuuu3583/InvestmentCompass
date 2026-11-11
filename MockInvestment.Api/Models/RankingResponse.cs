namespace MockInvestment.Api.Models;

public class RankingResponse
{
    public DateTimeOffset CalculatedAt { get; set; }

    public List<RankingItem> Items { get; set; } = new();
}

public class RankingItem
{
    public int Rank { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public decimal TotalAssets { get; set; }

    public decimal ReturnRate { get; set; }

    public bool IsMe { get; set; }
}