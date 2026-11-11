using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class CompetitionParticipantService
{
    private readonly CompetitionParticipantRepository _repository;

    public CompetitionParticipantService(
        CompetitionParticipantRepository repository)
    {
        _repository = repository;
    }

    // 대회 참가자 등록
    public async Task<CompetitionParticipantResponse> AddParticipantAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
        {
            throw new InvalidOperationException(
                "올바르지 않은 대회 번호입니다.");
        }

        if (userId <= 0)
        {
            throw new InvalidOperationException(
                "올바르지 않은 회원 번호입니다.");
        }

        return await _repository.AddParticipantAsync(
            competitionId,
            userId,
            cancellationToken);
    }
    // 대회 참가자 목록 조회
    public async Task<List<CompetitionParticipantListResponse>> GetParticipantsAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
        {
            throw new InvalidOperationException(
                "올바르지 않은 대회 번호입니다.");
        }

        return await _repository.GetParticipantsAsync(
            competitionId,
            cancellationToken);
    }
    // 대회 참가자 삭제
    public async Task DeleteParticipantAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
        {
            throw new InvalidOperationException(
                "올바르지 않은 대회 번호입니다.");
        }

        if (userId <= 0)
        {
            throw new InvalidOperationException(
                "올바르지 않은 회원 번호입니다.");
        }

        await _repository.DeleteParticipantAsync(
            competitionId,
            userId,
            cancellationToken);
    }
    // 로그인한 사용자의 현재 진행 중인 대회 조회
    public async Task<ActiveCompetitionResponse?> GetMyActiveCompetitionAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException(
                "올바르지 않은 회원 번호입니다.");
        }

        return await _repository.GetMyActiveCompetitionAsync(
            userId,
            cancellationToken);
    }
}