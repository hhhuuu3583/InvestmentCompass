
using MockInvestment.Api.Models;
using MySqlConnector;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Data;

public class TradeRepository
{
    private readonly string _connectionString;

    public TradeRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 설정이 없습니다.");
    }

    // 주문 번호로 기존 거래 조회
    // 동일한 주문이 재전송되었을 때 중복 거래를 방지한다.
    public async Task<TradeResponse?> FindByOrderIdAsync(
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
                t.trade_id,
                t.client_order_id,
                t.stock_id,
                t.trade_type,
                t.quantity,
                t.unit_price,
                t.total_amount,
                t.traded_at
            FROM trades t
            JOIN accounts a
                ON a.account_id = t.account_id
            WHERE a.user_id = @userId
              AND t.client_order_id = @clientOrderId;
            """;

        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue(
            "@clientOrderId", clientOrderId.ToString("D"));

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

    // 일반 계좌 매수
    public async Task<TradeResponse> BuyAsync(
        int userId,
        TradeRequest request,
        decimal unitPrice,
        DateTime priceObservedAtUtc,
        CancellationToken cancellationToken)
    {
        // 1. 주문 정보 검증
        if (request.StockId <= 0
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

        // 2. 총 거래금액 계산
        decimal totalAmount;

        try
        {
            totalAmount = checked(unitPrice * request.Quantity);
        }
        catch (OverflowException)
        {
            throw new TradeException(
                "ORDER_AMOUNT_OUT_OF_RANGE",
                "거래금액이 허용 범위를 초과했습니다.");
        }

        // DECIMAL(19,2) 최대값
        const decimal maxMoney =
            99_999_999_999_999_999.99m;

        if (totalAmount > maxMoney)
        {
            throw new TradeException(
                "ORDER_AMOUNT_OUT_OF_RANGE",
                "거래금액이 허용 범위를 초과했습니다.");
        }

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            int accountId;
            decimal cashBalance;

            // 3. 회원 계좌 잠금 및 잔액 조회
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT account_id, cash_balance
                    FROM accounts
                    WHERE user_id = @userId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue("@userId", userId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new TradeException(
                        "ACCOUNT_NOT_FOUND",
                        "가상 계좌가 없습니다.");
                }

                accountId = reader.GetInt32(0);
                cashBalance = reader.GetDecimal(1);
            }

            // 4. 중복 주문 확인
            TradeResponse? existing = null;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT
                        trade_id,
                        stock_id,
                        trade_type,
                        quantity,
                        unit_price,
                        total_amount,
                        traded_at
                    FROM trades
                    WHERE account_id = @accountId
                      AND client_order_id = @orderId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@orderId", request.ClientOrderId.ToString("D"));

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

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
                if (existing.TradeType != "BUY"
                    || existing.StockId != request.StockId
                    || existing.Quantity != request.Quantity)
                {
                    throw new TradeException(
                        "ORDER_ID_CONFLICT",
                        "같은 주문 번호에 다른 주문이 등록되어 있습니다.");
                }

                await transaction.CommitAsync(cancellationToken);

                return existing;
            }

            // 5. 현재 대회 참가 중인지 확인
            // 대회 참가 중에는 일반 계좌 거래를 차단한다.
            await EnsureNotInActiveCompetitionAsync(
                connection,
                transaction,
                userId,
                cancellationToken);

            // 6. 현재 가격의 유효 시간 확인
            TimeSpan priceAge =
                DateTime.UtcNow - priceObservedAtUtc;

            if (priceAge > TimeSpan.FromSeconds(10)
                || priceAge < TimeSpan.Zero)
            {
                throw new TradeException(
                    "STALE_QUOTE",
                    "가격 확인 시간이 오래되어 주문할 수 없습니다. 다시 시도해 주세요.");
            }

            // 7. 실제 거래 지원 종목인지 확인
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

                object? stockId =
                    await command.ExecuteScalarAsync(cancellationToken);

                if (stockId is null)
                {
                    throw new TradeException(
                        "STOCK_NOT_FOUND",
                        "거래 지원 종목을 찾을 수 없습니다.");
                }
            }

            // 8. 잔액 확인
            if (cashBalance < totalAmount)
            {
                throw new TradeException(
                    "INSUFFICIENT_BALANCE",
                    "예수금 잔액이 부족합니다.");
            }

            // 9. 기존 보유 정보 조회
            int oldQuantity = 0;
            decimal oldAverage = 0;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT quantity, average_buy_price
                    FROM holdings
                    WHERE account_id = @accountId
                      AND stock_id = @stockId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    oldQuantity = reader.GetInt32(0);
                    oldAverage = reader.GetDecimal(1);
                }
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

            // 평균 매입가격 DECIMAL(19,6) 최대 범위
            if (newAverage > 9_999_999_999_999.999999m)
            {
                throw new TradeException(
                    "PRICE_OUT_OF_RANGE",
                    "평균 매입가격이 허용 범위를 초과했습니다.");
            }

            // 10. 예수금 차감
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    UPDATE accounts
                    SET cash_balance = cash_balance - @amount
                    WHERE account_id = @accountId
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
                        "예수금 잔액이 부족합니다.");
                }
            }

            // 11. 보유 정보 생성 또는 갱신
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    INSERT INTO holdings (
                        account_id,
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

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // MySQL DATETIME(6)에 맞춰 마이크로초 단위로 기록
            DateTime now = DateTime.UtcNow;

            DateTime tradedAt = new(
                now.Ticks - now.Ticks % 10,
                DateTimeKind.Utc);

            long tradeId;

            // 12. 매수 거래 기록 저장
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    INSERT INTO trades (
                        account_id,
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
                        'BUY',
                        @quantity,
                        @price,
                        @amount,
                        @observedAt,
                        @tradedAt
                    );
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                command.Parameters.AddWithValue(
                    "@orderId", request.ClientOrderId.ToString("D"));

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

                await command.ExecuteNonQueryAsync(cancellationToken);

                tradeId = command.LastInsertedId;
            }

            // 13. 전체 변경사항 확정
            await transaction.CommitAsync(cancellationToken);

            return new TradeResponse
            {
                TradeId = tradeId,
                ClientOrderId = request.ClientOrderId,
                StockId = request.StockId,
                TradeType = "BUY",
                Quantity = request.Quantity,
                UnitPrice = unitPrice,
                TotalAmount = totalAmount,
                TradedAt = tradedAt,
                IsReplay = false
            };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // 일반 계좌 매도
    public async Task<TradeResponse> SellAsync(
        int userId,
        TradeRequest request,
        decimal unitPrice,
        DateTime priceObservedAtUtc,
        CancellationToken cancellationToken)
    {
        // 1. 주문 정보 검증
        if (request.StockId <= 0
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

        // 2. 총 거래금액 계산
        decimal totalAmount;

        try
        {
            totalAmount = checked(unitPrice * request.Quantity);
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

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            int accountId;
            decimal cashBalance;

            // 3. 계좌 잠금 및 잔액 조회
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT account_id, cash_balance
                    FROM accounts
                    WHERE user_id = @userId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue("@userId", userId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new TradeException(
                        "ACCOUNT_NOT_FOUND",
                        "가상 계좌가 없습니다.");
                }

                accountId = reader.GetInt32(0);
                cashBalance = reader.GetDecimal(1);
            }

            // 4. 중복 주문 확인
            TradeResponse? existing = null;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT
                        trade_id,
                        stock_id,
                        trade_type,
                        quantity,
                        unit_price,
                        total_amount,
                        traded_at
                    FROM trades
                    WHERE account_id = @accountId
                      AND client_order_id = @orderId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@orderId", request.ClientOrderId.ToString("D"));

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

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
                if (existing.TradeType != "SELL"
                    || existing.StockId != request.StockId
                    || existing.Quantity != request.Quantity)
                {
                    throw new TradeException(
                        "ORDER_ID_CONFLICT",
                        "같은 주문 번호에 다른 주문이 등록되어 있습니다.");
                }

                await transaction.CommitAsync(cancellationToken);

                return existing;
            }

            // 5. 현재 대회 참가 중인지 확인
            // 대회 참가 중에는 일반 계좌 거래를 차단한다.
            await EnsureNotInActiveCompetitionAsync(
                connection,
                transaction,
                userId,
                cancellationToken);

            // 6. 가격 확인 시각 검증
            TimeSpan priceAge =
                DateTime.UtcNow - priceObservedAtUtc;

            if (priceAge > TimeSpan.FromSeconds(10)
                || priceAge < TimeSpan.Zero)
            {
                throw new TradeException(
                    "STALE_QUOTE",
                    "가격 확인 시간이 오래되어 주문할 수 없습니다. 다시 시도해 주세요.");
            }

            // 7. 거래 지원 종목 확인
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
                    await command.ExecuteScalarAsync(cancellationToken);

                if (stock is null)
                {
                    throw new TradeException(
                        "STOCK_NOT_FOUND",
                        "거래 지원 종목을 찾을 수 없습니다.");
                }
            }

            // 8. 보유 수량 확인
            int holdingQuantity;

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    SELECT quantity
                    FROM holdings
                    WHERE account_id = @accountId
                      AND stock_id = @stockId
                    FOR UPDATE;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                object? quantityValue =
                    await command.ExecuteScalarAsync(cancellationToken);

                holdingQuantity = quantityValue is null
                    ? 0
                    : Convert.ToInt32(quantityValue);
            }

            if (holdingQuantity < request.Quantity)
            {
                throw new TradeException(
                    "INSUFFICIENT_HOLDINGS",
                    "보유 수량이 부족합니다.");
            }

            // 9. 매도 후 예수금 범위 확인
            if (cashBalance > maxMoney - totalAmount)
            {
                throw new TradeException(
                    "BALANCE_OUT_OF_RANGE",
                    "매도 후 예수금이 허용 범위를 초과합니다.");
            }

            // 10. 부분 매도 또는 전량 매도
            int holdingAffected;

            if (holdingQuantity == request.Quantity)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = transaction;

                command.CommandText = """
                    DELETE FROM holdings
                    WHERE account_id = @accountId
                      AND stock_id = @stockId;
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                holdingAffected =
                    await command.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var command = connection.CreateCommand();

                command.Transaction = transaction;

                command.CommandText = """
                    UPDATE holdings
                    SET quantity = quantity - @quantity
                    WHERE account_id = @accountId
                      AND stock_id = @stockId
                      AND quantity >= @quantity;
                    """;

                command.Parameters.AddWithValue(
                    "@quantity", request.Quantity);

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                holdingAffected =
                    await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (holdingAffected != 1)
            {
                throw new TradeException(
                    "INSUFFICIENT_HOLDINGS",
                    "보유 정보 변경에 실패했습니다.");
            }

            // 11. 매도금액을 예수금에 반영
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    UPDATE accounts
                    SET cash_balance = cash_balance + @amount
                    WHERE account_id = @accountId;
                    """;

                command.Parameters.AddWithValue(
                    "@amount", totalAmount);

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                int accountAffected =
                    await command.ExecuteNonQueryAsync(cancellationToken);

                if (accountAffected != 1)
                {
                    throw new TradeException(
                        "ACCOUNT_NOT_FOUND",
                        "계좌 잔액 변경에 실패했습니다.");
                }
            }

            // MySQL DATETIME(6)에 맞춰 마이크로초 단위로 기록
            DateTime now = DateTime.UtcNow;

            DateTime tradedAt = new(
                now.Ticks - now.Ticks % 10,
                DateTimeKind.Utc);

            long tradeId;

            // 12. 매도 거래 기록 저장
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;

                command.CommandText = """
                    INSERT INTO trades (
                        account_id,
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
                        'SELL',
                        @quantity,
                        @price,
                        @amount,
                        @observedAt,
                        @tradedAt
                    );
                    """;

                command.Parameters.AddWithValue(
                    "@accountId", accountId);

                command.Parameters.AddWithValue(
                    "@stockId", request.StockId);

                command.Parameters.AddWithValue(
                    "@orderId", request.ClientOrderId.ToString("D"));

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

                await command.ExecuteNonQueryAsync(cancellationToken);

                tradeId = command.LastInsertedId;
            }

            // 13. 전체 변경사항 확정
            await transaction.CommitAsync(cancellationToken);

            return new TradeResponse
            {
                TradeId = tradeId,
                ClientOrderId = request.ClientOrderId,
                StockId = request.StockId,
                TradeType = "SELL",
                Quantity = request.Quantity,
                UnitPrice = unitPrice,
                TotalAmount = totalAmount,
                TradedAt = tradedAt,
                IsReplay = false
            };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // 일반 계좌 거래내역 페이지 조회
    public async Task<(
        long TotalCount,
        List<TradeHistoryItem> Items)> GetHistoryAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "페이지 또는 페이지 크기가 올바르지 않습니다.");
        }

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        // 건수와 목록을 동일한 DB 스냅샷에서 조회
        await using var transaction =
            await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead,
                cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;

            command.Parameters.AddWithValue("@userId", userId);

            // 1. 전체 거래 건수 조회
            command.CommandText = """
                SELECT COUNT(*)
                FROM trades t
                JOIN accounts a
                    ON a.account_id = t.account_id
                WHERE a.user_id = @userId;
                """;

            long totalCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken));

            // 2. 페이지에 해당하는 거래내역 조회
            command.CommandText = """
                SELECT
                    t.trade_id,
                    t.stock_id,
                    s.stock_code,
                    s.stock_name,
                    t.trade_type,
                    t.quantity,
                    t.unit_price,
                    t.total_amount,
                    t.traded_at
                FROM trades t
                JOIN accounts a
                    ON a.account_id = t.account_id
                JOIN stocks s
                    ON s.stock_id = t.stock_id
                WHERE a.user_id = @userId
                ORDER BY t.traded_at DESC,
                         t.trade_id DESC
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

    // 대회 참가 중이거나 정산 중이면 일반 계좌 거래를 차단한다.
    // 대회 상태 변경과 동시에 실행되는 거래도 잠금으로 보호한다.
    private static async Task EnsureNotInActiveCompetitionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.status
            FROM competition_participants cp
            INNER JOIN competitions c
                ON cp.competition_id = c.competition_id
            WHERE cp.user_id = @userId
            FOR UPDATE;
            """;

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        command.Parameters.AddWithValue("@userId", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            string status = reader.GetString(0);

            if (status == "진행중" || status == "정산중")
            {
                throw new TradeException(
                    "ACTIVE_COMPETITION_USE_COMPETITION_ACCOUNT",
                    "현재 대회에 참가 중입니다. 대회 계좌를 이용해 주세요.");
            }
        }
    }
}
