using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Services;
using MySqlConnector;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/rankings")]
[Authorize]
public class RankingsController : ControllerBase
{
    private readonly RankingService _service;
    private readonly ILogger<RankingsController> _logger;

    public RankingsController(
        RankingService service,
        ILogger<RankingsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken)
    {
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
            var result = await _service.GetAsync(
                userId,
                cancellationToken);

            return Ok(result);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "순위 조회 DB 오류: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "순위 데이터 조회에 실패했습니다.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new
            {
                code = "RANKING_UNAVAILABLE",
                message = "시세 응답 지연으로 순위를 계산하지 못했습니다."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidOperationException
            or JsonException)
        {
            _logger.LogWarning(
                "순위 계산 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "RANKING_UNAVAILABLE",
                message = "필요한 시세를 확보하거나 순위를 계산하지 못했습니다."
            });
        }
    }
}