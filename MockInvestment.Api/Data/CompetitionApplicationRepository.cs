
using MockInvestment.Api.Models;
using MySqlConnector;

namespace MockInvestment.Api.Data;

public class CompetitionApplicationRepository
{
    private readonly IConfiguration _configuration;

    public CompetitionApplicationRepository(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private MySqlConnection GetConnection()
    {
        string connectionString =
       _configuration.GetConnectionString("MySql")
       ?? throw new InvalidOperationException(
           "MySql 연결 문자열을 찾을 수 없습니다.");

        return new MySqlConnection(connectionString);
    }


    // 참가 신청
    public async Task<int> AddApplicationAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. 대회 상태 확인
            const string checkSql = @"
            SELECT status
            FROM competitions
            WHERE competition_id = @CompetitionId";

            using var checkCommand = new MySqlCommand(
                checkSql, connection, transaction);

            checkCommand.Parameters.AddWithValue(
                "@CompetitionId", competitionId);

            var status = await checkCommand.ExecuteScalarAsync(
                cancellationToken);

            if (status == null)
                throw new InvalidOperationException(
                    "대회를 찾을 수 없습니다.");

            if (status.ToString() != "준비")
                throw new InvalidOperationException(
                    "준비 상태의 대회만 참가 신청할 수 있습니다.");

            // 2. 이미 참가 중인지 확인
            const string participantCheckSql = @"
            SELECT COUNT(*)
            FROM competition_participants
            WHERE competition_id = @CompetitionId
              AND user_id = @UserId";

            using var participantCheckCommand = new MySqlCommand(
                participantCheckSql, connection, transaction);

            participantCheckCommand.Parameters.AddWithValue(
                "@CompetitionId", competitionId);

            participantCheckCommand.Parameters.AddWithValue(
                "@UserId", userId);

            int participantCount = Convert.ToInt32(
                await participantCheckCommand.ExecuteScalarAsync(
                    cancellationToken));

            if (participantCount > 0)
                throw new InvalidOperationException(
                    "이미 해당 대회에 참가 중인 사용자입니다.");

            // 3. 기존 참가 신청 기록 확인
            const string applicationCheckSql = @"
            SELECT application_id, status
            FROM competition_applications
            WHERE competition_id = @CompetitionId
              AND user_id = @UserId
            FOR UPDATE";

            int applicationId = 0;
            string applicationStatus = "";

            using (var applicationCheckCommand = new MySqlCommand(
                applicationCheckSql, connection, transaction))
            {
                applicationCheckCommand.Parameters.AddWithValue(
                    "@CompetitionId", competitionId);

                applicationCheckCommand.Parameters.AddWithValue(
                    "@UserId", userId);

                using var reader =
                    await applicationCheckCommand.ExecuteReaderAsync(
                        cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    applicationId =
                        reader.GetInt32("application_id");

                    applicationStatus =
                        reader.GetString("status");
                }
            }

            // 4. 기존 신청 기록이 있는 경우
            if (applicationId > 0)
            {
                // 이미 대기 중
                if (applicationStatus == "대기")
                    throw new InvalidOperationException(
                        "이미 참가 신청이 대기 중입니다.");

                // 이미 승인됨
                if (applicationStatus == "승인")
                    throw new InvalidOperationException(
                        "이미 승인된 참가 신청입니다.");

                // 거절된 경우 재신청
                if (applicationStatus == "거절")
                {
                    const string retrySql = @"
                    UPDATE competition_applications
                    SET
                        status = '대기',
                        applied_at = NOW(6),
                        processed_at = NULL
                    WHERE application_id = @ApplicationId
                      AND competition_id = @CompetitionId";

                    using var retryCommand = new MySqlCommand(
                        retrySql, connection, transaction);

                    retryCommand.Parameters.AddWithValue(
                        "@ApplicationId", applicationId);

                    retryCommand.Parameters.AddWithValue(
                        "@CompetitionId", competitionId);

                    await retryCommand.ExecuteNonQueryAsync(
                        cancellationToken);

                    await transaction.CommitAsync(cancellationToken);

                    return applicationId;
                }

                throw new InvalidOperationException(
                    "현재 참가 신청 상태에서는 재신청할 수 없습니다.");
            }

            // 5. 신청 기록이 없으면 신규 신청
            const string insertSql = @"
            INSERT INTO competition_applications
            (
                competition_id,
                user_id,
                status
            )
            VALUES
            (
                @CompetitionId,
                @UserId,
                '대기'
            )";

            using var insertCommand = new MySqlCommand(
                insertSql, connection, transaction);

            insertCommand.Parameters.AddWithValue(
                "@CompetitionId", competitionId);

            insertCommand.Parameters.AddWithValue(
                "@UserId", userId);

            await insertCommand.ExecuteNonQueryAsync(
                cancellationToken);

            int newApplicationId =
                (int)insertCommand.LastInsertedId;

            // 6. 트랜잭션 완료
            await transaction.CommitAsync(cancellationToken);

            return newApplicationId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }


    // 참가 신청 목록 조회
    public async Task<List<CompetitionApplication>>
        GetApplicationsAsync(
            int competitionId,
            CancellationToken cancellationToken)
    {
        var applications = new List<CompetitionApplication>();

        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                a.application_id,
                a.competition_id,
                a.user_id,
                u.login_id,
                u.nickname,
                a.status,
                a.applied_at,
                a.processed_at
            FROM competition_applications a
            INNER JOIN users u
                ON a.user_id = u.user_id
            WHERE a.competition_id = @CompetitionId
            ORDER BY a.applied_at ASC";

        using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@CompetitionId", competitionId);

        using var reader = await command.ExecuteReaderAsync(
            cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            applications.Add(new CompetitionApplication
            {
                ApplicationId = reader.GetInt32("application_id"),
                CompetitionId = reader.GetInt32("competition_id"),
                UserId = reader.GetInt32("user_id"),
                LoginId = reader.GetString("login_id"),
                Nickname = reader.GetString("nickname"),
                Status = reader.GetString("status"),
                AppliedAt = reader.GetDateTime("applied_at"),
                ProcessedAt = reader.IsDBNull(
                    reader.GetOrdinal("processed_at"))
                    ? null
                    : reader.GetDateTime("processed_at")
            });
        }

        return applications;
    }

    // 참가 신청 승인 또는 거절
    public async Task ProcessApplicationAsync(int competitionId,int applicationId,bool approve,CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. 신청 정보 조회 및 잠금
            // 대회 번호와 신청 번호가 모두 일치해야 함
            const string selectSql = @"
                SELECT
                    a.competition_id,
                    a.user_id,
                    a.status,
                    c.initial_balance,
                    c.status AS competition_status
                FROM competition_applications a
                INNER JOIN competitions c
                    ON a.competition_id = c.competition_id
                WHERE a.application_id = @ApplicationId
                  AND a.competition_id = @CompetitionId
                FOR UPDATE";

            using var selectCommand = new MySqlCommand(
                selectSql, connection, transaction);

            selectCommand.Parameters.AddWithValue(
                "@ApplicationId", applicationId);

            selectCommand.Parameters.AddWithValue(
                "@CompetitionId", competitionId);

            int userId;
            decimal initialBalance;
            string applicationStatus;
            string competitionStatus;

            using (var reader = await selectCommand.ExecuteReaderAsync(
                cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "해당 대회의 참가 신청을 찾을 수 없습니다.");

                userId = reader.GetInt32("user_id");
                applicationStatus = reader.GetString("status");
                initialBalance = reader.GetDecimal("initial_balance");
                competitionStatus = reader.GetString("competition_status");
            }

            // 2. 대기 상태인지 확인
            if (applicationStatus != "대기")
                throw new InvalidOperationException(
                    "이미 처리된 참가 신청입니다.");

            if (competitionStatus != "준비")
                throw new InvalidOperationException(
                    "준비 상태의 대회만 신청을 처리할 수 있습니다.");

            // 3. 승인인 경우 실제 참가자 등록 및 계좌 생성
            if (approve)
            {
                const string participantSql = @"
                    INSERT INTO competition_participants
                    (
                        competition_id,
                        user_id,
                        joined_at
                    )
                    VALUES
                    (
                        @CompetitionId,
                        @UserId,
                        NOW(6)
                    )";

                using var participantCommand = new MySqlCommand(
                    participantSql, connection, transaction);

                participantCommand.Parameters.AddWithValue(
                    "@CompetitionId", competitionId);

                participantCommand.Parameters.AddWithValue(
                    "@UserId", userId);

                await participantCommand.ExecuteNonQueryAsync(
                    cancellationToken);

                int participantId =
                    (int)participantCommand.LastInsertedId;

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
                        @ParticipantId,
                        @InitialBalance,
                        @InitialBalance,
                        NOW(6)
                    )";

                using var accountCommand = new MySqlCommand(
                    accountSql, connection, transaction);

                accountCommand.Parameters.AddWithValue(
                    "@ParticipantId", participantId);

                accountCommand.Parameters.AddWithValue(
                    "@InitialBalance", initialBalance);

                await accountCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            // 4. 신청 상태 변경
            const string updateSql = @"
                UPDATE competition_applications
                SET
                    status = @Status,
                    processed_at = NOW(6)
                WHERE application_id = @ApplicationId
                  AND competition_id = @CompetitionId";

            using var updateCommand = new MySqlCommand(
                updateSql, connection, transaction);

            updateCommand.Parameters.AddWithValue(
                "@Status", approve ? "승인" : "거절");

            updateCommand.Parameters.AddWithValue(
                "@ApplicationId", applicationId);

            updateCommand.Parameters.AddWithValue(
                "@CompetitionId", competitionId);

            await updateCommand.ExecuteNonQueryAsync(
                cancellationToken);

            // 5. 트랜잭션 완료
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // 내 참가 신청 상태 조회
    public async Task<CompetitionApplication?> GetMyApplicationAsync(
        int competitionId,
        int userId,
        CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                application_id,
                competition_id,
                user_id,
                status,
                applied_at,
                processed_at
            FROM competition_applications
            WHERE competition_id = @CompetitionId
              AND user_id = @UserId";

        using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@CompetitionId", competitionId);

        command.Parameters.AddWithValue(
            "@UserId", userId);

        using var reader = await command.ExecuteReaderAsync(
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CompetitionApplication
        {
            ApplicationId = reader.GetInt32("application_id"),
            CompetitionId = reader.GetInt32("competition_id"),
            UserId = reader.GetInt32("user_id"),
            Status = reader.GetString("status"),
            AppliedAt = reader.GetDateTime("applied_at"),
            ProcessedAt = reader.IsDBNull(
                reader.GetOrdinal("processed_at"))
                ? null
                : reader.GetDateTime("processed_at")
        };
    }
}
