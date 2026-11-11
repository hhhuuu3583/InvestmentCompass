
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class CompetitionTradeRepository
{
    private readonly string _connectionString;

    public CompetitionTradeRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    // 이미 체결된 대회 주문인지 확인
    public async Task<TradeResponse?> FindByOrderIdAsync(
        int competitionId,
        int userId,
        Guid clientOrderId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                t.competition_trade_id,
                t.client_order_id,
                t.stock_id,
                t.trade_type,
                t.quantity,
                t.unit_price,
                t.total_amount,
                t.traded_at
            FROM competition_trades t
            JOIN competition_accounts ca
                ON ca.competition_account_id =
                   t.competition_account_id
            JOIN competition_participants cp
                ON cp.participant_id = ca.participant_id
            WHERE cp.competition_id = @competitionId
              AND cp.user_id = @userId
              AND t.client_order_id = @orderId;
            """;

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        command.Parameters.AddWithValue(
            "@userId", userId);

        command.Parameters.AddWithValue(
            "@orderId", clientOrderId.ToString("D"));

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TradeResponse
        {
            TradeId = reader.GetInt64(0),
            ClientOrderId = Guid.Parse(reader.GetString(1)),
            StockId = reader.GetInt32(2),
            TradeType = reader.GetString(3),
            Quantity = reader.GetInt32(4),
            UnitPrice = reader.GetDecimal(5),
            TotalAmount = reader.GetDecimal(6),
            TradedAt = DateTime.SpecifyKind(
                reader.GetDateTime(7),
                DateTimeKind.Utc),
            IsReplay = true
        };
    }

    // 대회 매수
    public Task<TradeResponse> BuyAsync(
        int competitionId,
        int userId,
        TradeRequest request,
        decimal unitPrice,
        DateTime priceObservedAtUtc,
        CancellationToken cancellationToken)
    {
        return ExecuteTradeAsync(
            competitionId,
            userId,
            request,
            "BUY",
            unitPrice,
            priceObservedAtUtc,
            cancellationToken);
    }

    // 대회 매도
    public Task<TradeResponse> SellAsync(
        int competitionId,
        int userId,
        TradeRequest request,
        decimal unitPrice,
        DateTime priceObservedAtUtc,
        CancellationToken cancellationToken)
    {
        return ExecuteTradeAsync(
            competitionId,
            userId,
            request,
            "SELL",
            unitPrice,
            priceObservedAtUtc,
            cancellationToken);
    }

    // 공통 거래 처리
    private async Task<TradeResponse> ExecuteTradeAsync(
        int competitionId,
        int userId,
        TradeRequest request,
        string tradeType,
        decimal unitPrice,
        DateTime priceObservedAtUtc,
        CancellationToken cancellationToken)
    {
        // 1. 주문 정보 검증
        if (competitionId <= 0
            || userId <= 0
            || request.StockId <= 0
            || request.Quantity <= 0
            || request.ClientOrderId == Guid.Empty)
        {
            throw new TradeException(
                "INVALID_ORDER",
                "주문 정보가 올바르지 않습니다.");
        }

        if (unitPrice <= 0
            || decimal.Round(unitPrice, 2) != unitPrice)
        {
            throw new TradeException(
                "INVALID_PRICE",
                "체결 가격이 올바르지 않습니다.");
        }

        if (priceObservedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "가격 확인 시각은 UTC여야 합니다.",
                nameof(priceObservedAtUtc));
        }

        decimal totalAmount;

        try
        {
            totalAmount = checked(
                unitPrice * request.Quantity);
        }
        catch (OverflowException)
        {
            throw new TradeException(
                "ORDER_AMOUNT_OUT_OF_RANGE",
                "거래금액이 허용 범위를 초과했습니다.");
        }

        const decimal maxMoney =
            99_999_999_999_999_999.99m;

        if (totalAmount > maxMoney)
        {
            throw new TradeException(
                "ORDER_AMOUNT_OUT_OF_RANGE",
                "거래금액이 허용 범위를 초과했습니다.");
        }

        // 2. DB 연결 및 트랜잭션 시작
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            // 3. 대회 상태 및 기간 확인
            // 대회 행을 잠가 종료 처리와 거래가 겹치는 상황을 방지
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;


                command.CommandText = """
    SELECT
        status,
        start_date,
        end_date,
        NOW() AS db_now
    FROM competitions
    WHERE competition_id = @competitionId
    FOR UPDATE;
    """;

                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new TradeException(
                        "COMPETITION_NOT_FOUND",
                        "대회를 찾을 수 없습니다.");
                }

                string status = reader.GetString(0);

                // MySQL에 저장된 시간과 MySQL 현재 시간을 그대로 비교
                DateTime startDate = reader.GetDateTime(1);
                DateTime endDate = reader.GetDateTime(2);
                DateTime now = reader.GetDateTime(3);

                if (status != "진행중"
                    || now < startDate
                    || now > endDate)
                {
                    throw new TradeException(
                        "COMPETITION_NOT_ACTIVE",
                        "현재 거래할 수 있는 대회가 아닙니다.");
                }


                if (status != "진행중"
                    || now < startDate
                    || now > endDate)
                {
                    throw new TradeException(
                        "COMPETITION_NOT_ACTIVE",
                        "현재 거래할 수 있는 대회가 아닙니다.");
                }
            }

            // 4. 참가자의 대회 계좌 잠금
            int competitionAccountId;
            decimal cashBalance;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT
                        ca.competition_account_id,
                        ca.cash_balance
                    FROM competition_accounts ca
                    JOIN competition_participants cp
                        ON cp.participant_id = ca.participant_id
                    WHERE cp.competition_id = @competitionId
                      AND cp.user_id = @userId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                command.Parameters.AddWithValue(
                    "@userId", userId);

                await using var reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new TradeException(
                        "COMPETITION_ACCOUNT_NOT_FOUND",
                        "대회 참가 계좌가 없습니다.");
                }

                competitionAccountId = reader.GetInt32(0);
                cashBalance = reader.GetDecimal(1);
            }

            // 5. 잠금 이후 중복 주문 재확인
            TradeResponse? existing = null;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT
                        competition_trade_id,
                        stock_id,
                        trade_type,
                        quantity,
                        unit_price,
                        total_amount,
                        traded_at
                    FROM competition_trades
                    WHERE competition_account_id =
                          @accountId
                      AND client_order_id = @orderId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", competitionAccountId);

                command.Parameters.AddWithValue(
                    "@orderId",
                    request.ClientOrderId.ToString("D"));

                await using var reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    existing = new TradeResponse
                    {
                        TradeId = reader.GetInt64(0),
                        ClientOrderId = request.ClientOrderId,
                        StockId = reader.GetInt32(1),
                        TradeType = reader.GetString(2),
                        Quantity = reader.GetInt32(3),
                        UnitPrice = reader.GetDecimal(4),
                        TotalAmount = reader.GetDecimal(5),
                        TradedAt = DateTime.SpecifyKind(
                            reader.GetDateTime(6),
                            DateTimeKind.Utc),
                        IsReplay = true
                    };
                }
            }

            if (existing is not null)
            {
                if (existing.TradeType != tradeType
                    || existing.StockId != request.StockId
                    || existing.Quantity != request.Quantity)
                {
                    throw new TradeException(
                        "ORDER_ID_CONFLICT",
                        "같은 주문번호에 다른 주문이 등록되어 있습니다.");
                }

                await transaction.CommitAsync(
                    cancellationToken);

                return existing;
            }

            // 6. 시세 유효시간 검사
            TimeSpan priceAge =
                DateTime.UtcNow - priceObservedAtUtc;

            if (priceAge > TimeSpan.FromSeconds(10)
                || priceAge < TimeSpan.Zero)
            {
                throw new TradeException(
                    "STALE_QUOTE",
                    "가격 확인 시간이 지나 주문하지 못했습니다. 다시 시도해주세요.");
            }

            // 7. 거래 가능한 종목인지 확인
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT stock_id
                    FROM stocks
                    WHERE stock_id = @stockId
                      AND is_active = 1
                      AND market IN ('KOSPI', 'KOSDAQ')
                      AND stock_code REGEXP '^[0-9]{6}$'
                    FOR SHARE;
                    """;

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                object? stock =
                    await command.ExecuteScalarAsync(
                        cancellationToken);

                if (stock is null)
                {
                    throw new TradeException(
                        "STOCK_NOT_FOUND",
                        "거래 지원 종목을 찾을 수 없습니다.");
                }
            }

            // 8. 대회 보유 주식 조회
            int oldQuantity = 0;
            decimal oldAverage = 0;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT quantity, average_buy_price
                    FROM competition_holdings
                    WHERE competition_account_id = @accountId
                      AND stock_id = @stockId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", competitionAccountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                await using var reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    oldQuantity = reader.GetInt32(0);
                    oldAverage = reader.GetDecimal(1);
                }
            }

            // 9. 매수 또는 매도 처리
            if (tradeType == "BUY")
            {
                await BuyHoldingAsync(
                    connection,
                    transaction,
                    competitionAccountId,
                    request,
                    unitPrice,
                    totalAmount,
                    cashBalance,
                    oldQuantity,
                    oldAverage,
                    cancellationToken);
            }
            else
            {
                await SellHoldingAsync(
                    connection,
                    transaction,
                    competitionAccountId,
                    request,
                    totalAmount,
                    cashBalance,
                    oldQuantity,
                    cancellationToken);
            }

            // 10. 거래 시간 생성
            DateTime nowUtc = DateTime.UtcNow;

            DateTime tradedAt = new(
                nowUtc.Ticks - nowUtc.Ticks % 10,
                DateTimeKind.Utc);

            // 11. 대회 거래 내역 저장
            long tradeId;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    INSERT INTO competition_trades (
                        competition_account_id,
                        stock_id,
                        client_order_id,
                        trade_type,
                        quantity,
                        unit_price,
                        total_amount,
                        quoted_at,
                        traded_at
                    )
                    VALUES (
                        @accountId,
                        @stockId,
                        @orderId,
                        @tradeType,
                        @quantity,
                        @price,
                        @amount,
                        @observedAt,
                        @tradedAt
                    );
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", competitionAccountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                command.Parameters.AddWithValue(
                    "@orderId",
                    request.ClientOrderId.ToString("D"));

                command.Parameters.AddWithValue(
                    "@tradeType", tradeType);

                command.Parameters.AddWithValue(
                    "@quantity", request.Quantity);

                command.Parameters.AddWithValue(
                    "@price", unitPrice);

                command.Parameters.AddWithValue(
                    "@amount", totalAmount);

                command.Parameters.AddWithValue(
                    "@observedAt", priceObservedAtUtc);

                command.Parameters.AddWithValue(
                    "@tradedAt", tradedAt);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);

                tradeId = command.LastInsertedId;
            }

            // 12. 모든 변경 확정
            await transaction.CommitAsync(cancellationToken);

            return new TradeResponse
            {
                TradeId = tradeId,
                ClientOrderId = request.ClientOrderId,
                StockId = request.StockId,
                TradeType = tradeType,
                Quantity = request.Quantity,
                UnitPrice = unitPrice,
                TotalAmount = totalAmount,
                TradedAt = tradedAt,
                IsReplay = false
            };
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    // 매수 처리
    private static async Task BuyHoldingAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int accountId,
        TradeRequest request,
        decimal unitPrice,
        decimal totalAmount,
        decimal cashBalance,
        int oldQuantity,
        decimal oldAverage,
        CancellationToken cancellationToken)
    {
        if (cashBalance < totalAmount)
        {
            throw new TradeException(
                "INSUFFICIENT_BALANCE",
                "대회 계좌의 현금 잔액이 부족합니다.");
        }

        if (request.Quantity > int.MaxValue - oldQuantity)
        {
            throw new TradeException(
                "HOLDING_QUANTITY_OUT_OF_RANGE",
                "보유 수량이 허용 범위를 초과했습니다.");
        }

        int newQuantity = oldQuantity + request.Quantity;

        decimal newAverage = decimal.Round(
            (oldAverage * oldQuantity + totalAmount)
            / newQuantity,
            6,
            MidpointRounding.AwayFromZero);

        if (newAverage > 9_999_999_999_999.999999m)
        {
            throw new TradeException(
                "PRICE_OUT_OF_RANGE",
                "평균 매입단가가 저장 범위를 초과했습니다.");
        }

        // 현금 차감
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.CommandText = """
                UPDATE competition_accounts
                SET cash_balance = cash_balance - @amount
                WHERE competition_account_id = @accountId
                  AND cash_balance >= @amount;
                """;

            command.Parameters.AddWithValue(
                "@amount", totalAmount);

            command.Parameters.AddWithValue(
                "@accountId", accountId);

            int affected = await command.ExecuteNonQueryAsync(
                cancellationToken);

            if (affected != 1)
            {
                throw new TradeException(
                    "INSUFFICIENT_BALANCE",
                    "대회 계좌의 현금 잔액이 부족합니다.");
            }
        }

        // 보유 주식 생성 또는 갱신
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.CommandText = """
                INSERT INTO competition_holdings (
                    competition_account_id,
                    stock_id,
                    quantity,
                    average_buy_price
                )
                VALUES (
                    @accountId,
                    @stockId,
                    @quantity,
                    @average
                )
                ON DUPLICATE KEY UPDATE
                    quantity = @quantity,
                    average_buy_price = @average;
                """;

            command.Parameters.AddWithValue(
                "@accountId", accountId);

            command.Parameters.AddWithValue(
                "@stockId", request.StockId);

            command.Parameters.AddWithValue(
                "@quantity", newQuantity);

            command.Parameters.AddWithValue(
                "@average", newAverage);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    // 매도 처리
    private static async Task SellHoldingAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int accountId,
        TradeRequest request,
        decimal totalAmount,
        decimal cashBalance,
        int holdingQuantity,
        CancellationToken cancellationToken)
    {
        if (holdingQuantity < request.Quantity)
        {
            throw new TradeException(
                "INSUFFICIENT_HOLDINGS",
                "대회 계좌의 보유 수량이 부족합니다.");
        }

        const decimal maxMoney =
            99_999_999_999_999_999.99m;

        if (cashBalance > maxMoney - totalAmount)
        {
            throw new TradeException(
                "BALANCE_OUT_OF_RANGE",
                "매도 후 현금 잔액이 허용 범위를 초과합니다.");
        }

        // 보유 수량 감소 또는 전량 매도
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.Parameters.AddWithValue(
                "@accountId", accountId);

            command.Parameters.AddWithValue(
                "@stockId", request.StockId);

            command.Parameters.AddWithValue(
                "@quantity", request.Quantity);

            if (holdingQuantity == request.Quantity)
            {
                command.CommandText = """
                    DELETE FROM competition_holdings
                    WHERE competition_account_id = @accountId
                      AND stock_id = @stockId;
                    """;
            }
            else
            {
                command.CommandText = """
                    UPDATE competition_holdings
                    SET quantity = quantity - @quantity
                    WHERE competition_account_id = @accountId
                      AND stock_id = @stockId
                      AND quantity >= @quantity;
                    """;
            }

            int affected = await command.ExecuteNonQueryAsync(
                cancellationToken);

            if (affected != 1)
            {
                throw new TradeException(
                    "INSUFFICIENT_HOLDINGS",
                    "대회 계좌의 보유 주식 변경에 실패했습니다.");
            }
        }

        // 매도 금액을 대회 계좌에 반영
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.CommandText = """
                UPDATE competition_accounts
                SET cash_balance = cash_balance + @amount
                WHERE competition_account_id = @accountId;
                """;

            command.Parameters.AddWithValue(
                "@amount", totalAmount);

            command.Parameters.AddWithValue(
                "@accountId", accountId);

            int affected = await command.ExecuteNonQueryAsync(
                cancellationToken);

            if (affected != 1)
            {
                throw new TradeException(
                    "ACCOUNT_NOT_FOUND",
                    "대회 계좌 잔액 변경에 실패했습니다.");
            }
        }
    }
}
