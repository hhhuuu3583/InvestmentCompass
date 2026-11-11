namespace MockInvestment.Client.Models;

public class RegisterRequest
{
    public string LoginId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

public class RegisterResponse
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal CashBalance { get; set; }
}