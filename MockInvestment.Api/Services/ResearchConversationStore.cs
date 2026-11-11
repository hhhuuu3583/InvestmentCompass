using Microsoft.Extensions.Caching.Memory;

namespace MockInvestment.Api.Services;

public sealed class ResearchConversationStore : IDisposable
{
    // 다른 캐시와 분리하여 대화 저장 개수를 제한
    private readonly MemoryCache _cache = new(
        new MemoryCacheOptions
        {
            SizeLimit = 200
        });

    private readonly object _sync = new();

    private const int MaxTurns = 2;
    private const int MaxAnswerCharacters = 400;

    public Guid Create(int userId)
    {
        ValidateUser(userId);

        lock (_sync)
        {
            Guid conversationId = Guid.NewGuid();

            _cache.Set(
                (userId, conversationId),
                new ConversationState(),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromMinutes(20),
                    Size = 1
                });

            return conversationId;
        }
    }

    public IReadOnlyList<ResearchTurn> GetHistory(
        int userId,
        Guid conversationId)
    {
        ValidateUser(userId);

        lock (_sync)
        {
            var state = GetState(userId, conversationId);

            // 保存 중인 목록을 외부에서 변경하지 못하도록 복사
            return state.Turns.ToArray();
        }
    }

    public void Append(
        int userId,
        Guid conversationId,
        string question,
        string answer)
    {
        ValidateUser(userId);

        if (string.IsNullOrWhiteSpace(question)
            || question.Trim().Length > 400)
        {
            throw new ArgumentException(
                "질문은 1~400자로 입력해주세요.");
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new ArgumentException(
                "빈 답변은 대화 기록에 저장할 수 없습니다.");
        }

        lock (_sync)
        {
            var state = GetState(userId, conversationId);

            state.Turns.Add(new ResearchTurn(
                question.Trim(),
                Limit(answer.Trim(), MaxAnswerCharacters)));

            while (state.Turns.Count > MaxTurns)
            {
                state.Turns.RemoveAt(0);
            }
        }
    }

    private ConversationState GetState(
        int userId,
        Guid conversationId)
    {
        // 같은 대화 번호라도 로그인 사용자 ID가 다르면 조회 불가
        if (!_cache.TryGetValue(
                (userId, conversationId),
                out ConversationState? state)
            || state is null)
        {
            throw new InvalidOperationException(
                "대화가 만료되었거나 접근할 수 없습니다. "
                + "새 대화를 시작해주세요.");
        }

        return state;
    }

    private static void ValidateUser(int userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentException(
                "올바른 사용자 정보가 필요합니다.");
        }
    }

    private static string Limit(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int length = maxLength;

        // 이모지 등의 UTF-16 문자 중간을 자르지 않음
        if (char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private sealed class ConversationState
    {
        public List<ResearchTurn> Turns { get; } = new();
    }
}

public sealed record ResearchTurn(
    string Question,
    string Answer);