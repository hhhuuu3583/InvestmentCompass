using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Data;
using MockInvestment.Api.Services;
using MySqlConnector;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/stocks")]
[Authorize]
public class StocksController : ControllerBase
{
    private readonly StockRepository _repository;
    private readonly QuoteCacheService _quoteService;
    private readonly ILogger<StocksController> _logger;
    private readonly KisChartService _chartService;
    private readonly KisMinuteChartService _minuteChartService;

    public StocksController(
     StockRepository repository,
     QuoteCacheService quoteService,
     KisChartService chartService,
     KisMinuteChartService minuteChartService,
     ILogger<StocksController> logger)
    {
        _repository = repository;
        _quoteService = quoteService;
        _chartService = chartService;
        _minuteChartService = minuteChartService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetStocks(
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        var stocks = await _repository.GetStocksAsync(
            keyword,
            cancellationToken);

        return Ok(new
        {
            items = stocks
        });
    }

    [HttpGet("{stockId:int}/quote")]
    public async Task<IActionResult> GetQuote(
        int stockId,
        CancellationToken cancellationToken)
    {
        if (stockId <= 0)
        {
            return BadRequest(new
            {
                code = "INVALID_STOCK_ID",
                message = "종목번호는 1 이상이어야 합니다."
            });
        }

        try
        {
            var stock = await _repository.FindByIdAsync(
                stockId,
                cancellationToken);

            if (stock is null)
            {
                return NotFound(new
                {
                    code = "STOCK_NOT_FOUND",
                    message = "거래 지원 종목을 찾을 수 없습니다."
                });
            }

            // 기존 가상 종목은 실제 시세 조회 대상에서 제외
            if (stock.Market is not ("KOSPI" or "KOSDAQ")
                || !Regex.IsMatch(
                    stock.StockCode, @"^[0-9]{6}$"))
            {
                return BadRequest(new
                {
                    code = "UNSUPPORTED_STOCK",
                    message = "실제 시세 조회를 지원하지 않는 종목입니다."
                });
            }

            var quote = await _quoteService.GetQuoteAsync(
                stock.StockCode,
                cancellationToken);

            return Ok(new
            {
                stockId = stock.StockId,
                stockCode = stock.StockCode,
                stockName = stock.StockName,
                market = stock.Market,
                currentPrice = quote.Price,
                fetchedAt = quote.FetchedAt,
                isCached = quote.IsCached
            });
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "종목 조회 DB 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "종목정보 조회에 실패했습니다.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new
            {
                code = "QUOTE_TIMEOUT",
                message = "시세 응답 시간이 초과되었습니다."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidOperationException
            or JsonException)
        {
            _logger.LogWarning(
                "현재가 조회 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "QUOTE_UNAVAILABLE",
                message = "현재 시세를 가져올 수 없습니다."
            });
        }
    }

    [HttpGet("{stockId:int}/candles")]
    public async Task<IActionResult> GetCandles(
    int stockId,
    CancellationToken cancellationToken,
    [FromQuery] string range = "3M")
    {
        range = range.Trim().ToUpperInvariant();

        if (range is not("1D" or "1W" or "1M" or "3M" or "1Y" or "5Y"))
        {
            return BadRequest(new
            {
                code = "INVALID_CHART_RANGE",
                message = "지원하지 않는 조회 기간입니다."
            });
        }

        if (stockId <= 0)
        {
            return BadRequest(new
            {
                code = "INVALID_STOCK_ID",
                message = "종목번호는 1 이상이어야 합니다."
            });
        }

        try
        {
            // DB 종목번호로 실제 종목코드 확인
            var stock = await _repository.FindByIdAsync(
                stockId,
                cancellationToken);

            if (stock is null)
            {
                return NotFound(new
                {
                    code = "STOCK_NOT_FOUND",
                    message = "거래 지원 종목을 찾을 수 없습니다."
                });
            }

            if (stock.Market is not ("KOSPI" or "KOSDAQ")
                || !Regex.IsMatch(
                    stock.StockCode, @"^[0-9]{6}$"))
            {
                return BadRequest(new
                {
                    code = "UNSUPPORTED_STOCK",
                    message = "차트 조회를 지원하지 않는 종목입니다."
                });
            }

            var candles = range switch
            {
                "1D" => await _minuteChartService.GetTodayAsync(
                    stock.StockCode,
                    cancellationToken),

                "1W" => await _minuteChartService.GetWeekAsync(
                    stock.StockCode,
                    cancellationToken),

                _ => await _chartService.GetDailyAsync(
                    stock.StockCode,
                    cancellationToken,
                    range)
            };

            return Ok(new
            {
                stockId = stock.StockId,
                stockCode = stock.StockCode,
                stockName = stock.StockName,
                range = range,
                period = range switch
                {
                    "1D" => "5m",
                    "1W" => "1h",
                    "1Y" => "W",
                    "5Y" => "M",
                    _ => "D"
                },

                priceType = range is "1D" or "1W" ? "RAW" : "ADJUSTED",
                fetchedAt = DateTimeOffset.UtcNow,
                items = candles
            });
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "차트 종목 조회 DB 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "종목정보 조회에 실패했습니다.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new
            {
                code = "CHART_TIMEOUT",
                message = "차트 시세 응답 시간이 초과되었습니다."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidOperationException
            or JsonException)
        {
            _logger.LogWarning(
                "차트 조회 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "CHART_UNAVAILABLE",
                message = "차트 데이터를 가져오지 못했습니다."
            });
        }
    }
}