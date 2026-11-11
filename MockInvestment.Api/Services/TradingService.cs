
using System.Text.RegularExpressions;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class TradingService
{
    private readonly TradeRepository _tradeRepository;
    private readonly StockRepository _stockRepository;
    private readonly QuoteCacheService _quoteService;
    private readonly CompetitionParticipantService _competitionParticipantService;

    public TradingService(
        TradeRepository tradeRepository,
        StockRepository stockRepository,
        QuoteCacheService quoteService,
        CompetitionParticipantService competitionParticipantService)
    {
        _tradeRepository = tradeRepository;
        _stockRepository = stockRepository;
        _quoteService = quoteService;
        _competitionParticipantService = competitionParticipantService;
    }

    // 진행 중인 대회 참가자는 일반 계좌 거래 차단
    private async Task ValidateGeneralAccountAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var activeCompetition =
            await _competitionParticipantService.GetMyActiveCompetitionAsync(
                userId,
                cancellationToken);

        if (activeCompetition != null)
        {
            throw new TradeException(
                "ACTIVE_COMPETITION_USE_COMPETITION_ACCOUNT",
                "현재 대회에 참가 중입니다. 대회 계좌를 이용해주세요.");
        }
    }

    public async Task<TradeResponse> BuyAsync(
        int userId,
        TradeRequest request,
        CancellationToken cancellationToken)
    {
        // 1. 이미 체결된 주문이면 기존 결과 반환
        var existing =
            await _tradeRepository.FindByOrderIdAsync(
                userId,
                request.ClientOrderId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.TradeType != "BUY"
                || existing.StockId != request.StockId
                || existing.Quantity != request.Quantity)
            {
                throw new TradeException(
                    "ORDER_ID_CONFLICT",
                    "같은 주문번호에 다른 주문이 등록되어 있습니다.");
            }

            return existing;
        }

        // 2. 진행 중인 대회 참가 여부 확인
        await ValidateGeneralAccountAsync(
            userId,
            cancellationToken);

        // 3. DB의 거래 지원 종목 확인
        var stock = await _stockRepository.FindByIdAsync(
            request.StockId,
            cancellationToken);

        if (stock is null)
        {
            throw new TradeException(
                "STOCK_NOT_FOUND",
                "거래 지원 종목을 찾을 수 없습니다.");
        }

        if (stock.Market is not ("KOSPI" or "KOSDAQ")
            || !Regex.IsMatch(
                stock.StockCode, @"^[0-9]{6}$"))
        {
            throw new TradeException(
                "UNSUPPORTED_STOCK",
                "실제 시세 조회를 지원하지 않는 종목입니다.");
        }

        // 4. 클라이언트 가격이 아닌 서버 시세 사용
        var quote = await _quoteService.GetQuoteAsync(
            stock.StockCode,
            cancellationToken);

        // 5. 일반 계좌 거래 실행
        return await _tradeRepository.BuyAsync(
            userId,
            request,
            quote.Price,
            quote.FetchedAt.UtcDateTime,
            cancellationToken);
    }

    public async Task<TradeResponse> SellAsync(
        int userId,
        TradeRequest request,
        CancellationToken cancellationToken)
    {
        // 1. 이미 체결된 주문인지 먼저 확인
        var existing =
            await _tradeRepository.FindByOrderIdAsync(
                userId,
                request.ClientOrderId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.TradeType != "SELL"
                || existing.StockId != request.StockId
                || existing.Quantity != request.Quantity)
            {
                throw new TradeException(
                    "ORDER_ID_CONFLICT",
                    "같은 주문번호에 다른 주문이 등록되어 있습니다.");
            }

            return existing;
        }

        // 2. 진행 중인 대회 참가 여부 확인
        await ValidateGeneralAccountAsync(
            userId,
            cancellationToken);

        // 3. 거래 지원 종목 확인
        var stock = await _stockRepository.FindByIdAsync(
            request.StockId,
            cancellationToken);

        if (stock is null)
        {
            throw new TradeException(
                "STOCK_NOT_FOUND",
                "거래 지원 종목을 찾을 수 없습니다.");
        }

        if (stock.Market is not ("KOSPI" or "KOSDAQ")
            || !Regex.IsMatch(
                stock.StockCode, @"^[0-9]{6}$"))
        {
            throw new TradeException(
                "UNSUPPORTED_STOCK",
                "실제 시세 조회를 지원하지 않는 종목입니다.");
        }

        // 4. 서버 시세 조회
        var quote = await _quoteService.GetQuoteAsync(
            stock.StockCode,
            cancellationToken);

        // 5. 일반 계좌 거래 실행
        return await _tradeRepository.SellAsync(
            userId,
            request,
            quote.Price,
            quote.FetchedAt.UtcDateTime,
            cancellationToken);
    }
}
