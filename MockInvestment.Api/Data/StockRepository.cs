using MockInvestment.Api.Models;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class StockRepository
{
    private readonly string _connectionString;

    public StockRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    public async Task<List<StockResponse>> GetStocksAsync(
        string? keyword,
        CancellationToken cancellationToken)
    {
        var stocks = new List<StockResponse>();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT stock_id, stock_code, stock_name, market
            FROM stocks
            WHERE is_active = 1
              AND (
                  @keyword = ''
                  OR LOCATE(@keyword, stock_name) > 0
                  OR LOCATE(@keyword, stock_code) > 0
              )
            ORDER BY stock_code;
            """;

        command.Parameters.AddWithValue(
            "@keyword",
            keyword?.Trim() ?? string.Empty);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stocks.Add(new StockResponse
            {
                StockId = reader.GetInt32(0),
                StockCode = reader.GetString(1),
                StockName = reader.GetString(2),
                Market = reader.GetString(3)
            });
        }

        return stocks;
    }

    public async Task<int> SaveMasterStocksAsync(
    List<StockMasterItem> stocks,
    CancellationToken cancellationToken)
    {
        if (stocks.Count == 0)
        {
            throw new ArgumentException(
                "저장할 종목이 없습니다.",
                nameof(stocks));
        }

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;

            command.CommandText = """
            INSERT INTO stocks (
                stock_code,
                stock_name,
                market
            )
            VALUES (
                @stockCode,
                @stockName,
                @market
            )
            ON DUPLICATE KEY UPDATE
                stock_name = @stockName,
                market = @market;
            """;

            // 매번 SQL을 새로 만들지 않고 값만 교체
            var codeParameter = command.Parameters.Add(
                "@stockCode", MySqlDbType.VarChar, 20);

            var nameParameter = command.Parameters.Add(
                "@stockName", MySqlDbType.VarChar, 100);

            var marketParameter = command.Parameters.Add(
                "@market", MySqlDbType.VarChar, 20);

            foreach (StockMasterItem stock in stocks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                codeParameter.Value = stock.StockCode;
                nameParameter.Value = stock.StockName;
                marketParameter.Value = stock.Market;

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            // 실제 변경 행 수가 아닌 처리한 종목 수
            return stocks.Count;
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    public async Task<StockResponse?> FindByIdAsync(
    int stockId,
    CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT stock_id, stock_code, stock_name, market
        FROM stocks
        WHERE stock_id = @stockId
          AND is_active = 1;
        """;

        command.Parameters.AddWithValue("@stockId", stockId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StockResponse
        {
            StockId = reader.GetInt32(0),
            StockCode = reader.GetString(1),
            StockName = reader.GetString(2),
            Market = reader.GetString(3)
        };
    }
}