using Microsoft.AspNetCore.Identity;

namespace MockInvestment.Api.Services;

public class PasswordService
{
    private readonly PasswordHasher<object> _hasher = new();
    private readonly object _userContext = new();

    public string Hash(string password)
    {
        return _hasher.HashPassword(
            _userContext,
            password);
    }

    public PasswordVerificationResult Verify(
        string passwordHash,
        string password)
    {
        return _hasher.VerifyHashedPassword(
            _userContext,
            passwordHash,
            password);
    }
}