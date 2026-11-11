namespace MockInvestment.Client.Models;

public sealed class ResearchReply
{
    public string Answer { get; set; } = "";

    public DateTimeOffset GeneratedAt { get; set; }

    public List<ResearchLink> Sources { get; set; } = new();

    public Guid? ConversationId { get; set; }

    public string Mode { get; set; } = "";
}

public sealed class ResearchLink
{
    public int SourceNumber { get; set; }

    public string Title { get; set; } = "";

    public string Url { get; set; } = "";
}