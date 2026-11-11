
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class CompetitionRankingService
{
    private readonly CompetitionParticipantRepository _participantRepository;
    private readonly CompetitionAccountService _accountService;

    public CompetitionRankingService(
        CompetitionParticipantRepository participantRepository,
        CompetitionAccountService accountService)
    {
        _participantRepository = participantRepository;
        _accountService = accountService;
    }

    public async Task<List<CompetitionRankingResponse>> GetRankingsAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
            throw new InvalidOperationException("올바르지 않은 대회 번호입니다.");

        var participants =
            await _participantRepository.GetParticipantsAsync(
                competitionId,
                cancellationToken);

        var rankings = new List<CompetitionRankingResponse>();

        foreach (var participant in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var account = await _accountService.GetAccountAsync(
                competitionId,
                participant.UserId,
                cancellationToken);

            if (account is null)
                continue;

            rankings.Add(new CompetitionRankingResponse
            {
                ParticipantId = participant.ParticipantId,
                UserId = participant.UserId,
                LoginId = participant.LoginId,
                Nickname = participant.Nickname,
                InitialBalance = account.InitialBalance,
                CashBalance = account.CashBalance,
                StockEvaluation = account.StockEvaluation,
                TotalAsset = account.TotalAsset,
                TotalProfit = account.TotalProfit,
                ProfitRate = account.ProfitRate
            });
        }

        // 수익률이 높은 순서로 정렬한다.
        // 수익률이 같으면 총자산이 많은 순서로 정렬한다.
        rankings = rankings
            .OrderByDescending(x => x.ProfitRate)
            .ThenByDescending(x => x.TotalAsset)
            .ThenBy(x => x.ParticipantId)
            .ToList();

        // 동일한 수익률은 공동 순위로 처리한다.
        // 예: 1위, 1위, 3위
        for (int i = 0; i < rankings.Count; i++)
        {
            if (i == 0)
            {
                rankings[i].Rank = 1;
            }
            else if (rankings[i].ProfitRate ==
                     rankings[i - 1].ProfitRate)
            {
                rankings[i].Rank = rankings[i - 1].Rank;
            }
            else
            {
                rankings[i].Rank = i + 1;
            }
        }

        return rankings;
    }
}
