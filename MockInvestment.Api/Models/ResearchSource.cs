namespace MockInvestment.Api.Models;

public sealed class ResearchSource
{
    public string Title { get; set; } = "";

    public string Url { get; set; } = "";

    public string Content { get; set; } = "";

    // 검색 결과에 날짜가 없으면 null
    public string? PublishedDate { get; set; }
}