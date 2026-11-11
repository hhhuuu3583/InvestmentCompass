using Microsoft.Extensions.Caching.Memory;

namespace MockInvestment.Api.Services;

public class QuoteCacheService
{
    private readonly IMemoryCache _cache;
    private readonly KisQuoteService _quoteService;
    private readonly TimeSpan _cacheDuration;

    // 소규모 프로젝트에서는 외부 조회를 하나씩 처리
    private readonly SemaphoreSlim _lock = new(1, 1);

    public QuoteCacheService(
        IMemoryCache cache,
        KisQuoteService quoteService,
        IConfiguration configuration)
    {
        _cache = cache;
        _quoteService = quoteService;

        int seconds =
            configuration.GetValue<int>("QuoteCache:Seconds");

        if (seconds <= 0)
        {
            throw new InvalidOperationException(
                "현재가 캐시 시간은 1초 이상이어야 합니다.");
        }

        _cacheDuration = TimeSpan.FromSeconds(seconds);
    }

    public async Task<(
        decimal Price,
        DateTimeOffset FetchedAt,
        bool IsCached)> GetQuoteAsync(
            string stockCode,
            CancellationToken cancellationToken)
    {
        string cacheKey = $"kis:quote:KRX:{stockCode}";

        // 1. 유효한 캐시가 있으면 즉시 반환
        if (_cache.TryGetValue(
                cacheKey, out CachedQuote? cached)
            && cached is not null)
        {
            return (cached.Price, cached.FetchedAt, true);
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            // 2. 기다리는 동안 다른 요청이 저장했는지 재확인
            if (_cache.TryGetValue(
                    cacheKey, out cached)
                && cached is not null)
            {
                return (cached.Price, cached.FetchedAt, true);
            }

            // 3. 외부 API 조회
            var quote = await _quoteService.GetQuoteAsync(
                stockCode,
                cancellationToken);

            var entry = new CachedQuote(
                quote.Price,
                quote.FetchedAt);

            // 4. 수신 시각 기준으로 만료 시각 설정
            _cache.Set(
                cacheKey,
                entry,
                quote.FetchedAt.Add(_cacheDuration));

            return (entry.Price, entry.FetchedAt, false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record CachedQuote(
        decimal Price,
        DateTimeOffset FetchedAt);
}