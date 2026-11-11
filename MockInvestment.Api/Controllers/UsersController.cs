using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MockInvestment.Api.Data;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserRepository _userRepository;

    public UsersController(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(
        CancellationToken cancellationToken)
    {
        string? subject =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!int.TryParse(
                subject,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            || userId <= 0)
        {
            return Unauthorized();
        }

        // 아래에서 추가할 메서드로 현재 회원 조회
        var user = await _userRepository.FindByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            userId = user.UserId,
            loginId = user.LoginId,
            nickname = user.Nickname
        });
    }
}