namespace MockInvestment.Api.Models;

public class CandleItem
{
    public DateOnly Date { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public long Volume { get; set; }

    // 분봉의 시작 시각. 일봉·주봉·월봉은 null
    public DateTimeOffset? Time { get; set; }
}