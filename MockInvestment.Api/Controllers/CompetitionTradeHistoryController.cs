
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions/{competitionId:int}/trades")]
[ApiController]
[Authorize]
public class CompetitionTradeHistoryController : ControllerBase
{
    private readonly CompetitionTradeHistoryService _service;

    public CompetitionTradeHistoryController(
        CompetitionTradeHistoryService service)
    {
        _service = service;
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyTrades(
        int competitionId,
        CancellationToken cancellationToken)
    {
        string? userIdValue =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!int.TryParse(userIdValue, out int userId) ||
            userId <= 0)
        {
            return Unauthorized(new
            {
                message = "사용자 인증 정보를 확인할 수 없습니다."
            });
        }

        try
        {
            var trades = await _service.GetMyTradesAsync(
                competitionId,
                userId,
                cancellationToken);

            return Ok(new
            {
                competitionId,
                tradeCount = trades.Count,
                trades
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }
}
