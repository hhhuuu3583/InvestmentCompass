
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;
using MySqlConnector;
using System.Security.Claims;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions/{competitionId:int}/participants")]
[ApiController]
[Authorize]
public class CompetitionParticipantsController : ControllerBase
{
    private readonly CompetitionParticipantService _service;

    public CompetitionParticipantsController(
        CompetitionParticipantService service)
    {
        _service = service;
    }

    // 참가자 목록 조회
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetParticipants(
        int competitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var participants = await _service.GetParticipantsAsync(
                competitionId,
                cancellationToken);

            return Ok(new
            {
                competitionId,
                participantCount = participants.Count,
                participants
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

    
    // 관리자 전용 참가자 삭제
    [HttpDelete("{userId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteParticipant(
        int competitionId,
        int userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteParticipantAsync(
                competitionId,
                userId,
                cancellationToken);

            return Ok(new
            {
                message = "대회 참가자가 삭제되었습니다."
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
    // 로그인한 사용자의 현재 진행 중인 대회 조회
    [HttpGet("~/api/competitions/my/active")]
    public async Task<IActionResult> GetMyActiveCompetition(
        CancellationToken cancellationToken)
    {
        try
        {
            // JWT에서 로그인한 사용자 ID 가져오기
            string? userIdValue =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (!int.TryParse(userIdValue, out int userId))
            {
                return Unauthorized(new
                {
                    message = "로그인 사용자 정보를 확인할 수 없습니다."
                });
            }

            var competition =
                await _service.GetMyActiveCompetitionAsync(
                    userId,
                    cancellationToken);

            if (competition == null)
            {
                return Ok(new
                {
                    isParticipating = false,
                    message = "현재 진행 중인 대회가 없습니다.",
                    competition = (ActiveCompetitionResponse?)null
                });
            }

            return Ok(new
            {
                isParticipating = true,
                message = "현재 진행 중인 대회가 있습니다.",
                competition
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
