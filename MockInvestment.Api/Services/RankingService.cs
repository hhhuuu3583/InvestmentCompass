using System.Text.RegularExpressions;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class RankingService
{
    private readonly RankingRepository _repository;
    private readonly QuoteCacheService _quoteService;

    public RankingService(
        RankingRepository repository,
        QuoteCacheService quoteService)
    {
        _repository = repository;
        _quoteService = quoteService;
    }

    public async Task<RankingResponse> GetAsync(
        int currentUserId,
        CancellationToken cancellationToken)
    {
        var accounts = await _repository.GetAccountsAsync(
            cancellationToken);

        // 1. 전체 회원이 보유한 종목을 중복 없이 추출
        var holdings = accounts
            .SelectMany(x => x.Holdings)
            .ToList();

        if (holdings.Any(x =>
                x.Market is not ("KOSPI" or "KOSDAQ")
                || !Regex.IsMatch(
                    x.StockCode, @"^[0-9]{6}$")))
        {
            throw new InvalidOperationException(
                "평가를 지원하지 않는 보유 종목이 있습니다.");
        }

        var stockCodes = holdings
            .Select(x => x.StockCode)
            .Distinct()
            .ToList();

        // 2. 종목마다 가격을 한 번 확보
        var prices = new Dictionary<string, decimal>();

        var fetchedTimes = new List<DateTimeOffset>();

        foreach (string stockCode in stockCodes)
        {
            var quote = await _quoteService.GetQuoteAsync(
                stockCode,
                cancellationToken);

            prices.Add(stockCode, quote.Price);
            fetchedTimes.Add(quote.FetchedAt);
        }

        // 개발용 기준: 계산 시작에 사용할 수신 시세가
        // 10초를 넘겼으면 순위 확정을 보류
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (fetchedTimes.Any(time =>
                now - time > TimeSpan.FromSeconds(10)
                || time > now))
        {
            throw new InvalidOperationException(
                "순위 계산에 사용할 시세가 오래되었습니다.");
        }

        // 3. 같은 가격 묶음으로 모든 회원 계산
        var calculated = accounts.Select(account =>
        {
            if (account.InitialBalance <= 0)
            {
                throw new InvalidOperationException(
                    "초기 투자금이 올바르지 않습니다.");
            }

            decimal evaluationAmount =
                account.Holdings.Sum(holding =>
                    prices[holding.StockCode]
                    * holding.Quantity);

            decimal totalAssets =
                account.CashBalance + evaluationAmount;

            decimal returnRate =
                (totalAssets - account.InitialBalance)
                / account.InitialBalance
                * 100m;

            return new
            {
                account.UserId,
                account.Nickname,
                TotalAssets = totalAssets,
                ReturnRate = returnRate
            };
        })
        .OrderByDescending(x => x.ReturnRate)
        .ThenBy(x => x.UserId)
        .ToList();

        // 4. 공동 순위 계산
        var response = new RankingResponse();

        decimal? previousRate = null;
        int rank = 0;

        for (int i = 0; i < calculated.Count; i++)
        {
            var item = calculated[i];

            if (previousRate is null
                || item.ReturnRate != previousRate.Value)
            {
                rank = i + 1;
            }

            response.Items.Add(new RankingItem
            {
                Rank = rank,
                Nickname = item.Nickname,

                TotalAssets = decimal.Round(
                    item.TotalAssets,
                    2,
                    MidpointRounding.AwayFromZero),

                ReturnRate = decimal.Round(
                    item.ReturnRate,
                    2,
                    MidpointRounding.AwayFromZero),

                IsMe = item.UserId == currentUserId
            });

            previousRate = item.ReturnRate;
        }

        response.CalculatedAt = DateTimeOffset.UtcNow;

        return response;
    }
}