
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class CompetitionTradeHistoryService
{
    private readonly CompetitionTradeHistoryRepository _repository;

    public CompetitionTradeHistoryService(
        CompetitionTradeHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CompetitionTradeHistoryResponse>>
        GetMyTradesAsync(
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

        return await _repository.GetMyTradesAsync(
            competitionId,
            userId,
            cancellationToken);
    }

    public async Task<(long TotalCount, List<TradeHistoryItem> Items)>
        GetMyTradesPageAsync(
            int competitionId,
            int userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        if (competitionId <= 0 || userId <= 0)
            throw new InvalidOperationException(
                "대회 또는 사용자 정보가 올바르지 않습니다.");

        if (page < 1 || pageSize < 1 || pageSize > 100)
            throw new InvalidOperationException(
                "페이지 또는 페이지 크기가 올바르지 않습니다.");

        return await _repository.GetMyTradesPageAsync(
            competitionId,
            userId,
            page,
            pageSize,
            cancellationToken);
    }
}
