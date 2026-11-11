
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions/{competitionId:int}/account")]
[ApiController]
[Authorize]
public class CompetitionAccountsController : ControllerBase
{
    private readonly CompetitionAccountService _accountService;

    public CompetitionAccountsController(
        CompetitionAccountService accountService)
    {
        _accountService = accountService;
    }

    // 대회 계좌 조회
    [HttpGet]
    public async Task<IActionResult> GetAccount(
        int competitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            int userId = GetUserId();

            CompetitionAccountResponse? account =
                await _accountService.GetAccountAsync(
                    competitionId,
                    userId,
                    cancellationToken);

            if (account == null)
            {
                return NotFound(new
                {
                    message = "해당 대회에 참가하지 않았거나 대회 계좌가 없습니다."
                });
            }

            return Ok(account);
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

    // JWT에서 로그인 사용자 ID 가져오기
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
