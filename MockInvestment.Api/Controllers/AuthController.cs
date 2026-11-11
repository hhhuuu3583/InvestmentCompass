using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;
using MySqlConnector;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserRepository _userRepository;
    private readonly PasswordService _passwordService;
    private readonly ILogger<AuthController> _logger;
    private readonly LoginService _loginService;
    private readonly TokenService _tokenService;

    public AuthController(
       UserRepository userRepository,
       PasswordService passwordService,
       ILogger<AuthController> logger,
       LoginService loginService,
       TokenService tokenService)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
        _logger = logger;
        _loginService = loginService;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        // 이전 단계의 입력 검증 규칙을 통과한 요청만 실행됩니다.
        string passwordHash =
            _passwordService.Hash(request.Password);

        try
        {
            int userId =
                await _userRepository.CreateUserAsync(
                    request.LoginId,
                    passwordHash,
                    request.Nickname.Trim(),
                    cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    userId,
                    nickname = request.Nickname.Trim(),
                    initialBalance = 100_000_000m,
                    cashBalance = 100_000_000m
                });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return Conflict(new
            {
                code = "DUPLICATE_LOGIN_ID",
                message = "이미 사용 중인 아이디입니다."
            });
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "회원가입 DB 오류. MySQL 오류 번호: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "회원가입 처리 중 DB 오류가 발생했습니다.");
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
    [FromBody] LoginRequest request,
    CancellationToken cancellationToken)
    {
        LoginUser? user;

        try
        {
            user = await _loginService.AuthenticateAsync(
                request.LoginId,
                request.Password,
                cancellationToken);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(
                "로그인 DB 오류. MySQL 오류 번호: {ErrorNumber}",
                ex.Number);

            return Problem(
                statusCode: 500,
                detail: "로그인 처리 중 DB 오류가 발생했습니다.");
        }

        if (user is null)
        {
            return Unauthorized(new
            {
                code = "INVALID_CREDENTIALS",
                message = "아이디 또는 비밀번호가 올바르지 않습니다."
            });
        }

        var token = _tokenService.CreateToken(
                    user.UserId,
                    user.Role);

        return Ok(new
        {
            accessToken = token.AccessToken,
            tokenType = "Bearer",
            expiresAt = token.ExpiresAt,
            user = new
            {
                userId = user.UserId,
                nickname = user.Nickname,
                role = user.Role
            }
        });
    }
}