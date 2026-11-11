using MockInvestment.Api.Models;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class CompetitionFinalRankingRepository
{
    private readonly string _connectionString;

    public CompetitionFinalRankingRepository(
        IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    public async Task SaveFinalRankingsAndCompleteAsync(
        int competitionId,
        List<CompetitionRankingResponse> rankings,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(competitionId));

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string deleteSql = """
                DELETE FROM competition_final_rankings
                WHERE competition_id = @competitionId;
                """;

            await using (var deleteCommand =
                new MySqlCommand(deleteSql, connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                await deleteCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            const string insertSql = """
                INSERT INTO competition_final_rankings
                (
                    competition_id,
                    participant_id,
                    user_id,
                    nickname,
                    initial_balance,
                    cash_balance,
                    stock_evaluation,
                    total_asset,
                    profit_amount,
                    profit_rate,
                    ranking
                )
                VALUES
                (
                    @competitionId,
                    @participantId,
                    @userId,
                    @nickname,
                    @initialBalance,
                    @cashBalance,
                    @stockEvaluation,
                    @totalAsset,
                    @profitAmount,
                    @profitRate,
                    @ranking
                );
                """;

            foreach (var ranking in rankings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var command =
                    new MySqlCommand(insertSql, connection, transaction);

                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);
                command.Parameters.AddWithValue(
                    "@participantId", ranking.ParticipantId);
                command.Parameters.AddWithValue(
                    "@userId", ranking.UserId);
                command.Parameters.AddWithValue(
                    "@nickname", ranking.Nickname);
                command.Parameters.AddWithValue(
                    "@initialBalance", ranking.InitialBalance);
                command.Parameters.AddWithValue(
                    "@cashBalance", ranking.CashBalance);
                command.Parameters.AddWithValue(
                    "@stockEvaluation", ranking.StockEvaluation);
                command.Parameters.AddWithValue(
                    "@totalAsset", ranking.TotalAsset);
                command.Parameters.AddWithValue(
                    "@profitAmount", ranking.TotalProfit);
                command.Parameters.AddWithValue(
                    "@profitRate", ranking.ProfitRate);
                command.Parameters.AddWithValue(
                    "@ranking", ranking.Rank);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            const string finishSql = """
                UPDATE competitions
                SET status = '종료'
                WHERE competition_id = @competitionId
                  AND status = '정산중';
                """;

            await using (var finishCommand =
                new MySqlCommand(finishSql, connection, transaction))
            {
                finishCommand.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                int affectedRows =
                    await finishCommand.ExecuteNonQueryAsync(
                        cancellationToken);

                if (affectedRows != 1)
                {
                    throw new InvalidOperationException(
                        "대회 정산 상태가 변경되어 종료할 수 없습니다.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<List<CompetitionFinalRankingResponse>>
        GetFinalRankingsAsync(
            int competitionId,
            CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                ranking,
                participant_id,
                user_id,
                nickname,
                initial_balance,
                cash_balance,
                stock_evaluation,
                total_asset,
                profit_amount,
                profit_rate,
                finalized_at
            FROM competition_final_rankings
            WHERE competition_id = @competitionId
            ORDER BY ranking ASC, participant_id ASC;
            """;

        var rankings =
            new List<CompetitionFinalRankingResponse>();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rankings.Add(new CompetitionFinalRankingResponse
            {
                Rank = reader.GetInt32("ranking"),
                ParticipantId = reader.GetInt32("participant_id"),
                UserId = reader.GetInt32("user_id"),
                Nickname = reader.GetString("nickname"),
                InitialBalance = reader.GetDecimal("initial_balance"),
                CashBalance = reader.GetDecimal("cash_balance"),
                StockEvaluation = reader.GetDecimal("stock_evaluation"),
                TotalAsset = reader.GetDecimal("total_asset"),
                TotalProfit = reader.GetDecimal("profit_amount"),
                ProfitRate = reader.GetDecimal("profit_rate"),
                FinalizedAt = reader.GetDateTime("finalized_at")
            });
        }

        return rankings;
    }
}
