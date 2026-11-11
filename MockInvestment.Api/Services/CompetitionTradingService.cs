
using System.Text.RegularExpressions;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class CompetitionTradingService
{
    private readonly CompetitionTradeRepository _competitionTradeRepository;
    private readonly StockRepository _stockRepository;
    private readonly QuoteCacheService _quoteService;
    private readonly CompetitionRepository _competitionRepository;

    public CompetitionTradingService(
    CompetitionTradeRepository competitionTradeRepository,
    StockRepository stockRepository,
    QuoteCacheService quoteService,
    CompetitionRepository competitionRepository)
    {
        _competitionTradeRepository = competitionTradeRepository;
        _stockRepository = stockRepository;
        _quoteService = quoteService;
        _competitionRepository = competitionRepository;
    }
    private async Task ValidateCompetitionStatusAsync(
    int competitionId,
    CancellationToken cancellationToken)
    {
        var competition = await _competitionRepository.GetByIdAsync(
            competitionId,
            cancellationToken);

        if (competition == null)
        {
            throw new TradeException(
                "COMPETITION_NOT_FOUND",
                "존재하지 않는 대회입니다.");
        }

        if (competition.Status != "진행중")
        {
            throw new TradeException(
                "COMPETITION_NOT_ACTIVE",
                "현재 진행 중인 대회가 아니므로 거래할 수 없습니다.");
        }
    }
    // 대회 매수
    public async Task<TradeResponse> BuyAsync(
        int competitionId,
        int userId,
        TradeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(competitionId, userId, request);

        // 중복 주문 확인
        TradeResponse? existingTrade =
            await _competitionTradeRepository.FindByOrderIdAsync(
                competitionId,
                userId,
                request.ClientOrderId,
                cancellationToken);

        if (existingTrade != null)
            return existingTrade;
        // 대회 상태 확인
        await ValidateCompetitionStatusAsync(
            competitionId,
            cancellationToken);

        // 종목 조회
        var stock = await _stockRepository.FindByIdAsync(
            request.StockId,
            cancellationToken);

        ValidateStock(stock);

        // 실시간 시세 조회
        var quote = await _quoteService.GetQuoteAsync(
            stock!.StockCode,
            cancellationToken);

        if (quote.Price <= 0)
        {
            throw new TradeException(
                "QUOTE_UNAVAILABLE",
                "현재 주식 가격을 확인할 수 없습니다.");
        }

        // 대회 전용 계좌로 매수
        return await _competitionTradeRepository.BuyAsync(
            competitionId,
            userId,
            request,
            quote.Price,
            quote.FetchedAt.UtcDateTime,
            cancellationToken);
    }

    // 대회 매도
    public async Task<TradeResponse> SellAsync(
        int competitionId,
        int userId,
        TradeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(competitionId, userId, request);

        // 중복 주문 확인
        TradeResponse? existingTrade =
            await _competitionTradeRepository.FindByOrderIdAsync(
                competitionId,
                userId,
                request.ClientOrderId,
                cancellationToken);

        if (existingTrade != null)
            return existingTrade;
        // 대회 상태 확인
        await ValidateCompetitionStatusAsync(
            competitionId,
            cancellationToken);

        // 종목 조회
        var stock = await _stockRepository.FindByIdAsync(
            request.StockId,
            cancellationToken);

        ValidateStock(stock);

        // 실시간 시세 조회
        var quote = await _quoteService.GetQuoteAsync(
            stock!.StockCode,
            cancellationToken);

        if (quote.Price <= 0)
        {
            throw new TradeException(
                "QUOTE_UNAVAILABLE",
                "현재 주식 가격을 확인할 수 없습니다.");
        }

        // 대회 전용 계좌로 매도
        return await _competitionTradeRepository.SellAsync(
            competitionId,
            userId,
            request,
            quote.Price,
            quote.FetchedAt.UtcDateTime,
            cancellationToken);
    }

    // 요청값 검사
    private static void ValidateRequest(
        int competitionId,
        int userId,
        TradeRequest request)
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

        if (request == null)
        {
            throw new TradeException(
                "INVALID_REQUEST",
                "매매 요청 정보가 없습니다.");
        }

        if (request.StockId <= 0)
        {
            throw new TradeException(
                "INVALID_STOCK",
                "올바르지 않은 종목입니다.");
        }

        if (request.Quantity <= 0)
        {
            throw new TradeException(
                "INVALID_QUANTITY",
                "매매 수량은 1주 이상이어야 합니다.");
        }

        if (request.ClientOrderId == Guid.Empty)
        {
            throw new TradeException(
                "INVALID_ORDER_ID",
                "주문 번호가 올바르지 않습니다.");
        }
    }

    // 종목 검사
    private static void ValidateStock(StockResponse? stock)
    {
        if (stock == null)
        {
            throw new TradeException(
                "STOCK_NOT_FOUND",
                "존재하지 않는 종목입니다.");
        }

        if (!Regex.IsMatch(stock.StockCode, @"^\d{6}$"))
        {
            throw new TradeException(
                "INVALID_STOCK_CODE",
                "올바르지 않은 종목 코드입니다.");
        }

        if (stock.Market != "KOSPI" &&
            stock.Market != "KOSDAQ")
        {
            throw new TradeException(
                "INVALID_MARKET",
                "거래할 수 없는 시장입니다.");
        }
    }
}
