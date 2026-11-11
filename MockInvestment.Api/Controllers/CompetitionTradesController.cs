
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions/{competitionId:int}/trades")]
[ApiController]
[Authorize]
public class CompetitionTradesController : ControllerBase
{
    private readonly CompetitionTradingService _tradingService;

    public CompetitionTradesController(
        CompetitionTradingService tradingService)
    {
        _tradingService = tradingService;
    }

    // 대회 매수
    [HttpPost("buy")]
    public async Task<IActionResult> Buy(
        int competitionId,
        [FromBody] TradeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            int userId = GetUserId();

            TradeResponse result =
                await _tradingService.BuyAsync(
                    competitionId,
                    userId,
                    request,
                    cancellationToken);

            return Ok(result);
        }
        catch (TradeException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new
            {
                message = "로그인 사용자 정보를 확인할 수 없습니다."
            });
        }
    }

    // 대회 매도
    [HttpPost("sell")]
    public async Task<IActionResult> Sell(
        int competitionId,
        [FromBody] TradeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            int userId = GetUserId();

            TradeResponse result =
                await _tradingService.SellAsync(
                    competitionId,
                    userId,
                    request,
                    cancellationToken);

            return Ok(result);
        }
        catch (TradeException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new
            {
                message = "로그인 사용자 정보를 확인할 수 없습니다."
            });
        }
    }

    // JWT에서 사용자 ID 가져오기
    private int GetUserId()
    {
        string? userIdValue =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!int.TryParse(userIdValue, out int userId))
        {
            throw new UnauthorizedAccessException();
        }

        return userId;
    }
}
