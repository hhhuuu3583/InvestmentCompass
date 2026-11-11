
using MockInvestment.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Controllers;

[Route("api/competitions")]
[ApiController]
[Authorize]
public class CompetitionsController : ControllerBase
{
    private readonly CompetitionRepository _repository;
    private readonly CompetitionRankingService _rankingService;
    private readonly CompetitionFinalRankingRepository _finalRankingRepository;

    public CompetitionsController(
        CompetitionRepository repository,
        CompetitionRankingService rankingService,
        CompetitionFinalRankingRepository finalRankingRepository)
    {
        _repository = repository;
        _rankingService = rankingService;
        _finalRankingRepository = finalRankingRepository;
    }

    // 대회 생성
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new
            {
                message = "대회 이름을 입력해 주세요."
            });
        }

        if (request.Title.Length > 100)
        {
            return BadRequest(new
            {
                message = "대회 이름은 100자 이내로 입력해 주세요."
            });
        }

        if (request.StartDate >= request.EndDate)
        {
            return BadRequest(new
            {
                message = "대회 종료일은 시작일보다 늦어야 합니다."
            });
        }

        if (request.InitialBalance <= 0 ||
            request.InitialBalance > 99999999999999999.99m)
        {
            return BadRequest(new
            {
                message = "초기 자금이 올바르지 않습니다."
            });
        }

        int competitionId =
            await _repository.CreateAsync(
                request,
                cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = competitionId },
            new
            {
                competitionId,
                message = "대회가 생성되었습니다."
            });
    }

    // 전체 대회 목록 조회
    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        List<Competition> competitions =
            await _repository.GetAllAsync(cancellationToken);

        return Ok(competitions);
    }

    // 대회 상세 조회
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "올바르지 않은 대회 번호입니다."
            });
        }

        Competition? competition =
            await _repository.GetByIdAsync(
                id,
                cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        return Ok(competition);
    }

    // 대회 정보 수정
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] CreateCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "올바르지 않은 대회 번호입니다."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new
            {
                message = "대회 이름을 입력해 주세요."
            });
        }

        if (request.Title.Length > 100)
        {
            return BadRequest(new
            {
                message = "대회 이름은 100자 이내로 입력해 주세요."
            });
        }

        if (request.StartDate >= request.EndDate)
        {
            return BadRequest(new
            {
                message = "대회 종료일은 시작일보다 늦어야 합니다."
            });
        }

        if (request.InitialBalance <= 0 ||
            request.InitialBalance > 99999999999999999.99m)
        {
            return BadRequest(new
            {
                message = "초기 자금이 올바르지 않습니다."
            });
        }

        var competition = await _repository.GetByIdAsync(
            id, cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        if (competition.Status != "준비")
        {
            return BadRequest(new
            {
                message = "준비 상태의 대회만 수정할 수 있습니다."
            });
        }

        bool result = await _repository.UpdateAsync(
            id, request, cancellationToken);

        if (!result)
        {
            return BadRequest(new
            {
                message = "대회 정보를 수정하지 못했습니다."
            });
        }

        return Ok(new
        {
            message = "대회 정보가 수정되었습니다.",
            competitionId = id
        });
    }

    // 대회 시작
    [HttpPost("{id:int}/start")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> StartCompetition(
        int id,
        CancellationToken cancellationToken)
    {
        var competition = await _repository.GetByIdAsync(
            id, cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        if (competition.Status != "준비")
        {
            return BadRequest(new
            {
                message = "준비 상태의 대회만 시작할 수 있습니다."
            });
        }

        await _repository.UpdateStatusAsync(
            id, "진행중", cancellationToken);

        return Ok(new
        {
            message = "대회가 시작되었습니다.",
            competitionId = id,
            status = "진행중"
        });
    }

    // 대회 종료 및 최종 순위 확정
    [HttpPost("{id:int}/end")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EndCompetition(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "올바르지 않은 대회 번호입니다."
            });
        }

        var competition = await _repository.GetByIdAsync(
            id, cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        if (competition.Status != "진행중")
        {
            return BadRequest(new
            {
                message = "진행 중인 대회만 종료할 수 있습니다."
            });
        }

        // 대회 상태를 정산중으로 변경한다.
        // 정산이 시작되면 신규 거래를 차단한다.
        bool settlementStarted =
            await _repository.TryBeginSettlementAsync(
                id, cancellationToken);

        if (!settlementStarted)
        {
            return Conflict(new
            {
                message = "대회 상태가 변경되었거나 이미 정산 중입니다."
            });
        }

        try
        {
            // 거래가 차단된 상태에서 참가자별 최종 자산과 수익률 계산
            var rankings = await _rankingService.GetRankingsAsync(
                id, cancellationToken);

            // 최종 순위 저장과 대회 종료 상태 변경을 DB 트랜잭션으로 처리
            await _finalRankingRepository
                .SaveFinalRankingsAndCompleteAsync(
                    id, rankings, cancellationToken);

            return Ok(new
            {
                message = "대회가 종료되고 최종 순위가 확정되었습니다.",
                competitionId = id,
                status = "종료",
                participantCount = rankings.Count,
                rankings
            });
        }
        catch
        {
            // 정산 실패 시 대회를 다시 진행중 상태로 복구
            try
            {
                await _repository.ResumeAfterSettlementFailureAsync(id);
            }
            catch
            {
                // 복구 과정에서 발생한 예외는 원래 예외를 가리지 않도록 한다.
            }

            throw;
        }
    }

    // 대회 삭제
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "올바르지 않은 대회 번호입니다."
            });
        }

        var competition = await _repository.GetByIdAsync(
            id, cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        if (competition.Status != "준비")
        {
            return BadRequest(new
            {
                message = "준비 상태의 대회만 삭제할 수 있습니다."
            });
        }

        bool result = await _repository.DeleteAsync(
            id, cancellationToken);

        if (!result)
        {
            return BadRequest(new
            {
                message = "참가자가 있는 대회는 삭제할 수 없습니다."
            });
        }

        return Ok(new
        {
            message = "대회가 삭제되었습니다.",
            competitionId = id
        });
    }

    // 대회 취소
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CancelCompetition(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "올바르지 않은 대회 번호입니다."
            });
        }

        var competition = await _repository.GetByIdAsync(
            id, cancellationToken);

        if (competition == null)
        {
            return NotFound(new
            {
                message = "대회를 찾을 수 없습니다."
            });
        }

        if (competition.Status != "준비" &&
            competition.Status != "진행중")
        {
            return BadRequest(new
            {
                message = "준비 또는 진행 중인 대회만 취소할 수 있습니다."
            });
        }

        bool result = await _repository.CancelAsync(
            id, cancellationToken);

        if (!result)
        {
            return BadRequest(new
            {
                message = "대회 취소에 실패했습니다."
            });
        }

        return Ok(new
        {
            message = "대회가 취소되었습니다.",
            competitionId = id,
            status = "취소"
        });
    }
}
