
using MockInvestment.Api.Models;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class PortfolioRepository
{
    private readonly string _connectionString;

    public PortfolioRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    public async Task<PortfolioData?> GetAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                a.initial_balance,
                a.cash_balance,
                s.stock_id,
                s.stock_code,
                s.stock_name,
                s.market,
                h.quantity,
                h.average_buy_price
            FROM accounts a
            LEFT JOIN holdings h
                ON h.account_id = a.account_id
            LEFT JOIN stocks s
                ON s.stock_id = h.stock_id
            WHERE a.user_id = @userId
            ORDER BY s.stock_code;
            """;

        command.Parameters.AddWithValue("@userId", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        PortfolioData? portfolio = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (portfolio is null)
            {
                portfolio = new PortfolioData
                {
                    InitialBalance = reader.GetDecimal(0),
                    CashBalance = reader.GetDecimal(1)
                };
            }

            if (reader.IsDBNull(2))
                continue;

            portfolio.Holdings.Add(new HoldingData
            {
                StockId = reader.GetInt32(2),
                StockCode = reader.GetString(3),
                StockName = reader.GetString(4),
                Market = reader.GetString(5),
                Quantity = reader.GetInt32(6),
                AverageBuyPrice = reader.GetDecimal(7)
            });
        }

        return portfolio;
    }

    public async Task<PortfolioData?> GetCompetitionAsync(
        int userId,
        int competitionId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                ca.initial_balance,
                ca.cash_balance,
                s.stock_id,
                s.stock_code,
                s.stock_name,
                s.market,
                h.quantity,
                h.average_buy_price
            FROM competition_participants cp
            INNER JOIN competitions c
                ON cp.competition_id = c.competition_id
            INNER JOIN competition_accounts ca
                ON cp.participant_id = ca.participant_id
            LEFT JOIN competition_holdings h
                ON ca.competition_account_id =
                   h.competition_account_id
            LEFT JOIN stocks s
                ON h.stock_id = s.stock_id
            WHERE cp.user_id = @userId
              AND cp.competition_id = @competitionId
              AND c.status IN ('진행중', '정산중')
            ORDER BY s.stock_code;
            """;

        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        PortfolioData? portfolio = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (portfolio is null)
            {
                portfolio = new PortfolioData
                {
                    InitialBalance = reader.GetDecimal(0),
                    CashBalance = reader.GetDecimal(1)
                };
            }

            if (reader.IsDBNull(2))
                continue;

            portfolio.Holdings.Add(new HoldingData
            {
                StockId = reader.GetInt32(2),
                StockCode = reader.GetString(3),
                StockName = reader.GetString(4),
                Market = reader.GetString(5),
                Quantity = reader.GetInt32(6),
                AverageBuyPrice = reader.GetDecimal(7)
            });
        }

        return portfolio;
    }
}
