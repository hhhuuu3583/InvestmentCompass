using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/market")]
[Authorize]
public class MarketController : ControllerBase
{
    private readonly QuoteCacheService _quoteService;
    private readonly ILogger<MarketController> _logger;

    public MarketController(
    QuoteCacheService quoteService,
    ILogger<MarketController> logger)
    {
        _quoteService = quoteService;
        _logger = logger;
    }

    [HttpGet("quotes/{stockCode}")]
    public async Task<IActionResult> GetQuote(
        string stockCode,
        CancellationToken cancellationToken)
    {
        // 현재 테스트는 숫자 6자리 국내 주식코드로 제한
        if (!Regex.IsMatch(stockCode, @"^[0-9]{6}$"))
        {
            return BadRequest(new
            {
                code = "INVALID_STOCK_CODE",
                message = "종목코드는 숫자 6자리로 입력해주세요."
            });
        }

        try
        {
            var quote = await _quoteService.GetQuoteAsync(
                stockCode,
                cancellationToken);

            return Ok(new
            {
                stockCode,
                currentPrice = quote.Price,
                fetchedAt = quote.FetchedAt,
                isCached = quote.IsCached
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new
            {
                code = "QUOTE_TIMEOUT",
                message = "외부 시세 응답 시간이 초과되었습니다."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidOperationException
            or JsonException)
        {
            _logger.LogWarning(
                "외부 시세 조회 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "QUOTE_UNAVAILABLE",
                message = "현재 시세를 가져올 수 없습니다."
            });
        }
    }
}