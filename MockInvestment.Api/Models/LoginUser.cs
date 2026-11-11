namespace MockInvestment.Api.Models;

public class LoginUser
{
    public int UserId { get; set; }

    public string LoginId { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    // 회원 권한
    public string Role { get; set; } = "User";
}