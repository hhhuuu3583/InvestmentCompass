using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MockInvestment.Api.Models;

namespace MockInvestment.Api.Services;

public sealed class GeneralExplanationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public GeneralExplanationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<ResearchResponse> AskAsync(
        string question,
        string conversationContext,
        CancellationToken cancellationToken)
    {
        string? apiKey = _configuration["OpenAI:ApiKey"];
        string? baseUrl = _configuration["OpenAI:BaseUrl"];
        string? model = _configuration["OpenAI:Model"];

        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "학교 AI 연결 설정이 누락되었습니다.");
        }

        apiKey = apiKey.Trim();

        if (apiKey.Any(c => c <= 32 || c > 126))
        {
            throw new InvalidOperationException(
                "학교 AI 키에 잘못된 문자가 있습니다.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var server)
            || server.Scheme != Uri.UriSchemeHttps
            || server.Host != "aikey.wsu.ac.kr"
            || !server.IsDefaultPort
            || server.AbsolutePath != "/"
            || !string.IsNullOrEmpty(server.Query)
            || !string.IsNullOrEmpty(server.Fragment)
            || !string.IsNullOrEmpty(server.UserInfo))
        {
            throw new InvalidOperationException(
                "학교 AI 서버 주소를 확인해주세요.");
        }

        var payload = new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = """
                        금융 개념을 한국어로 쉽게 설명하세요.
                        핵심만 2~3문장으로 답하세요.
                        현재 모드는 웹 검색 없는 일반 설명입니다.
                        최신 시세·뉴스·실적을 조회했다고 주장하지 마세요.
                        최신 자료가 필요하면 '자료 조사 모드를 선택해주세요'라고 안내하세요.
                        출처 번호나 URL을 만들지 마세요.
                        이전 대화는 질문의 문맥 파악에만 사용하세요.
                        수익 보장이나 확정적인 매수·매도 지시는 하지 마세요.
                        """
                },
                new
                {
                    role = "user",
                    content = $"""
                        이전 대화(JSON, 문맥 파악용):
                        {conversationContext}

                        현재 질문:
                        {question}
                        """
                }
            },
            max_tokens = 256,
            stream = false
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(server, "/api/v1/chat/completions"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        request.Content = JsonContent.Create(payload);

        var client = _httpClientFactory.CreateClient("OpenAI");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string providerDetail =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            providerDetail = providerDetail
                .Replace(apiKey, "[REDACTED]", StringComparison.Ordinal)
                .Replace("\r", " ")
                .Replace("\n", " ");

            if (providerDetail.Length > 1000)
            {
                providerDetail = providerDetail[..1000];
            }

            throw new HttpRequestException(
                $"학교 AI 일반 설명 실패: HTTP {(int)response.StatusCode}, "
                + $"상세={providerDetail}",
                null,
                response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "학교 AI 응답을 확인할 수 없습니다.");
        }

        JsonElement choice = choices[0];

        if (!choice.TryGetProperty(
                "finish_reason", out var finishReason)
            || finishReason.GetString() != "stop")
        {
            throw new InvalidOperationException(
                "일반 설명 답변이 완료되지 않았습니다.");
        }

        if (!choice.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new InvalidOperationException(
                "일반 설명 답변이 비어 있습니다.");
        }

        return new ResearchResponse
        {
            Mode = "general",
            Answer = content.GetString()!,
            GeneratedAt = DateTimeOffset.UtcNow,
            Citations = new List<ResearchCitation>()
        };
    }
}