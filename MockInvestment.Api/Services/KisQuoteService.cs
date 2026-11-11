using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MockInvestment.Api.Services;

public class KisQuoteService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KisTokenService _tokenService;
    private readonly string _appKey;
    private readonly string _appSecret;
    private readonly ILogger<KisQuoteService> _logger;

    public KisQuoteService(
    IHttpClientFactory httpClientFactory,
    KisTokenService tokenService,
    IConfiguration configuration,
    ILogger<KisQuoteService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
        _logger = logger;

        _appKey = configuration["Kis:AppKey"]
            ?? throw new InvalidOperationException("AppKey 설정이 없습니다.");

        _appSecret = configuration["Kis:AppSecret"]
            ?? throw new InvalidOperationException("AppSecret 설정이 없습니다.");
    }

    public async Task<(decimal Price, DateTimeOffset FetchedAt)>
        GetQuoteAsync(
            string stockCode,
            CancellationToken cancellationToken)
    {
        string token = await _tokenService
            .GetAccessTokenAsync(cancellationToken);

        using var client =
            _httpClientFactory.CreateClient("Kis");

        string path =
            "uapi/domestic-stock/v1/quotations/inquire-price"
            + "?FID_COND_MRKT_DIV_CODE=J"
            + "&FID_INPUT_ISCD="
            + Uri.EscapeDataString(stockCode);

        using var request =
            new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add("appkey", _appKey);
        request.Headers.Add("appsecret", _appSecret);
        request.Headers.Add("tr_id", "FHKST01010100");
        request.Headers.Add("custtype", "P");

        using var response =
            await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string externalCode = "UNKNOWN";

            try
            {
                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                using var errorDocument = JsonDocument.Parse(body);

                if (errorDocument.RootElement.ValueKind
                        == JsonValueKind.Object
                    && errorDocument.RootElement.TryGetProperty(
                        "msg_cd", out var codeElement)
                    && codeElement.ValueKind == JsonValueKind.String)
                {
                    string? value = codeElement.GetString();

                    // 오류 코드만 기록
                    if (!string.IsNullOrEmpty(value)
                        && value.Length <= 40
                        && value.All(c => char.IsAsciiLetterOrDigit(c)
                            || c == '_'
                            || c == '-'))
                    {
                        externalCode = value;
                    }
                }
            }
            catch (JsonException)
            {
                // JSON이 아닌 오류 응답도 HTTP 상태는 기록
            }

            _logger.LogWarning(
                "KIS 현재가 조회 실패: 종목={StockCode}, HTTP={Status}, 코드={Code}",
                stockCode,
                (int)response.StatusCode,
                externalCode);

            throw new HttpRequestException(
                $"현재가 조회 실패. HTTP: {(int)response.StatusCode}, "
                + $"코드: {externalCode}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("rt_cd", out var resultCode)
            || resultCode.GetString() != "0")
        {
            string code =
                root.TryGetProperty("msg_cd", out var messageCode)
                ? messageCode.GetString() ?? "UNKNOWN"
                : "UNKNOWN";

            _logger.LogWarning("KIS 현재가 응답 오류: 종목={StockCode}, 코드={Code}", 
                stockCode, code);

            throw new InvalidOperationException(
                $"한국투자증권 시세 응답 오류. 코드: {code}");
        }

        if (!root.TryGetProperty("output", out var output)
            || !output.TryGetProperty(
                "stck_prpr", out var priceElement)
            || !decimal.TryParse(
                priceElement.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal price)
            || price <= 0)
        {
            throw new InvalidOperationException(
                "유효한 현재가를 받지 못했습니다.");
        }

        return (price, DateTimeOffset.UtcNow);
    }
}