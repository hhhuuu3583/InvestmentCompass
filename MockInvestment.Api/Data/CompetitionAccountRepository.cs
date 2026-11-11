
using MySqlConnector;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Data;

public class CompetitionAccountRepository
{
    private readonly IConfiguration _configuration;

    public CompetitionAccountRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // 대회 참가자의 계좌 및 보유 종목 조회
    public async Task<CompetitionAccountData?> GetAccountAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        string connectionString =
            _configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySql 연결 문자열을 찾을 수 없습니다.");

        const string sql = @"
            SELECT
                ca.competition_account_id,
                ca.initial_balance,
                ca.cash_balance,
                ch.stock_id,
                s.stock_code,
                s.stock_name,
                ch.quantity,
                ch.average_buy_price
            FROM competition_participants cp
            INNER JOIN competition_accounts ca
                ON cp.participant_id = ca.participant_id
            LEFT JOIN competition_holdings ch
                ON ca.competition_account_id = ch.competition_account_id
            LEFT JOIN stocks s
                ON ch.stock_id = s.stock_id
            WHERE cp.competition_id = @competitionId
              AND cp.user_id = @userId
            ORDER BY s.stock_name;
        ";

        using var connection =
            new MySqlConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        command.Parameters.AddWithValue(
            "@userId", userId);

        using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        CompetitionAccountData? account = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            // 계좌 기본 정보는 최초 한 번만 저장
            if (account == null)
            {
                account = new CompetitionAccountData
                {
                    CompetitionAccountId =
                        reader.GetInt32("competition_account_id"),

                    InitialBalance =
                        reader.GetDecimal("initial_balance"),

                    CashBalance =
                        reader.GetDecimal("cash_balance")
                };
            }

            // 보유 종목이 없는 경우
            if (reader.IsDBNull(reader.GetOrdinal("stock_id")))
                continue;

            CompetitionHoldingData holding =
                new CompetitionHoldingData
                {
                    StockId = reader.GetInt32("stock_id"),

                    StockCode = reader.GetString("stock_code"),

                    StockName = reader.GetString("stock_name"),

                    Quantity = reader.GetInt32("quantity"),

                    AverageBuyPrice =
                        reader.GetDecimal("average_buy_price")
                };

            account.Holdings.Add(holding);
        }

        return account;
    }
}
