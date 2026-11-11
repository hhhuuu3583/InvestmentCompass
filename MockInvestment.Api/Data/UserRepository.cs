using MySqlConnector;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Data;

public class UserRepository
{
    private readonly string _connectionString;

    public UserRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("MySQL 연결 설정이 없습니다.");
    }

    public async Task<int> CreateUserAsync(
        string loginId,
        string passwordHash,
        string nickname,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            // 1. 회원 저장
            await using var userCommand =
                connection.CreateCommand();

            userCommand.Transaction = transaction;

            userCommand.CommandText = """
                INSERT INTO users (
                    login_id,
                    password_hash,
                    nickname
                )
                VALUES (
                    @loginId,
                    @passwordHash,
                    @nickname
                );
                """;

            userCommand.Parameters.AddWithValue(
                "@loginId", loginId);

            userCommand.Parameters.AddWithValue(
                "@passwordHash", passwordHash);

            userCommand.Parameters.AddWithValue(
                "@nickname", nickname);

            await userCommand.ExecuteNonQueryAsync(
                cancellationToken);

            int userId = checked(
                (int)userCommand.LastInsertedId);

            // 2. 생성된 회원의 계좌 저장
            await using var accountCommand =
                connection.CreateCommand();

            accountCommand.Transaction = transaction;

            accountCommand.CommandText = """
                INSERT INTO accounts (
                    user_id,
                    initial_balance,
                    cash_balance
                )
                VALUES (
                    @userId,
                    @initialBalance,
                    @cashBalance
                );
                """;

            accountCommand.Parameters.AddWithValue(
                "@userId", userId);

            accountCommand.Parameters.AddWithValue(
                "@initialBalance", 100_000_000m);

            accountCommand.Parameters.AddWithValue(
                "@cashBalance", 100_000_000m);

            await accountCommand.ExecuteNonQueryAsync(
                cancellationToken);

            // 3. 회원과 계좌가 모두 저장되면 확정
            await transaction.CommitAsync(
                cancellationToken);

            return userId;
        }
        catch
        {
            // 요청이 취소되어도 변경 취소는 시도
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    public async Task<LoginUser?> FindByLoginIdAsync(
    string loginId,
    CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT
            user_id,
            login_id,
            nickname,
            password_hash,
            role
        FROM users
        WHERE login_id = @loginId
        LIMIT 1;
        """;

        command.Parameters.AddWithValue(
            "@loginId", loginId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoginUser
        {
            UserId = reader.GetInt32(0),
            LoginId = reader.GetString(1),
            Nickname = reader.GetString(2),
            PasswordHash = reader.GetString(3),
            Role = reader.GetString(4)
        };
    }
    public async Task<LoginUser?> FindByIdAsync(
    int userId,
    CancellationToken cancellationToken)
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT
            user_id,
            login_id,
            nickname,
            role
        FROM users
        WHERE user_id = @userId;
        """;

        command.Parameters.AddWithValue(
            "@userId", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoginUser
        {
            UserId = reader.GetInt32(0),
            LoginId = reader.GetString(1),
            Nickname = reader.GetString(2),
            Role = reader.GetString(3)
        };
    }
}