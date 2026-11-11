
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;
using MySqlConnector;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
[Authorize]
public class PortfolioController : ControllerBase
{
    private readonly PortfolioService _service;
    private readonly CompetitionParticipantService
        _competitionParticipantService;

    private readonly ILogger<PortfolioController> _logger;

    public PortfolioController(
        PortfolioService service,
        CompetitionParticipantService competitionParticipantService,
        ILogger<PortfolioController> logger)
    {
        _service = service;
        _competitionParticipantService =
            competitionParticipantService;

        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken)
    {
        string? subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

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
            // 현재 진행 중인 대회 확인
            var activeCompetition =
                await _competitionParticipantService
                    .GetMyActiveCompetitionAsync(
                        userId,
                        cancellationToken);

            PortfolioResponse? result;

            if (activeCompetition is not null)
            {
                // 대회 계좌 조회
                result = await _service.GetCompetitionAsync(
                    userId,
                    activeCompetition.CompetitionId,
                    cancellationToken);
            }
            else
            {
                // 일반 계좌 조회
                result = await _service.GetAsync(
                    userId,
                    cancellationToken);
            }

            if (result is null)
            {
                return NotFound(new
                {
                    code = activeCompetition is not null
                        ? "COMPETITION_ACCOUNT_NOT_FOUND"
                        : "ACCOUNT_NOT_FOUND",

                    message = activeCompetition is not null
                        ? "대회 계좌가 없습니다."
                        : "가상 계좌가 없습니다."
                });
            }

            return Ok(result);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "자산 조회 DB 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "자산정보 조회에 실패했습니다.");
        }
    }
}
