namespace MockInvestment.Client.Models;

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public LoginUserResponse User { get; set; } = new();
}

public class LoginUserResponse
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}