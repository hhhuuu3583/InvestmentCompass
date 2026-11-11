using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class CompetitionApplicationService
{
    private readonly CompetitionApplicationRepository _repository;

    public CompetitionApplicationService(
        CompetitionApplicationRepository repository)
    {
        _repository = repository;
    }

    // 참가 신청
    public async Task<int> AddApplicationAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken)
    {
        if (competitionId <= 0)
            throw new InvalidOperationException(
                "올바르지 않은 대회 번호입니다.");

        if (userId <= 0)
            throw new InvalidOperationException(
                "올바르지 않은 사용자 번호입니다.");

        return await _repository.AddApplicationAsync(
            competitionId,
            userId,
            cancellationToken);
    }

    // 참가 신청 목록 조회
    public async Task<List<CompetitionApplication>>
        GetApplicationsAsync(
            int competitionId,
            CancellationToken cancellationToken)
    {
        if (competitionId <= 0)
            throw new InvalidOperationException(
                "올바르지 않은 대회 번호입니다.");

        return await _repository.GetApplicationsAsync(
            competitionId,
            cancellationToken);
    }

    // 참가 신청 승인 또는 거절
    public async Task ProcessApplicationAsync(
    int competitionId,
    int applicationId,
    bool approve,
    CancellationToken cancellationToken)
    {
        if (competitionId <= 0)
            throw new InvalidOperationException(
                "올바르지 않은 대회 번호입니다.");

        if (applicationId <= 0)
            throw new InvalidOperationException(
                "올바르지 않은 신청 번호입니다.");

        await _repository.ProcessApplicationAsync(
            competitionId,
            applicationId,
            approve,
            cancellationToken);
    }
    public async Task<CompetitionApplication?> GetMyApplicationAsync(
    int competitionId, int userId,
    CancellationToken cancellationToken)
    {
        if (competitionId <= 0)
            throw new InvalidOperationException("올바르지 않은 대회 번호입니다.");

        if (userId <= 0)
            throw new InvalidOperationException("올바르지 않은 사용자 번호입니다.");

        return await _repository.GetMyApplicationAsync(
            competitionId, userId, cancellationToken);
    }
}