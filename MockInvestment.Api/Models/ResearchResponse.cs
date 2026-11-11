namespace MockInvestment.Api.Models;

public sealed class ResearchResponse
{
    public Guid? ConversationId { get; set; }

    public string Mode { get; set; } = "research";

    public string Answer { get; set; } = "";

    public DateTimeOffset GeneratedAt { get; set; }

    // 답변 속 각 인용 위치: 같은 출처가 여러 번 나올 수 있음
    public List<ResearchCitation> Citations { get; set; } = new();

    // 화면에 표시할 출처 목록: 같은 URL은 한 번만 표시
    public List<ResearchSourceLink> Sources =>
        Citations
            .GroupBy(c => c.Url, StringComparer.Ordinal)
            .Select(group =>
            {
                var citation = group.First();

                return new ResearchSourceLink
                {
                    SourceNumber = citation.SourceNumber,
                    Title = citation.Title,
                    Url = citation.Url
                };
            })
            .OrderBy(source => source.SourceNumber)
            .ToList();
}

public sealed class ResearchCitation
{
    public int SourceNumber { get; set; }

    public string Title { get; set; } = "";

    public string Url { get; set; } = "";

    public int StartIndex { get; set; }

    public int EndIndex { get; set; }
}

public sealed class ResearchSourceLink
{
    public int SourceNumber { get; set; }

    public string Title { get; set; } = "";

    public string Url { get; set; } = "";
}