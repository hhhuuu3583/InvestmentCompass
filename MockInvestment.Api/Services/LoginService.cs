using Microsoft.AspNetCore.Identity;
using MockInvestment.Api.Data;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class LoginService
{
    private readonly UserRepository _userRepository;
    private readonly PasswordService _passwordService;

    public LoginService(
        UserRepository userRepository,
        PasswordService passwordService)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
    }

    public async Task<LoginUser?> AuthenticateAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken)
    {
        // 1. 아이디로 회원 조회
        LoginUser? user =
            await _userRepository.FindByLoginIdAsync(
                loginId,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        // 2. DB 테스트 전용 계정은 로그인 차단
        if (user.PasswordHash == "TEST_ACCOUNT_LOGIN_DISABLED")
        {
            return null;
        }

        PasswordVerificationResult result;

        try
        {
            // 3. 저장된 해시와 입력 비밀번호 비교
            result = _passwordService.Verify(
                user.PasswordHash,
                password);
        }
        catch (FormatException)
        {
            // 정상적인 해시 형식이 아닌 경우 로그인 차단
            return null;
        }

        // 4. 비밀번호가 다르면 실패
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        // 5. 검증 성공 시 서버 내부에 회원 정보 전달
        return user;
    }
}