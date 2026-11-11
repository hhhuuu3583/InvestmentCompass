using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Services;
using MockInvestment.Api.Data;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/stock-master")]
[Authorize]
public class StockMasterController : ControllerBase
{
    private readonly StockMasterService _service;
    private readonly ILogger<StockMasterController> _logger;
    private readonly StockRepository _repository;
    private readonly IWebHostEnvironment _environment;

    public StockMasterController(
        StockMasterService service,
        StockRepository repository,
        IWebHostEnvironment environment,
        ILogger<StockMasterController> logger)
    {
        _service = service;
        _repository = repository;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("preview/{market}")]
    public async Task<IActionResult> Preview(
        string market,
        CancellationToken cancellationToken)
    {
        market = market.ToUpperInvariant();

        if (market is not ("KOSPI" or "KOSDAQ"))
        {
            return BadRequest(new
            {
                message = "KOSPI 또는 KOSDAQ을 입력해주세요."
            });
        }

        try
        {
            var stocks = await _service.GetStocksAsync(
                market,
                cancellationToken);

            return Ok(new
            {
                market,
                totalCount = stocks.Count,
                items = stocks.Take(10).ToList()
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new
            {
                message = "종목정보 다운로드 시간이 초과되었습니다."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidOperationException
            or System.IO.IOException
            or System.Text.DecoderFallbackException)
        {
            _logger.LogWarning(
                "종목정보 조회 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                message = "종목정보를 가져오거나 해석하지 못했습니다."
            });
        }
    }

    [HttpPost("import/{market}")]
    public async Task<IActionResult> Import(
    string market,
    CancellationToken cancellationToken)
    {
        // 로컬 개발 환경에서만 사용
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        market = market.ToUpperInvariant();

        if (market is not ("KOSPI" or "KOSDAQ"))
        {
            return BadRequest(new
            {
                message = "KOSPI 또는 KOSDAQ을 입력해주세요."
            });
        }

        try
        {
            // 미리보기 10개가 아닌 전체 목록
            var stocks = await _service.GetStocksAsync(
                market,
                cancellationToken);

            int processedCount =
                await _repository.SaveMasterStocksAsync(
                    stocks,
                    cancellationToken);

            return Ok(new
            {
                market,
                processedCount,
                message = "종목정보 저장이 완료되었습니다."
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new
            {
                message = "종목정보 다운로드 시간이 초과되었습니다."
            });
        }
        catch (MySqlConnector.MySqlException ex)
        {
            _logger.LogError(
                "종목정보 저장 실패. MySQL 오류 번호: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "종목정보 DB 저장에 실패했습니다.");
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidOperationException
            or System.IO.IOException
            or System.Text.DecoderFallbackException)
        {
            _logger.LogWarning(
                "종목정보 수집 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                message = "종목정보를 가져오거나 해석하지 못했습니다."
            });
        }
    }
}