using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public class KisChartService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KisTokenService _tokenService;
    private readonly string _appKey;
    private readonly string _appSecret;

    public KisChartService(
        IHttpClientFactory httpClientFactory,
        KisTokenService tokenService,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;

        _appKey = configuration["Kis:AppKey"]
            ?? throw new InvalidOperationException(
                "AppKey 설정이 없습니다.");

        _appSecret = configuration["Kis:AppSecret"]
            ?? throw new InvalidOperationException(
                "AppSecret 설정이 없습니다.");
    }

    public async Task<List<CandleItem>> GetDailyAsync(
    string stockCode,
    CancellationToken cancellationToken,
    string range = "3M")
    {
        if (!Regex.IsMatch(stockCode, @"^[0-9]{6}$"))
        {
            throw new ArgumentException(
                "종목코드는 숫자 6자리여야 합니다.",
                nameof(stockCode));
        }

        string token = await _tokenService
            .GetAccessTokenAsync(cancellationToken);

        // 서버 PC 시간대와 관계없이 한국 날짜 사용
        DateTime today = DateTimeOffset.UtcNow
            .ToOffset(TimeSpan.FromHours(9))
            .Date;

        DateTime startDate = range switch
        {
            "1W" => today.AddDays(-7),
            "1M" => today.AddMonths(-1),
            "3M" => today.AddMonths(-3),
            "1Y" => today.AddYears(-1),
            "5Y" => today.AddYears(-5),

            _ => throw new ArgumentException(
                "지원하지 않는 조회 기간입니다.",
                nameof(range))
        };

        // 짧은 기간은 일봉, 긴 기간은 주봉·월봉
        string candlePeriod = range switch
        {
            "1Y" => "W",
            "5Y" => "M",
            _ => "D"
        };

        string start = startDate.ToString(
            "yyyyMMdd",
            CultureInfo.InvariantCulture);

        string end = today
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        string path =
            "uapi/domestic-stock/v1/quotations/"
            + "inquire-daily-itemchartprice"
            + "?FID_COND_MRKT_DIV_CODE=J"
            + $"&FID_INPUT_ISCD={Uri.EscapeDataString(stockCode)}"
            + $"&FID_INPUT_DATE_1={start}"
            + $"&FID_INPUT_DATE_2={end}"
            + $"&FID_PERIOD_DIV_CODE={candlePeriod}"
            + "&FID_ORG_ADJ_PRC=0";

        using var client =
            _httpClientFactory.CreateClient("Kis");

        using var request =
            new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add("appkey", _appKey);
        request.Headers.Add("appsecret", _appSecret);
        request.Headers.Add("tr_id", "FHKST03010100");
        request.Headers.Add("custtype", "P");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

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

            throw new InvalidOperationException(
                $"차트 시세 응답 오류. 코드: {code}");
        }

        if (!root.TryGetProperty("output2", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "차트 데이터 목록이 없습니다.");
        }

        var candles = new List<CandleItem>();

        foreach (JsonElement row in rows.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? dateText = ReadText(
                row, "stck_bsop_date");

            // 응답의 빈 행은 제외
            if (string.IsNullOrWhiteSpace(dateText))
            {
                continue;
            }

            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly date))
            {
                throw new InvalidOperationException(
                    "차트 날짜 형식이 올바르지 않습니다.");
            }

            decimal open = ReadPrice(row, "stck_oprc");
            decimal high = ReadPrice(row, "stck_hgpr");
            decimal low = ReadPrice(row, "stck_lwpr");
            decimal close = ReadPrice(row, "stck_clpr");

            // 거래 가격이 없는 행은 캔들로 표시하지 않음
            if (open == 0 && high == 0 && low == 0 && close == 0)
            {
                continue;
            }

            if (open <= 0
                || high <= 0
                || low <= 0
                || close <= 0
                || high < Math.Max(open, close)
                || low > Math.Min(open, close))
            {
                throw new InvalidOperationException(
                    "캔들 가격 범위가 올바르지 않습니다.");
            }

            if (!long.TryParse(
                    ReadText(row, "acml_vol"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long volume)
                || volume < 0)
            {
                throw new InvalidOperationException(
                    "거래량 형식이 올바르지 않습니다.");
            }

            candles.Add(new CandleItem
            {
                Date = date,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        if (candles.GroupBy(x => x.Date).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "중복된 일별 시세가 있습니다.");
        }

        // 차트 왼쪽은 과거, 오른쪽은 최근 날짜
        return candles.OrderBy(x => x.Date).ToList();
    }

    private static string? ReadText(
        JsonElement row,
        string name)
    {
        return row.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static decimal ReadPrice(
        JsonElement row,
        string name)
    {
        if (!decimal.TryParse(
                ReadText(row, name),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal price))
        {
            throw new InvalidOperationException(
                $"차트 가격 필드가 올바르지 않습니다: {name}");
        }

        return price;
    }
}