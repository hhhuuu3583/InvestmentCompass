using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/database")]
public class DatabaseController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public DatabaseController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("test")]
    public async Task<IActionResult> TestConnection(
        CancellationToken cancellationToken)
    {
        string? connectionString =
            _configuration.GetConnectionString("MySql");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem(
                statusCode: 500,
                detail: "DB 연결 설정이 없습니다.");
        }

        await using var connection =
            new MySqlConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";

        object? result =
            await command.ExecuteScalarAsync(cancellationToken);

        return Ok(new
        {
            message = "MySQL 연결 성공",
            result = Convert.ToInt32(result)
        });
    }
}