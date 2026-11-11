using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace MockInvestment.Api.Services;

public class TokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiresInMinutes;
    private readonly SigningCredentials _signingCredentials;

    public TokenService(IConfiguration configuration)
    {
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT 발급자 설정이 없습니다.");

        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT 사용 대상 설정이 없습니다.");

        string secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException(
                "JWT 비밀키 설정이 없습니다.");

        _expiresInMinutes =
            configuration.GetValue<int>("Jwt:ExpiresInMinutes");

        if (_expiresInMinutes <= 0)
        {
            throw new InvalidOperationException(
                "JWT 유효기간은 1분 이상이어야 합니다.");
        }

        byte[] keyBytes = Convert.FromBase64String(secretKey);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT 비밀키는 최소 32바이트여야 합니다.");
        }

        var securityKey = new SymmetricSecurityKey(keyBytes);

        _signingCredentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256);
    }

    public (string AccessToken, DateTime ExpiresAt) CreateToken(
        int userId,
        string role)
    {
        DateTime now = DateTime.UtcNow;
        DateTime expiresAt = now.AddMinutes(_expiresInMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                userId.ToString(CultureInfo.InvariantCulture)),

            new Claim(
                ClaimTypes.Role,
                role),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString()),

            new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now)
                    .ToUnixTimeSeconds()
                    .ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: _signingCredentials);

        string accessToken =
            new JwtSecurityTokenHandler().WriteToken(token);

        return (accessToken, expiresAt);
    }
}