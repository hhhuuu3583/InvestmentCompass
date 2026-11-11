using System.Text.Json;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public sealed class ResearchDialogueService
{
    private readonly ResearchService _research;
    private readonly ResearchConversationStore _store;
    private readonly GeneralExplanationService _general;
    
    public ResearchDialogueService(
     ResearchService research,
     ResearchConversationStore store,
     GeneralExplanationService general)
    {
        _research = research;
        _store = store;
        _general = general;
    }

    public async Task<ResearchResponse> AskAsync(
        int userId,
        ResearchRequest request,
        CancellationToken cancellationToken)
    {
        string question = request.Question?.Trim() ?? "";

        if (question.Length < 2 || question.Length > 400)
        {
            throw new ArgumentException(
                "질문은 2~400자로 입력해주세요.");
        }

        if (request.Mode != "general" && request.Mode != "research")
        {
            throw new ArgumentException(
                "올바른 답변 방식을 선택해주세요.");
        }

        Guid conversationId;
        IReadOnlyList<ResearchTurn> history;

        if (request.ConversationId is Guid existingId)
        {
            conversationId = existingId;

            try
            {
                history = _store.GetHistory(
                    userId,
                    conversationId);
            }
            catch (InvalidOperationException)
            {
                throw new KeyNotFoundException(
                    "대화가 만료되었거나 접근할 수 없습니다. "
                    + "새 대화를 시작해주세요.");
            }
        }
        else
        {
            conversationId = _store.Create(userId);
            history = Array.Empty<ResearchTurn>();
        }

        // 두 모드 모두 사용자가 명시적으로 선택한 경우에만 참고.
        bool useHistory = request.UsePreviousContext;

        IEnumerable<ResearchTurn> contextTurns = useHistory
            ? history.TakeLast(1)
            : Array.Empty<ResearchTurn>();

        string context = JsonSerializer.Serialize(
            contextTurns.Select(turn => new
            {
                question = Limit(turn.Question, 120),
                answer = Limit(turn.Answer, 180)
            }));

        // 기본적으로 현재 질문만 검색
        string searchQuery = question;

        // 후속 질문으로 선택한 경우에만 직전 질문을 추가
        if (request.Mode == "research"
            && request.UsePreviousContext)
        {
            string previousQuestion =
                history.LastOrDefault()?.Question ?? "";

            int remaining = 400 - question.Length - 1;

            if (remaining > 0
                && !string.IsNullOrWhiteSpace(previousQuestion))
            {
                searchQuery += " " + Limit(
                    previousQuestion,
                    Math.Min(remaining, 180));
            }
        }

        ResearchResponse result;

        if (request.Mode == "general")
        {
            // 검색 없이 개념 설명
            result = await _general.AskAsync(
                question,
                context,
                cancellationToken);
        }
        else
        {
            // 기존 검색 기반 자료 조사
            result = await _research.AskAsync(
                question,
                cancellationToken,
                conversationContext: context,
                searchQuery: searchQuery);
        }

        result.Mode = request.Mode;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _store.Append(
                userId,
                conversationId,
                question,
                result.Answer);
        }
        catch (InvalidOperationException)
        {
            // AI 응답을 기다리는 동안 대화가 만료된 경우,
            // 받은 답변을 버리지 않고 새 대화 번호로 저장
            conversationId = _store.Create(userId);

            _store.Append(
                userId,
                conversationId,
                question,
                result.Answer);
        }

        result.ConversationId = conversationId;

        return result;
    }

    private static string Limit(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int length = maxLength;

        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

}