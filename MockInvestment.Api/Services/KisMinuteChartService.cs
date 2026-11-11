using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public sealed class KisMinuteChartService
{
    private readonly IHttpClientFactory _factory;
    private readonly KisTokenService _tokens;
    private readonly IMemoryCache _cache;
    private readonly string _appKey;
    private readonly string _appSecret;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly TimeSpan KoreaOffset =
        TimeSpan.FromHours(9);

    public KisMinuteChartService(
        IHttpClientFactory factory,
        KisTokenService tokens,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _factory = factory;
        _tokens = tokens;
        _cache = cache;

        _appKey = configuration["Kis:AppKey"]
            ?? throw new InvalidOperationException(
                "AppKey 설정이 없습니다.");

        _appSecret = configuration["Kis:AppSecret"]
            ?? throw new InvalidOperationException(
                "AppSecret 설정이 없습니다.");
    }

    private async Task<List<CandleItem>> GetDayAsync(string stockCode, DateOnly date, 
        int candleMinutes, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(stockCode, @"^[0-9]{6}$"))
        {
            throw new InvalidOperationException(
                "종목코드가 올바르지 않습니다.");
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow.ToOffset(KoreaOffset);

        DateOnly today = DateOnly.FromDateTime(now.Date);

        if (date > today || date < today.AddDays(-6))
        {
            throw new InvalidOperationException(
                "분봉 조회 범위를 벗어났습니다.");
        }

        if (date.DayOfWeek == DayOfWeek.Saturday
            || date.DayOfWeek == DayOfWeek.Sunday)
        {
            return new List<CandleItem>();
        }

        if (date == today
            && now.TimeOfDay < TimeSpan.FromHours(9))
        {
            return new List<CandleItem>();
        }

        string key = $"kis:minutes:{stockCode}:{date:yyyyMMdd}";

        await _gate.WaitAsync(cancellationToken);

        try
        {
            _cache.TryGetValue(key, out MinuteState? state);

            state ??= new MinuteState();

            if (state.IsFinal || DateTimeOffset.UtcNow - state.FetchedAt < TimeSpan.FromSeconds(10))
            {
                return Aggregate(state.Rows.Values, candleMinutes);
            }

            // 조회 실패·취소로 기존 캐시가 부분 수정되지 않도록 복사
            var working = new MinuteState();

            foreach (var pair in state.Rows)
            {
                working.Rows[pair.Key] = pair.Value;
            }

            state = working;

            var close = new DateTimeOffset(
                date.Year, date.Month, date.Day,
                15, 30, 0, KoreaOffset);

            DateTimeOffset cursor = now < close ? now : close;

            bool covered = false;

            // 120개씩 최대 5회. 정규장 하루 분량 확보
            for (int pageIndex = 0; pageIndex < 5; pageIndex++)
            {
                var page = await ReadPageAsync(
                    stockCode,
                    date,
                    cursor,
                    cancellationToken);

                if (page.Count == 0)
                {
                    covered = true;
                    break;
                }

                // 이전에 받은 구간까지 도달했는지 확인
                bool overlaps = page.Any(
                    x => state.Rows.ContainsKey(x.Time));

                foreach (var row in page)
                {
                    // 같은 시간은 덮어쓰기: 거래량 중복 합산 방지
                    state.Rows[row.Time] = row;
                }

                DateTimeOffset earliest =
                    page.Min(x => x.Time);

                if (overlaps
                    || earliest.TimeOfDay
                        <= TimeSpan.FromHours(9))
                {
                    covered = true;
                    break;
                }

                DateTimeOffset next =
                    earliest.AddMinutes(-1);

                if (next >= cursor)
                {
                    throw new InvalidOperationException(
                        "분봉의 이전 시간 조회가 진행되지 않았습니다.");
                }

                cursor = next;
            }

            if (!covered)
            {
                throw new InvalidOperationException(
                    "하루 전체 분봉을 확보하지 못했습니다.");
            }

            state.FetchedAt = DateTimeOffset.UtcNow;

            // 지난 날짜는 장 마감까지 조회한 결과를 재사용
            state.IsFinal = date < today;

            _cache.Set(
                key,
                state,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
                });

            return Aggregate(state.Rows.Values, candleMinutes);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<MinuteRow>> ReadPageAsync(
        string stockCode,
        DateOnly date,
        DateTimeOffset cursor,
        CancellationToken cancellationToken)
    {
        string token = await _tokens.GetAccessTokenAsync(
            cancellationToken);

        string path =
            "uapi/domestic-stock/v1/quotations/"
            + "inquire-time-dailychartprice"
            + "?FID_COND_MRKT_DIV_CODE=J"
            + $"&FID_INPUT_ISCD={Uri.EscapeDataString(stockCode)}"
            + $"&FID_INPUT_DATE_1={date:yyyyMMdd}"
            + $"&FID_INPUT_HOUR_1={cursor:HHmmss}"
            + "&FID_PW_DATA_INCU_YN=N"
            + "&FID_FAKE_TICK_INCU_YN=";

        using var client = _factory.CreateClient("Kis");

        using var request =
            new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add("appkey", _appKey);
        request.Headers.Add("appsecret", _appSecret);
        request.Headers.Add("tr_id", "FHKST03010230");
        request.Headers.Add("custtype", "P");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        string body = await response.Content
            .ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        if (!response.IsSuccessStatusCode
            || ReadText(root, "rt_cd") != "0")
        {
            throw new InvalidOperationException(
                "분봉 조회 실패. HTTP: "
                + (int)response.StatusCode
                + ", 코드: "
                + (ReadText(root, "msg_cd") ?? "UNKNOWN"));
        }

        if (!root.TryGetProperty("output2", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "분봉 응답에 데이터 목록이 없습니다.");
        }

        var result = new List<MinuteRow>();

        foreach (var row in rows.EnumerateArray())
        {
            string? dateText = ReadText(row, "stck_bsop_date");
            string? timeText = ReadText(row, "stck_cntg_hour");

            if (string.IsNullOrWhiteSpace(dateText)
                || string.IsNullOrWhiteSpace(timeText))
            {
                continue;
            }

            if (!DateTime.TryParseExact(
                dateText + timeText,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime localTime))
            {
                throw new InvalidOperationException(
                    "분봉 날짜·시간 형식이 올바르지 않습니다.");
            }

            var time = new DateTimeOffset(
                DateTime.SpecifyKind(
                    localTime, DateTimeKind.Unspecified),
                KoreaOffset);

            // 요청 날짜의 정규장 구간만 사용
            if (DateOnly.FromDateTime(localTime) != date
                || time > cursor
                || time.TimeOfDay < TimeSpan.FromHours(9)
                || time.TimeOfDay > new TimeSpan(15, 30, 0))
            {
                continue;
            }

            decimal open = ReadNumber(row, "stck_oprc");
            decimal high = ReadNumber(row, "stck_hgpr");
            decimal low = ReadNumber(row, "stck_lwpr");
            decimal close = ReadNumber(row, "stck_prpr");

            if (open == 0 && high == 0
                && low == 0 && close == 0)
            {
                continue;
            }

            if (open <= 0 || low <= 0 || close <= 0
                || high < Math.Max(open, close)
                || low > Math.Min(open, close))
            {
                throw new InvalidOperationException(
                    "분봉 가격 범위가 올바르지 않습니다.");
            }

            if (!long.TryParse(
                ReadText(row, "cntg_vol"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long volume)
                || volume < 0)
            {
                throw new InvalidOperationException(
                    "분봉 거래량이 올바르지 않습니다.");
            }

            result.Add(new MinuteRow(
                time, open, high, low, close, volume));
        }

        return result;
    }

    private static List<CandleItem> Aggregate(
    IEnumerable<MinuteRow> rows,
    int candleMinutes)
    {
        return rows
            .OrderBy(x => x.Time)
            .GroupBy(x => CandleStart(x.Time, candleMinutes))
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(x => x.Time)
                    .ToList();

                return new CandleItem
                {
                    Date = DateOnly.FromDateTime(
                        group.Key.DateTime),

                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                };
            })
            .OrderBy(x => x.Time)
            .ToList();
    }

    private static DateTimeOffset CandleStart(
     DateTimeOffset time,
     int candleMinutes)
    {
        return new DateTimeOffset(
            time.Year,
            time.Month,
            time.Day,
            time.Hour,
            time.Minute / candleMinutes * candleMinutes,
            0,
            KoreaOffset);
    }

    private static string? ReadText(
        JsonElement element,
        string name)
    {
        return element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static decimal ReadNumber(
        JsonElement element,
        string name)
    {
        if (!decimal.TryParse(
            ReadText(element, name),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal value))
        {
            throw new InvalidOperationException(
                $"분봉 숫자 형식 오류: {name}");
        }

        return value;
    }

    private sealed class MinuteState
    {
        public bool IsFinal { get; set; }

        public SortedDictionary<DateTimeOffset, MinuteRow>
            Rows
        { get; } = new();

        public DateTimeOffset FetchedAt { get; set; }
    }

    private sealed record MinuteRow(
        DateTimeOffset Time,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume);

    public Task<List<CandleItem>> GetTodayAsync(
    string stockCode,
    CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(KoreaOffset).Date);

        return GetDayAsync(
            stockCode,
            today,
            5,
            cancellationToken);
    }

    public async Task<List<CandleItem>> GetWeekAsync(
        string stockCode,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(KoreaOffset).Date);

        var result = new List<CandleItem>();

        // 오늘을 포함한 최근 7일: 주말 제외, 휴장일은 데이터 없음
        for (int offset = -6; offset <= 0; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candles = await GetDayAsync(
                stockCode,
                today.AddDays(offset),
                10,
                cancellationToken);

            result.AddRange(candles);
        }

        return result
            .OrderBy(x => x.Time)
            .ToList();
    }
}