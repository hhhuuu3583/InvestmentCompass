
using MockInvestment.Api.Models;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class CompetitionTradeHistoryRepository
{
    private readonly string _connectionString;

    public CompetitionTradeHistoryRepository(
        IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    // 대회 거래내역 전체 조회
    public async Task<List<CompetitionTradeHistoryResponse>>
        GetMyTradesAsync(
            int competitionId,
            int userId,
            CancellationToken cancellationToken)
    {
        var result = new List<CompetitionTradeHistoryResponse>();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                ct.competition_trade_id,
                cp.competition_id,
                ct.stock_id,
                s.stock_code,
                s.stock_name,
                ct.trade_type,
                ct.quantity,
                ct.unit_price,
                ct.total_amount,
                ct.quoted_at,
                ct.traded_at
            FROM competition_trades ct
            INNER JOIN competition_accounts ca
                ON ct.competition_account_id =
                   ca.competition_account_id
            INNER JOIN competition_participants cp
                ON ca.participant_id = cp.participant_id
            INNER JOIN stocks s
                ON ct.stock_id = s.stock_id
            WHERE cp.competition_id = @CompetitionId
              AND cp.user_id = @UserId
            ORDER BY ct.traded_at DESC,
                     ct.competition_trade_id DESC;
        ";

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@CompetitionId", competitionId);

        command.Parameters.AddWithValue(
            "@UserId", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CompetitionTradeHistoryResponse
            {
                CompetitionTradeId =
                    reader.GetInt64("competition_trade_id"),

                CompetitionId =
                    reader.GetInt32("competition_id"),

                StockId =
                    reader.GetInt32("stock_id"),

                StockCode =
                    reader.GetString("stock_code"),

                StockName =
                    reader.GetString("stock_name"),

                TradeType =
                    reader.GetString("trade_type"),

                Quantity =
                    reader.GetInt32("quantity"),

                UnitPrice =
                    reader.GetDecimal("unit_price"),

                TotalAmount =
                    reader.GetDecimal("total_amount"),

                QuotedAt =
                    reader.GetDateTime("quoted_at"),

                TradedAt =
                    reader.GetDateTime("traded_at")
            });
        }

        return result;
    }

    // 대회 거래내역을 일반 거래내역과 동일한 페이지 응답 형식으로 조회
    public async Task<(long TotalCount, List<TradeHistoryItem> Items)>
        GetMyTradesPageAsync(
            int competitionId,
            int userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        if (competitionId <= 0 || userId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(competitionId),
                "대회 번호 또는 사용자 번호가 올바르지 않습니다.");
        }

        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "페이지 또는 페이지 크기가 올바르지 않습니다.");
        }

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead,
                cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;

            command.Parameters.AddWithValue(
                "@competitionId", competitionId);

            command.Parameters.AddWithValue(
                "@userId", userId);

            // 1. 전체 거래내역 개수 조회
            command.CommandText = """
                SELECT COUNT(*)
                FROM competition_trades ct
                INNER JOIN competition_accounts ca
                    ON ct.competition_account_id =
                       ca.competition_account_id
                INNER JOIN competition_participants cp
                    ON ca.participant_id = cp.participant_id
                WHERE cp.competition_id = @competitionId
                  AND cp.user_id = @userId;
                """;

            long totalCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken));

            // 2. 페이지에 해당하는 거래내역 조회
            command.CommandText = """
                SELECT
                    ct.competition_trade_id,
                    ct.stock_id,
                    s.stock_code,
                    s.stock_name,
                    ct.trade_type,
                    ct.quantity,
                    ct.unit_price,
                    ct.total_amount,
                    ct.traded_at
                FROM competition_trades ct
                INNER JOIN competition_accounts ca
                    ON ct.competition_account_id =
                       ca.competition_account_id
                INNER JOIN competition_participants cp
                    ON ca.participant_id = cp.participant_id
                INNER JOIN stocks s
                    ON ct.stock_id = s.stock_id
                WHERE cp.competition_id = @competitionId
                  AND cp.user_id = @userId
                ORDER BY ct.traded_at DESC,
                         ct.competition_trade_id DESC
                LIMIT @pageSize OFFSET @offset;
                """;

            command.Parameters.AddWithValue(
                "@pageSize", pageSize);

            command.Parameters.AddWithValue(
                "@offset", (page - 1L) * pageSize);

            var items = new List<TradeHistoryItem>();

            await using (var reader =
                await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(new TradeHistoryItem
                    {
                        TradeId = reader.GetInt64(0),
                        StockId = reader.GetInt32(1),
                        StockCode = reader.GetString(2),
                        StockName = reader.GetString(3),
                        TradeType = reader.GetString(4),
                        Quantity = reader.GetInt32(5),
                        UnitPrice = reader.GetDecimal(6),
                        TotalAmount = reader.GetDecimal(7),

                        TradedAt = DateTime.SpecifyKind(
                            reader.GetDateTime(8),
                            DateTimeKind.Utc)
                    });
                }
            }

            await transaction.CommitAsync(cancellationToken);

            return (totalCount, items);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
