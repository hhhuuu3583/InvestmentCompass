using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Data;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions/{competitionId:int}/final-rankings")]
[ApiController]
[Authorize]
public class CompetitionFinalRankingsController : ControllerBase
{
    private readonly CompetitionRepository _competitionRepository;
    private readonly CompetitionFinalRankingRepository _rankingRepository;

    public CompetitionFinalRankingsController(
        CompetitionRepository competitionRepository,
        CompetitionFinalRankingRepository rankingRepository)
    {
        _competitionRepository = competitionRepository;
        _rankingRepository = rankingRepository;
    }

    // 대회 최종 순위 조회
    [HttpGet]
    public async Task<IActionResult> GetFinalRankings(
        int competitionId,
        CancellationToken cancellationToken)
    {
        if (competitionId <= 0)
        {
            return BadRequest(new
            {
                message = "올바르지 않은 대회 번호입니다."
            });
        }

        var competition = await _competitionRepository.GetByIdAsync(
            competitionId, cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        if (competition.Status != "종료")
        {
            return BadRequest(new
            {
                message = "종료된 대회만 최종 순위를 조회할 수 있습니다."
            });
        }

        var rankings = await _rankingRepository.GetFinalRankingsAsync(
            competitionId, cancellationToken);

        return Ok(new
        {
            competitionId,
            competitionTitle = competition.Title,
            finalized = true,
            participantCount = rankings.Count,
            rankings
        });
    }
}