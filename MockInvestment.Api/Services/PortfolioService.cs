
using System.Text.Json;
using System.Text.RegularExpressions;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class PortfolioService
{
    private readonly PortfolioRepository _repository;
    private readonly QuoteCacheService _quoteService;
    private readonly ILogger<PortfolioService> _logger;

    public PortfolioService(
        PortfolioRepository repository,
        QuoteCacheService quoteService,
        ILogger<PortfolioService> logger)
    {
        _repository = repository;
        _quoteService = quoteService;
        _logger = logger;
    }

    public async Task<PortfolioResponse?> GetAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var data = await _repository.GetAsync(
            userId,
            cancellationToken);

        if (data is null)
            return null;

        return await CalculatePortfolioAsync(
            data,
            cancellationToken);
    }

    public async Task<PortfolioResponse?> GetCompetitionAsync(
        int userId,
        int competitionId,
        CancellationToken cancellationToken)
    {
        var data = await _repository.GetCompetitionAsync(
            userId,
            competitionId,
            cancellationToken);

        if (data is null)
            return null;

        return await CalculatePortfolioAsync(
            data,
            cancellationToken);
    }

    private async Task<PortfolioResponse> CalculatePortfolioAsync(
        PortfolioData data,
        CancellationToken cancellationToken)
    {
        var response = new PortfolioResponse
        {
            InitialBalance = data.InitialBalance,
            CashBalance = data.CashBalance
        };

        bool allPricesAvailable = true;

        foreach (var holding in data.Holdings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = new HoldingResponse
            {
                StockId = holding.StockId,
                StockCode = holding.StockCode,
                StockName = holding.StockName,
                Quantity = holding.Quantity,
                AverageBuyPrice = holding.AverageBuyPrice,
                QuoteStatus = "UNAVAILABLE"
            };

            bool supported =
                holding.Market is "KOSPI" or "KOSDAQ"
                && Regex.IsMatch(
                    holding.StockCode,
                    @"^[0-9]{6}$");

            if (supported)
            {
                try
                {
                    var quote =
                        await _quoteService.GetQuoteAsync(
                            holding.StockCode,
                            cancellationToken);

                    item.CurrentPrice = quote.Price;
                    item.FetchedAt = quote.FetchedAt;

                    item.EvaluationAmount =
                        quote.Price * holding.Quantity;

                    item.UnrealizedProfitLoss = RoundMoney(
                        item.EvaluationAmount.Value
                        - holding.AverageBuyPrice
                        * holding.Quantity);

                    item.QuoteStatus = "AVAILABLE";
                }
                catch (OperationCanceledException)
                    when (!cancellationToken
                        .IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "자산 평가 시세 응답 지연: {StockCode}",
                        holding.StockCode);
                }
                catch (Exception ex) when (
                    ex is HttpRequestException
                    or InvalidOperationException
                    or JsonException)
                {
                    _logger.LogWarning(
                        "자산 평가 시세 조회 실패: {StockCode}, {Reason}",
                        holding.StockCode,
                        ex.Message);
                }
            }

            if (item.CurrentPrice is null)
                allPricesAvailable = false;

            response.Holdings.Add(item);
        }

        if (allPricesAvailable)
        {
            decimal evaluationAmount =
                response.Holdings.Sum(
                    x => x.EvaluationAmount ?? 0m);

            decimal unrealizedProfitLoss =
                response.Holdings.Sum(
                    x => x.UnrealizedProfitLoss ?? 0m);

            decimal totalAssets =
                data.CashBalance + evaluationAmount;

            decimal totalProfitLoss =
                totalAssets - data.InitialBalance;

            response.TotalEvaluationAmount = evaluationAmount;

            response.TotalUnrealizedProfitLoss =
                unrealizedProfitLoss;

            response.TotalAssets = totalAssets;
            response.TotalProfitLoss = totalProfitLoss;

            response.TotalReturnRate =
                data.InitialBalance > 0
                    ? RoundMoney(
                        totalProfitLoss
                        / data.InitialBalance
                        * 100m)
                    : 0m;

            response.ValuationStatus = "AVAILABLE";
        }
        else
        {
            response.ValuationStatus = "UNAVAILABLE";
        }

        response.CalculatedAt = DateTimeOffset.UtcNow;

        return response;
    }

    private static decimal RoundMoney(decimal value)
    {
        return decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
    }
}
