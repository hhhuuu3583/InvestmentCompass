
using MySqlConnector;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Data;

public class CompetitionRepository
{
    private readonly IConfiguration _configuration;

    public CompetitionRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException(
                "MySQL 연결 문자열을 찾을 수 없습니다.");
    }

    // 대회 생성
    public async Task<int> CreateAsync(
        CreateCompetitionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO competitions
            (
                title,
                start_date,
                end_date,
                initial_balance,
                status,
                created_at
            )
            VALUES
            (
                @title,
                @startDate,
                @endDate,
                @initialBalance,
                '준비',
                CURRENT_TIMESTAMP(6)
            );
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@title", request.Title);
        command.Parameters.AddWithValue("@startDate", request.StartDate);
        command.Parameters.AddWithValue("@endDate", request.EndDate);
        command.Parameters.AddWithValue(
            "@initialBalance", request.InitialBalance);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return (int)command.LastInsertedId;
    }

    // 전체 대회 목록 조회
    public async Task<List<Competition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                competition_id,
                title,
                start_date,
                end_date,
                initial_balance,
                status,
                created_at
            FROM competitions
            ORDER BY created_at DESC;
        ";

        List<Competition> competitions = new();

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            competitions.Add(ReadCompetition(reader));
        }

        return competitions;
    }

    // 특정 대회 조회
    public async Task<Competition?> GetByIdAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                competition_id,
                title,
                start_date,
                end_date,
                initial_balance,
                status,
                created_at
            FROM competitions
            WHERE competition_id = @competitionId;
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadCompetition(reader);
    }

    // 대회 정보 수정
    public async Task<bool> UpdateAsync(
        int competitionId,
        CreateCompetitionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE competitions
            SET
                title = @title,
                start_date = @startDate,
                end_date = @endDate,
                initial_balance = @initialBalance
            WHERE competition_id = @competitionId
              AND status = '준비';
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@title", request.Title);
        command.Parameters.AddWithValue("@startDate", request.StartDate);
        command.Parameters.AddWithValue("@endDate", request.EndDate);
        command.Parameters.AddWithValue(
            "@initialBalance", request.InitialBalance);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    // 대회 상태 변경
    public async Task<bool> UpdateStatusAsync(
        int competitionId,
        string status,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE competitions
            SET status = @status
            WHERE competition_id = @competitionId;
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    // 거래를 차단하고 대회 최종 정산 시작
    // 대회 상태를 진행중에서 정산중으로 변경한다.
    public async Task<bool> TryBeginSettlementAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE competitions
            SET status = '정산중'
            WHERE competition_id = @competitionId
              AND status = '진행중';
            """;

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows == 1;
    }

    // 최종 순위 계산 또는 정산 실패 시 다시 진행중 상태로 복구
    public async Task ResumeAfterSettlementFailureAsync(
        int competitionId)
    {
        const string sql = """
            UPDATE competitions
            SET status = '진행중'
            WHERE competition_id = @competitionId
              AND status = '정산중';
            """;

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        await command.ExecuteNonQueryAsync();
    }

    // 대회 삭제
    // 참가자가 한 명도 없는 준비 상태의 대회만 삭제 가능
    public async Task<bool> DeleteAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM competitions
            WHERE competition_id = @competitionId
              AND status = '준비'
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM competition_participants
                  WHERE competition_id = @competitionId
              );
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    // DB 조회 결과를 Competition 객체로 변환
    private static Competition ReadCompetition(
        MySqlDataReader reader)
    {
        return new Competition
        {
            CompetitionId =
                reader.GetInt32("competition_id"),

            Title =
                reader.GetString("title"),

            StartDate =
                reader.GetDateTime("start_date"),

            EndDate =
                reader.GetDateTime("end_date"),

            InitialBalance =
                reader.GetDecimal("initial_balance"),

            Status =
                reader.GetString("status"),

            CreatedAt =
                reader.GetDateTime("created_at")
        };
    }

    // 대회 취소
    // 준비 또는 진행중 상태의 대회만 취소 가능
    public async Task<bool> CancelAsync(
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE competitions
            SET status = '취소'
            WHERE competition_id = @competitionId
              AND status IN ('준비', '진행중');
        ";

        await using var connection =
            new MySqlConnection(GetConnectionString());

        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@competitionId", competitionId);

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }
}
