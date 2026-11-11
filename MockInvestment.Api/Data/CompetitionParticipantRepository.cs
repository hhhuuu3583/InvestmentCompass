
using MySqlConnector;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Data;

public class CompetitionParticipantRepository
{
    private readonly IConfiguration _configuration;

    public CompetitionParticipantRepository(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 문자열을 찾을 수 없습니다.");
    }

    // 대회 참가자 목록 조회
    public async Task<List<CompetitionParticipantListResponse>> GetParticipantsAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
        SELECT
            cp.participant_id,
            cp.competition_id,
            cp.user_id,
            u.login_id,
            u.nickname,
            cp.joined_at,
            ca.competition_account_id,
            ca.initial_balance,
            ca.cash_balance
        FROM competition_participants cp
        INNER JOIN users u
            ON cp.user_id = u.user_id
        INNER JOIN competition_accounts ca
            ON cp.participant_id = ca.participant_id
        WHERE cp.competition_id = @competitionId
        ORDER BY cp.joined_at ASC;
        ";

        List<CompetitionParticipantListResponse> participants = new();

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            participants.Add(new CompetitionParticipantListResponse
            {
                ParticipantId = reader.GetInt32("participant_id"),
                CompetitionId = reader.GetInt32("competition_id"),
                UserId = reader.GetInt32("user_id"),
                LoginId = reader.GetString("login_id"),
                Nickname = reader.GetString("nickname"),
                JoinedAt = reader.GetDateTime("joined_at"),
                CompetitionAccountId =
                    reader.GetInt32("competition_account_id"),
                InitialBalance = reader.GetDecimal("initial_balance"),
                CashBalance = reader.GetDecimal("cash_balance")
            });
        }

        return participants;
    }

    // 대회 참가 등록 및 대회 전용 계좌 자동 생성
    public async Task<CompetitionParticipantResponse> AddParticipantAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. 대회 정보 확인 및 잠금
            const string competitionSql = @"
                SELECT initial_balance, status
                FROM competitions
                WHERE competition_id = @competitionId
                FOR UPDATE;
            ";

            decimal initialBalance;
            string status;

            await using (var command = new MySqlCommand(
                competitionSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "존재하지 않는 대회입니다.");
                }

                initialBalance =
                    reader.GetDecimal("initial_balance");

                status = reader.GetString("status");
            }

            // 2. 준비 상태인지 확인
            if (status != "준비")
            {
                throw new InvalidOperationException(
                    "준비 상태의 대회에만 참가자를 등록할 수 있습니다.");
            }

            // 3. 이미 참가했는지 확인
            const string duplicateSql = @"
                SELECT COUNT(*)
                FROM competition_participants
                WHERE competition_id = @competitionId
                  AND user_id = @userId;
            ";

            await using (var command = new MySqlCommand(
                duplicateSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                command.Parameters.AddWithValue("@userId", userId);

                long count = Convert.ToInt64(
                    await command.ExecuteScalarAsync(cancellationToken));

                if (count > 0)
                {
                    throw new InvalidOperationException(
                        "이미 해당 대회에 참가한 회원입니다.");
                }
            }

            // 4. 참가자 등록
            const string participantSql = @"
                INSERT INTO competition_participants
                (
                    competition_id,
                    user_id,
                    joined_at
                )
                VALUES
                (
                    @competitionId,
                    @userId,
                    CURRENT_TIMESTAMP(6)
                );
            ";

            int participantId;

            await using (var command = new MySqlCommand(
                participantSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                command.Parameters.AddWithValue("@userId", userId);

                await command.ExecuteNonQueryAsync(cancellationToken);

                participantId = (int)command.LastInsertedId;
            }

            // 5. 대회 전용 계좌 생성
            const string accountSql = @"
                INSERT INTO competition_accounts
                (
                    participant_id,
                    initial_balance,
                    cash_balance,
                    created_at
                )
                VALUES
                (
                    @participantId,
                    @initialBalance,
                    @cashBalance,
                    CURRENT_TIMESTAMP(6)
                );
            ";

            int competitionAccountId;

            await using (var command = new MySqlCommand(
                accountSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@participantId", participantId);

                command.Parameters.AddWithValue(
                    "@initialBalance", initialBalance);

                command.Parameters.AddWithValue(
                    "@cashBalance", initialBalance);

                await command.ExecuteNonQueryAsync(cancellationToken);

                competitionAccountId = (int)command.LastInsertedId;
            }

            // 6. 모든 작업 성공 시 커밋
            await transaction.CommitAsync(cancellationToken);

            return new CompetitionParticipantResponse
            {
                ParticipantId = participantId,
                CompetitionAccountId = competitionAccountId,
                CompetitionId = competitionId,
                UserId = userId,
                InitialBalance = initialBalance,
                CashBalance = initialBalance
            };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // 대회 참가자 삭제
    public async Task DeleteParticipantAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. 대회 상태 확인 및 잠금
            const string competitionSql = @"
                SELECT status
                FROM competitions
                WHERE competition_id = @competitionId
                FOR UPDATE;
            ";

            string status;

            await using (var command = new MySqlCommand(
                competitionSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                object? result =
                    await command.ExecuteScalarAsync(cancellationToken);

                if (result == null)
                {
                    throw new InvalidOperationException(
                        "존재하지 않는 대회입니다.");
                }

                status = result.ToString()!;
            }

            if (status != "준비")
            {
                throw new InvalidOperationException(
                    "준비 상태의 대회에서만 참가자를 삭제할 수 있습니다.");
            }

            // 2. 참가자 및 대회 계좌 확인
            const string participantSql = @"
                SELECT
                    cp.participant_id,
                    ca.competition_account_id
                FROM competition_participants cp
                INNER JOIN competition_accounts ca
                    ON cp.participant_id = ca.participant_id
                WHERE cp.competition_id = @competitionId
                  AND cp.user_id = @userId
                FOR UPDATE;
            ";

            int competitionAccountId;

            await using (var command = new MySqlCommand(
                participantSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                command.Parameters.AddWithValue("@userId", userId);

                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "해당 대회에 참가한 회원이 아닙니다.");
                }

                competitionAccountId =
                    reader.GetInt32("competition_account_id");
            }

            // 3. 거래 내역 존재 여부 확인
            const string tradeSql = @"
                SELECT COUNT(*)
                FROM competition_trades
                WHERE competition_account_id = @accountId;
            ";

            await using (var command = new MySqlCommand(
                tradeSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@accountId", competitionAccountId);

                long tradeCount = Convert.ToInt64(
                    await command.ExecuteScalarAsync(cancellationToken));

                if (tradeCount > 0)
                {
                    throw new InvalidOperationException(
                        "거래 내역이 있는 참가자는 삭제할 수 없습니다.");
                }
            }

            // 4. 보유 주식 삭제
            const string holdingSql = @"
                DELETE FROM competition_holdings
                WHERE competition_account_id = @accountId;
            ";

            await using (var command = new MySqlCommand(
                holdingSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@accountId", competitionAccountId);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // 5. 대회 계좌 삭제
            const string accountSql = @"
                DELETE FROM competition_accounts
                WHERE competition_account_id = @accountId;
            ";

            await using (var command = new MySqlCommand(
                accountSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@accountId", competitionAccountId);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // 6. 참가자 삭제
            const string deleteParticipantSql = @"
                DELETE FROM competition_participants
                WHERE competition_id = @competitionId
                  AND user_id = @userId;
            ";

            await using (var command = new MySqlCommand(
                deleteParticipantSql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@competitionId", competitionId);

                command.Parameters.AddWithValue("@userId", userId);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // 7. 모든 작업 성공 시 커밋
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // 로그인한 사용자의 현재 진행 중이거나 정산 중인 대회 계좌 조회
    public async Task<ActiveCompetitionResponse?> GetMyActiveCompetitionAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                c.competition_id,
                cp.participant_id,
                ca.competition_account_id,
                ca.initial_balance,
                ca.cash_balance,
                c.status
            FROM competition_participants cp
            INNER JOIN competitions c
                ON cp.competition_id = c.competition_id
            INNER JOIN competition_accounts ca
                ON cp.participant_id = ca.participant_id
            WHERE cp.user_id = @userId
              AND c.status IN ('진행중', '정산중')
            LIMIT 1;
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@userId", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActiveCompetitionResponse
        {
            CompetitionId = reader.GetInt32("competition_id"),
            ParticipantId = reader.GetInt32("participant_id"),
            CompetitionAccountId =
                reader.GetInt32("competition_account_id"),
            InitialBalance = reader.GetDecimal("initial_balance"),
            CashBalance = reader.GetDecimal("cash_balance"),
            CompetitionStatus = reader.GetString("status")
        };
    }
}
