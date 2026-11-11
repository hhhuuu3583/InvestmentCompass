using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions/{competitionId:int}/applications")]
[ApiController]
[Authorize]
public class CompetitionApplicationsController : ControllerBase
{
    private readonly CompetitionApplicationService _service;

    public CompetitionApplicationsController(
        CompetitionApplicationService service)
    {
        _service = service;
    }

    // 일반 사용자 참가 신청
    [HttpPost]
    public async Task<IActionResult> Apply(
        int competitionId,
        CancellationToken cancellationToken)
    {
        string? userIdValue =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!int.TryParse(userIdValue, out int userId) || userId <= 0)
        {
            return Unauthorized(new
            {
                message = "사용자 인증 정보를 확인할 수 없습니다."
            });
        }

        try
        {
            int applicationId = await _service.AddApplicationAsync(
                competitionId,
                userId,
                cancellationToken);

            return Ok(new
            {
                message = "대회 참가 신청이 완료되었습니다. 관리자 승인을 기다려 주세요.",
                applicationId,
                status = "대기"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (MySqlConnector.MySqlException ex)
            when (ex.Number == 1062)
        {
            return Conflict(new
            {
                message = "이미 해당 대회에 신청한 이력이 있습니다."
            });
        }
    }

    // 관리자 전용 신청자 목록 조회
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetApplications(
        int competitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var applications = await _service.GetApplicationsAsync(
                competitionId,
                cancellationToken);

            return Ok(new
            {
                competitionId,
                applicationCount = applications.Count,
                applications
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

    [HttpPost("{applicationId:int}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(
    int competitionId,
    int applicationId,
    CancellationToken cancellationToken)
    {
        try
        {
            await _service.ProcessApplicationAsync(
                competitionId,
                applicationId,
                true,
                cancellationToken);

            return Ok(new
            {
                message = "참가 신청이 승인되었습니다."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // 관리자 전용 거절
    [HttpPost("{applicationId:int}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(
    int competitionId,
    int applicationId,
    CancellationToken cancellationToken)
    {
        try
        {
            await _service.ProcessApplicationAsync(
                competitionId,
                applicationId,
                false,
                cancellationToken);

            return Ok(new
            {
                message = "참가 신청이 거절되었습니다."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    [HttpGet("my")]
    public async Task<IActionResult> GetMyApplication(
    int competitionId,
    CancellationToken cancellationToken)
    {
        string? userIdValue =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!int.TryParse(userIdValue, out int userId) || userId <= 0)
            return Unauthorized(new
            {
                message = "사용자 인증 정보를 확인할 수 없습니다."
            });

        try
        {
            var application = await _service.GetMyApplicationAsync(
                competitionId, userId, cancellationToken);

            if (application == null)
                return NotFound(new
                {
                    message = "해당 대회에 참가 신청한 내역이 없습니다."
                });

            return Ok(application);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}