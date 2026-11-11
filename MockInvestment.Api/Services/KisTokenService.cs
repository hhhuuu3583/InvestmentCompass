using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MockInvestment.Api.Services;

public class KisTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _appKey;
    private readonly string _appSecret;

    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt;
    private DateTimeOffset _lastAttemptAt =
        DateTimeOffset.MinValue;

    public KisTokenService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;

        _appKey = configuration["Kis:AppKey"]
            ?? throw new InvalidOperationException(
                "한국투자증권 AppKey 설정이 없습니다.");

        _appSecret = configuration["Kis:AppSecret"]
            ?? throw new InvalidOperationException(
                "한국투자증권 AppSecret 설정이 없습니다.");
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        // 여러 요청이 동시에 토큰을 발급하지 않도록 제한
        await _lock.WaitAsync(cancellationToken);

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // 만료까지 1분 이상 남으면 재사용
            if (!string.IsNullOrEmpty(_accessToken)
                && _expiresAt > now.AddMinutes(1))
            {
                return _accessToken;
            }

            // 실패 후에도 즉시 반복 발급하지 않도록 제한
            if (now - _lastAttemptAt < TimeSpan.FromMinutes(1))
            {
                throw new InvalidOperationException(
                    "한국투자증권 인증 요청 후 1분이 지나지 않았습니다.");
            }

            _lastAttemptAt = now;

            using var client =
                _httpClientFactory.CreateClient("Kis");

            using var response = await client.PostAsJsonAsync(
                "oauth2/tokenP",
                new
                {
                    grant_type = "client_credentials",
                    appkey = _appKey,
                    appsecret = _appSecret
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    "한국투자증권 인증 실패. HTTP 상태: "
                    + (int)response.StatusCode);
            }

            var token = await response.Content
                .ReadFromJsonAsync<KisTokenResponse>(
                    cancellationToken: cancellationToken);

            if (token is null
                || string.IsNullOrWhiteSpace(token.AccessToken)
                || token.ExpiresIn <= 60)
            {
                throw new InvalidOperationException(
                    "한국투자증권 인증 응답이 올바르지 않습니다.");
            }

            _accessToken = token.AccessToken;

            // 요청 시작 시각을 기준으로 보수적으로 계산
            _expiresAt = now.AddSeconds(token.ExpiresIn);

            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class KisTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}