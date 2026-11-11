
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;
using MySqlConnector;
using MockInvestment.Api.Data;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/trades")]
[Authorize]
public class TradesController : ControllerBase
{
    private readonly TradingService _tradingService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<TradesController> _logger;
    private readonly TradeRepository _repository;
    private readonly CompetitionParticipantService _competitionParticipantService;
    private readonly CompetitionTradeHistoryService _competitionTradeHistoryService;

    public TradesController(
        TradingService tradingService,
        TradeRepository repository,
        CompetitionParticipantService competitionParticipantService,
        CompetitionTradeHistoryService competitionTradeHistoryService,
        IWebHostEnvironment environment,
        ILogger<TradesController> logger)
    {
        _tradingService = tradingService;
        _repository = repository;
        _competitionParticipantService = competitionParticipantService;
        _competitionTradeHistoryService = competitionTradeHistoryService;
        _environment = environment;
        _logger = logger;
    }

    // 일반 계좌 매수
    [HttpPost("buy")]
    public async Task<IActionResult> Buy(
        [FromBody] TradeRequest request,
        CancellationToken cancellationToken)
    {
        // 일반 매매는 개발 환경에서만 허용
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        string? subject =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(
                subject,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            || userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var result = await _tradingService.BuyAsync(
                userId,
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (TradeException ex)
        {
            var error = new
            {
                code = ex.Code,
                message = ex.Message
            };

            return ex.Code switch
            {
                "ORDER_ID_CONFLICT" => Conflict(error),

                "STOCK_NOT_FOUND" or "ACCOUNT_NOT_FOUND"
                    => NotFound(error),

                _ => BadRequest(error)
            };
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "매수 DB 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "매수 처리 중 DB 오류가 발생했습니다.");
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
                "매수 처리 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "TRADE_UNAVAILABLE",
                message = "현재 매수 요청을 처리할 수 없습니다."
            });
        }
    }

    // 일반 계좌 매도
    [HttpPost("sell")]
    public async Task<IActionResult> Sell(
        [FromBody] TradeRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        string? subject =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(
                subject,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            || userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var result = await _tradingService.SellAsync(
                userId,
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (TradeException ex)
        {
            var error = new
            {
                code = ex.Code,
                message = ex.Message
            };

            return ex.Code switch
            {
                "ORDER_ID_CONFLICT" => Conflict(error),

                "STOCK_NOT_FOUND" or "ACCOUNT_NOT_FOUND"
                    => NotFound(error),

                _ => BadRequest(error)
            };
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "매도 DB 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "매도 처리 중 DB 오류가 발생했습니다.");
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
                "매도 처리 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "TRADE_UNAVAILABLE",
                message = "현재 매도 요청을 처리할 수 없습니다."
            });
        }
    }

    // 거래 내역 조회
    [HttpGet]
    public async Task<IActionResult> GetHistory(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGING",
                message = "page는 1 이상, pageSize는 1~100이어야 합니다."
            });
        }

        string? subject =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(
                subject,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            || userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            // 현재 진행 중이거나 정산 중인 대회 참가자는 대회 거래 내역 조회
            var activeCompetition =
                await _competitionParticipantService
                    .GetMyActiveCompetitionAsync(
                        userId,
                        cancellationToken);

            if (activeCompetition is not null)
            {
                var competitionResult =
                    await _competitionTradeHistoryService
                        .GetMyTradesPageAsync(
                            activeCompetition.CompetitionId,
                            userId,
                            page,
                            pageSize,
                            cancellationToken);

                return Ok(new
                {
                    page,
                    pageSize,
                    totalCount = competitionResult.TotalCount,
                    items = competitionResult.Items,
                    accountType = "COMPETITION",
                    competitionId = activeCompetition.CompetitionId
                });
            }

            // 대회 참가 중이 아니면 일반 계좌 거래 내역 조회
            var result = await _repository.GetHistoryAsync(
                userId,
                page,
                pageSize,
                cancellationToken);

            return Ok(new
            {
                page,
                pageSize,
                totalCount = result.TotalCount,
                items = result.Items,
                accountType = "GENERAL",
                competitionId = (int?)null
            });
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "거래 내역 조회 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "거래 내역 조회에 실패했습니다.");
        }
    }
}
