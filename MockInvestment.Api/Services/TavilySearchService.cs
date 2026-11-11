using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public sealed class TavilySearchService
{
    // 여러 자료를 비교할 수 있도록 최대 3개 검색
    private const int MaxResults = 3;

    // 학교 AI에 전달하는 출처별 최대 글자 수
    private const int MaxContentLength = 1400;

    // 본문 선택 과정에서 살펴볼 최대 글자 수
    private const int MaxBodyScanLength = 60000;

    // 본문을 나누는 구간 크기와 겹치는 부분
    private const int ChunkLength = 420;
    private const int ChunkOverlap = 80;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public TavilySearchService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<List<ResearchSource>> SearchAsync(
        string query,
        CancellationToken cancellationToken,
        string[]? includeDomains = null)
    {
        query = (query ?? "").Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException(
                "검색어를 입력해주세요.");
        }

        if (query.Length > 400)
        {
            throw new ArgumentException(
                "검색어는 400자 이내로 입력해주세요.");
        }

        string? apiKey = _configuration["Tavily:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Tavily API 키가 설정되지 않았습니다.");
        }

        apiKey = apiKey.Trim();

        if (apiKey.Any(c => c <= 32 || c > 126))
        {
            throw new InvalidOperationException(
                "Tavily API 키에 공백이나 잘못된 문자가 있습니다.");
        }

        string[] domains = NormalizeDomains(includeDomains);

        var payload = new Dictionary<string, object>
        {
            ["query"] = query,
            ["search_depth"] = "basic",
            ["topic"] = "general",
            ["max_results"] = MaxResults,
            ["include_answer"] = false,

            // 검색 결과 페이지의 본문도 요청
            ["include_raw_content"] = "text",

            ["include_published_date"] = true,
            ["auto_parameters"] = false
        };

        // 목록을 전달하지 않으면 사이트 제한 없이 검색
        if (domains.Length > 0)
        {
            payload["include_domains"] = domains;
            payload["include_domains_mode"] = "restrict";
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.tavily.com/search");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        request.Content = JsonContent.Create(payload);

        var client = _httpClientFactory.CreateClient("Tavily");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Tavily 검색 실패: HTTP {(int)response.StatusCode}",
                null,
                response.StatusCode);
        }

        using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(
                "results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "검색 결과 형식을 확인할 수 없습니다.");
        }

        var sources = new List<ResearchSource>();

        var seenUrls = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var item in results.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string url = ReadString(item, "url");

            if (!Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps
                    && uri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                continue;
            }

            if (!IsAllowedDomain(uri, domains))
            {
                continue;
            }

            // fragment(#...)를 제외한 주소로 중복 확인
            string normalizedUrl =
                uri.GetLeftPart(UriPartial.Query);

            if (seenUrls.Contains(normalizedUrl))
            {
                continue;
            }

            string title = ReadString(item, "title");

            string excerpt = NormalizeText(
                ReadString(item, "content"));

            string rawContent = NormalizeText(
                ReadString(item, "raw_content"));

            string content = BuildEvidence(
                query,
                excerpt,
                rawContent);

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            string publishedDate =
                ReadString(item, "published_date");

            seenUrls.Add(normalizedUrl);

            sources.Add(new ResearchSource
            {
                Title = string.IsNullOrWhiteSpace(title)
                    ? uri.Host
                    : Limit(title.Trim(), 300),

                Url = normalizedUrl,

                Content = content,

                // Tavily가 추정한 게시·수정 날짜일 수 있음
                PublishedDate =
                    string.IsNullOrWhiteSpace(publishedDate)
                        ? null
                        : Limit(publishedDate.Trim(), 100)
            });

            if (sources.Count >= MaxResults)
            {
                break;
            }
        }

        return sources;
    }

    private static string BuildEvidence(
        string query,
        string excerpt,
        string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            if (string.IsNullOrWhiteSpace(excerpt))
            {
                return "";
            }

            const string label =
                "[본문 미확보: 검색 발췌문만 사용]\n";

            return label + Limit(
                excerpt,
                MaxContentLength - label.Length);
        }

        string selectedBody = SelectRelevantBody(
            rawContent,
            query);

        // 검색 발췌문도 보조 자료로 유지
        string excerptSection =
            string.IsNullOrWhiteSpace(excerpt)
                ? ""
                : "[검색 발췌문]\n"
                    + Limit(excerpt, 300)
                    + "\n\n";

        const string bodyLabel =
            "[확보한 본문의 선택 구간 — 중간 내용 생략 가능]\n";

        string prefix = excerptSection + bodyLabel;

        int remaining =
            Math.Max(0, MaxContentLength - prefix.Length);

        return prefix + Limit(selectedBody, remaining);
    }

    private static string SelectRelevantBody(
        string body,
        string query)
    {
        body = Limit(body, MaxBodyScanLength);

        string[] keywords = ExtractKeywords(query);

        var chunks = new List<
            (int Index, string Text, int Score)>();

        int start = 0;
        int index = 0;

        while (start < body.Length)
        {
            int end = Math.Min(
                start + ChunkLength,
                body.Length);

            // UTF-16 서로게이트 쌍 중간에서 자르지 않음
            if (end < body.Length
                && end > start
                && char.IsHighSurrogate(body[end - 1])
                && char.IsLowSurrogate(body[end]))
            {
                end--;
            }

            string text = body[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                int score = keywords.Count(keyword =>
                    text.Contains(
                        keyword,
                        StringComparison.OrdinalIgnoreCase));

                chunks.Add((index, text, score));
            }

            if (end >= body.Length)
            {
                break;
            }

            start = Math.Max(
                start + 1,
                end - ChunkOverlap);

            if (start > 0
                && char.IsLowSurrogate(body[start])
                && char.IsHighSurrogate(body[start - 1]))
            {
                start++;
            }

            index++;
        }

        // 관련 단어가 많은 구간을 최대 2개 선택.
        // 모두 0점이면 앞쪽 구간을 선택.
        var selected = chunks
            .OrderByDescending(chunk => chunk.Score)
            .ThenBy(chunk => chunk.Index)
            .Take(2)
            .OrderBy(chunk => chunk.Index)
            .Select(chunk => chunk.Text);

        return string.Join(
            "\n\n[다음 선택 구간]\n\n",
            selected);
    }

    private static string[] ExtractKeywords(string query)
    {
        var stopWords = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "최근",
            "관련",
            "대한",
            "대해서",
            "이유",
            "기사를",
            "기사",
            "찾아",
            "찾고",
            "찾아줘",
            "알려줘",
            "설명해줘",
            "분석해줘",
            "요약해줘",
            "한국어로",
            "출처",
            "포함해",
            "문장으로"
        };

        return Regex.Matches(
                query,
                @"[\p{L}\p{N}]{2,}")
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(word => !stopWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private static string[] NormalizeDomains(
        string[]? includeDomains)
    {
        string[] domains =
            (includeDomains ?? Array.Empty<string>())
            .Where(domain =>
                !string.IsNullOrWhiteSpace(domain))
            .Select(domain =>
                domain.Trim()
                    .TrimEnd('.')
                    .ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string domain in domains)
        {
            // https://... 또는 경로가 아닌 도메인만 허용
            if (Uri.CheckHostName(domain)
                    != UriHostNameType.Dns
                || !domain.Contains('.'))
            {
                throw new ArgumentException(
                    "검색 도메인은 samsung.com처럼 입력해주세요.");
            }
        }

        return domains;
    }

    private static bool IsAllowedDomain(
        Uri uri,
        string[] domains)
    {
        if (domains.Length == 0)
        {
            return true;
        }

        return domains.Any(domain =>
            uri.Host.Equals(
                domain,
                StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(
                "." + domain,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string ReadString(
        JsonElement item,
        string propertyName)
    {
        return item.TryGetProperty(
                propertyName,
                out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    private static string Limit(
        string value,
        int length)
    {
        if (length <= 0)
        {
            return "";
        }

        if (value.Length <= length)
        {
            return value;
        }

        int end = length;

        if (char.IsHighSurrogate(value[end - 1])
            && char.IsLowSurrogate(value[end]))
        {
            end--;
        }

        return value[..end];
    }
}