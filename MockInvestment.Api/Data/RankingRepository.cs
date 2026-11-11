using MockInvestment.Api.Models;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class RankingRepository
{
    private readonly string _connectionString;

    public RankingRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    public async Task<List<RankingAccountData>> GetAccountsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                u.user_id,
                u.nickname,
                a.initial_balance,
                a.cash_balance,
                s.stock_id,
                s.stock_code,
                s.stock_name,
                s.market,
                h.quantity,
                h.average_buy_price
            FROM users u
            JOIN accounts a
                ON a.user_id = u.user_id
            LEFT JOIN holdings h
                ON h.account_id = a.account_id
            LEFT JOIN stocks s
                ON s.stock_id = h.stock_id
            ORDER BY u.user_id, s.stock_code;
            """;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var accounts = new List<RankingAccountData>();
        RankingAccountData? current = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            int userId = reader.GetInt32(0);

            if (current is null || current.UserId != userId)
            {
                current = new RankingAccountData
                {
                    UserId = userId,
                    Nickname = reader.GetString(1),
                    InitialBalance = reader.GetDecimal(2),
                    CashBalance = reader.GetDecimal(3)
                };

                accounts.Add(current);
            }

            if (reader.IsDBNull(4))
            {
                continue;
            }

            current.Holdings.Add(new HoldingData
            {
                StockId = reader.GetInt32(4),
                StockCode = reader.GetString(5),
                StockName = reader.GetString(6),
                Market = reader.GetString(7),
                Quantity = reader.GetInt32(8),
                AverageBuyPrice = reader.GetDecimal(9)
            });
        }

        return accounts;
    }
}