
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class CompetitionAccountService
{
    private readonly CompetitionAccountRepository _accountRepository;
    private readonly QuoteCacheService _quoteService;

    public CompetitionAccountService(
        CompetitionAccountRepository accountRepository,
        QuoteCacheService quoteService)
    {
        _accountRepository = accountRepository;
        _quoteService = quoteService;
    }

    // 대회 계좌 조회
    public async Task<CompetitionAccountResponse?> GetAccountAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
        {
            throw new TradeException(
                "INVALID_COMPETITION",
                "올바르지 않은 대회 번호입니다.");
        }

        if (userId <= 0)
        {
            throw new TradeException(
                "INVALID_USER",
                "사용자 정보를 확인할 수 없습니다.");
        }

        // DB에서 대회 계좌 조회
        CompetitionAccountData? account =
            await _accountRepository.GetAccountAsync(
                competitionId,
                userId,
                cancellationToken);

        if (account == null)
            return null;

        CompetitionAccountResponse response =
            new CompetitionAccountResponse
            {
                CompetitionAccountId = account.CompetitionAccountId,
                InitialBalance = account.InitialBalance,
                CashBalance = account.CashBalance
            };

        decimal stockEvaluation = 0;

        // 보유 종목별 현재 평가금액 계산
        foreach (var holding in account.Holdings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var quote = await _quoteService.GetQuoteAsync(
                holding.StockCode,
                cancellationToken);

            decimal currentPrice = quote.Price;

            decimal evaluationAmount =
                currentPrice * holding.Quantity;

            decimal purchaseAmount =
                holding.AverageBuyPrice * holding.Quantity;

            decimal profit =
                evaluationAmount - purchaseAmount;

            decimal profitRate = purchaseAmount > 0
                ? Math.Round(
                    profit / purchaseAmount * 100,
                    2)
                : 0;

            response.Holdings.Add(
                new CompetitionHoldingResponse
                {
                    StockId = holding.StockId,
                    StockCode = holding.StockCode,
                    StockName = holding.StockName,
                    Quantity = holding.Quantity,
                    AverageBuyPrice = holding.AverageBuyPrice,
                    CurrentPrice = currentPrice,
                    EvaluationAmount = evaluationAmount,
                    Profit = profit,
                    ProfitRate = profitRate
                });

            stockEvaluation += evaluationAmount;
        }

        // 전체 계좌 자산 계산
        response.StockEvaluation = stockEvaluation;

        response.TotalAsset =
            response.CashBalance + response.StockEvaluation;

        response.TotalProfit =
            response.TotalAsset - response.InitialBalance;

        response.ProfitRate = response.InitialBalance > 0
            ? Math.Round(
                response.TotalProfit / response.InitialBalance * 100,
                2)
            : 0;

        return response;
    }
}
