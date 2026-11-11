using System.Net;
using System.Net.Http.Json;
using MockInvestment.Client.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MockInvestment.Client.Services;

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://localhost:7090/"),
        
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly HttpClient _chartHttpClient = new()
    {
        BaseAddress = new Uri("https://localhost:7090/"),
        
        Timeout = TimeSpan.FromSeconds(90)
    };

    private readonly HttpClient _researchHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(240)
    };

    public async Task<LoginResponse> LoginAsync(
        string loginId,
        string password)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/login",
            new
            {
                loginId,
                password
            });

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "아이디 또는 비밀번호가 올바르지 않습니다.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException(
                "아이디와 비밀번호의 입력 내용을 확인해주세요.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"서버 요청에 실패했습니다. 상태 코드: {(int)response.StatusCode}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<LoginResponse>();

        if (result is null
            || string.IsNullOrWhiteSpace(result.AccessToken)
            || result.User is null
            || result.User.UserId <= 0
            || result.ExpiresAt <= DateTimeOffset.UtcNow
            || !string.Equals(
                result.TokenType,
                "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "서버의 로그인 응답이 올바르지 않습니다.");
        }

        return result;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _chartHttpClient.Dispose();
        _researchHttpClient.Dispose();
    }

    public async Task<List<StockItem>> GetStocksAsync(
    string accessToken,
    string keyword,
    CancellationToken cancellationToken)
    {
        string path =
            "api/stocks?keyword="
            + Uri.EscapeDataString(keyword);

        using var request =
            new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 창을 닫고 다시 로그인해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<StockListResponse>(
                cancellationToken: cancellationToken);

        if (result is null || result.Items is null)
        {
            throw new InvalidOperationException(
                "종목 목록 응답이 올바르지 않습니다.");
        }

        return result.Items;
    }

    public async Task<QuoteResponse> GetQuoteAsync(
    string accessToken,
    int stockId,
    CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/stocks/{stockId}/quote");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException(
                "시세 조회를 지원하지 않는 종목입니다.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "거래 지원 종목을 찾을 수 없습니다.");
        }

        response.EnsureSuccessStatusCode();

        var quote = await response.Content
            .ReadFromJsonAsync<QuoteResponse>(
                cancellationToken: cancellationToken);

        if (quote is null
            || quote.StockId != stockId
            || quote.CurrentPrice <= 0)
        {
            throw new InvalidOperationException(
                "현재가 응답이 올바르지 않습니다.");
        }

        return quote;
    }

    public async Task<TradeResponse> SendTradeAsync(
    string accessToken,
    bool isBuy,
    TradeRequest order,
    CancellationToken cancellationToken)
    {
        string path = isBuy
            ? "api/trades/buy"
            : "api/trades/sell";

        using var request =
            new HttpRequestMessage(HttpMethod.Post, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        // Content-Type: application/json도 함께 설정됨
        request.Content = JsonContent.Create(order);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string message =
                $"주문 요청에 실패했습니다. 상태 코드: {(int)response.StatusCode}";

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                message = "로그인이 만료되었습니다. 다시 로그인해주세요.";
            }
            else
            {
                // 서버가 반환한 거래 거부 사유 읽기
                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                try
                {
                    using var document = JsonDocument.Parse(body);
                    JsonElement root = document.RootElement;

                    if (root.TryGetProperty(
                            "message", out var messageElement)
                        && messageElement.ValueKind
                            == JsonValueKind.String)
                    {
                        message = messageElement.GetString()
                            ?? message;
                    }
                    else if (response.StatusCode
                             == HttpStatusCode.BadRequest)
                    {
                        message =
                            "종목·수량·주문번호의 입력 내용을 확인해주세요.";
                    }
                }
                catch (JsonException)
                {
                    // JSON이 아니면 기본 안내 유지
                }
            }

            throw new HttpRequestException(
                message,
                inner: null,
                statusCode: response.StatusCode);
        }

        var result = await response.Content
            .ReadFromJsonAsync<TradeResponse>(
                cancellationToken: cancellationToken);

        string expectedType = isBuy ? "BUY" : "SELL";

        if (result is null
            || result.TradeId <= 0
            || result.ClientOrderId != order.ClientOrderId
            || result.StockId != order.StockId
            || result.Quantity != order.Quantity
            || result.TradeType != expectedType
            || result.UnitPrice <= 0
            || result.TotalAmount != result.UnitPrice * result.Quantity)
        {
            throw new InvalidOperationException(
                "주문 응답을 확인하지 못했습니다. 새 주문을 보내기 전에 거래 결과를 확인해주세요.");
        }

        return result;
    }

    public async Task<PortfolioResponse> GetPortfolioAsync(
    string accessToken,
    CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "api/portfolio");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "가상 계좌를 찾을 수 없습니다.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<PortfolioResponse>(
                cancellationToken: cancellationToken);

        if (result is null || result.Holdings is null)
        {
            throw new InvalidOperationException(
                "자산 응답이 올바르지 않습니다.");
        }

        return result;
    }

    public async Task<TradeHistoryResponse> GetTradeHistoryAsync(
    string accessToken,
    int page,
    int pageSize,
    CancellationToken cancellationToken)
    {
        string path =
            $"api/trades?page={page}&pageSize={pageSize}";

        using var request =
            new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<TradeHistoryResponse>(
                cancellationToken: cancellationToken);

        if (result is null
            || result.Items is null
            || result.Page != page
            || result.PageSize != pageSize
            || result.TotalCount < 0)
        {
            throw new InvalidOperationException(
                "거래 내역 응답이 올바르지 않습니다.");
        }

        return result;
    }

    public async Task<RankingResponse> GetRankingsAsync(
    string accessToken,
    CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "api/rankings");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new InvalidOperationException(
                "시세를 확보하지 못해 순위를 계산할 수 없습니다. 잠시 후 다시 조회해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<RankingResponse>(
                cancellationToken: cancellationToken);

        if (result is null || result.Items is null)
        {
            throw new InvalidOperationException(
                "순위 응답이 올바르지 않습니다.");
        }

        return result;
    }

    public async Task<RegisterResponse> RegisterAsync(
    RegisterRequest registration)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/register",
            registration);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                "이미 사용 중인 아이디입니다.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            string body = await response.Content.ReadAsStringAsync();

            var messages = new List<string>();

            try
            {
                using var document = JsonDocument.Parse(body);

                if (document.RootElement.TryGetProperty(
                        "errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in errors.EnumerateObject())
                    {
                        if (property.Value.ValueKind
                            != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String
                                && item.GetString() is string message)
                            {
                                messages.Add(message);
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 서버 응답 형식이 다르면 기본 안내 사용
            }

            throw new InvalidOperationException(
                messages.Count > 0
                    ? string.Join(Environment.NewLine, messages.Distinct())
                    : "회원가입 입력 내용을 확인해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<RegisterResponse>();

        if (result is null || result.UserId <= 0)
        {
            throw new InvalidOperationException(
                "가입 응답을 확인하지 못했습니다. 로그인 가능 여부를 확인해주세요.");
        }

        return result;
    }

    public async Task<CandleResponse> GetCandlesAsync(
    string accessToken,
    int stockId,
    CancellationToken cancellationToken,
    string range = "3M")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/stocks/{stockId}/candles"
            + $"?range={Uri.EscapeDataString(range)}"); ;

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _chartHttpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<CandleResponse>(
                cancellationToken: cancellationToken);

        if (result is null
            || result.Items is null
            || result.StockId != stockId)
        {
            throw new InvalidOperationException(
                "차트 응답이 올바르지 않습니다.");
        }

        return result;
    }
    // 종료된 대회의 최종 순위 조회
    public async Task<CompetitionFinalRankingListResponse> GetCompetitionFinalRankingsAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
            throw new ArgumentException("올바르지 않은 대회 번호입니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/competitions/{competitionId}/final-rankings");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string message = $"최종 순위 조회에 실패했습니다. 상태 코드: {(int)response.StatusCode}";
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("message", out var messageElement))
                    message = messageElement.GetString() ?? message;
            }
            catch (JsonException) { }

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<CompetitionFinalRankingListResponse>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (result == null || !result.Finalized || result.Rankings == null)
            throw new InvalidOperationException("최종 순위 응답이 올바르지 않습니다.");

        return result;
    }

    // 대회 전체 목록 조회
    public async Task<List<Competition>> GetCompetitionsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "api/competitions");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<List<Competition>>(
                cancellationToken: cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException(
                "대회 목록 응답이 올바르지 않습니다.");
        }

        return result;
    }


    // 대회 상세 조회
    public async Task<Competition> GetCompetitionAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        if (competitionId <= 0)
            throw new ArgumentException("올바르지 않은 대회 번호입니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/competitions/{competitionId}");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "대회를 찾을 수 없습니다.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<Competition>(
                cancellationToken: cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException(
                "대회 상세 정보 응답이 올바르지 않습니다.");
        }

        return result;
    }

    // 관리자 대회 등록
    public async Task CreateCompetitionAsync(
        string accessToken,
        CompetitionRequest request,
        CancellationToken cancellationToken = default)
    {
        await SendAdminRequestAsync(
            HttpMethod.Post,
            "api/competitions",
            accessToken,
            request,
            cancellationToken);
    }

    // 관리자 대회 수정
    public async Task UpdateCompetitionAsync(
        string accessToken,
        int competitionId,
        CompetitionRequest request,
        CancellationToken cancellationToken = default)
    {
        await SendAdminRequestAsync(
            HttpMethod.Put,
            $"api/competitions/{competitionId}",
            accessToken,
            request,
            cancellationToken);
    }

    // 관리자 대회 삭제
    public async Task DeleteCompetitionAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        await SendAdminRequestAsync(
            HttpMethod.Delete,
            $"api/competitions/{competitionId}",
            accessToken,
            null,
            cancellationToken);
    }

    // 관리자 대회 시작
    public async Task StartCompetitionAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        await SendAdminRequestAsync(
            HttpMethod.Post,
            $"api/competitions/{competitionId}/start",
            accessToken,
            null,
            cancellationToken);
    }

    // 관리자 대회 종료
    public async Task EndCompetitionAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        await SendAdminRequestAsync(
            HttpMethod.Post,
            $"api/competitions/{competitionId}/end",
            accessToken,
            null,
            cancellationToken);
    }

    // 관리자 대회 취소
    public async Task CancelCompetitionAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        await SendAdminRequestAsync(
            HttpMethod.Post,
            $"api/competitions/{competitionId}/cancel",
            accessToken,
            null,
            cancellationToken);
    }

    // 관리자 API 공통 요청 처리
    private async Task SendAdminRequestAsync(
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string message =
                $"요청에 실패했습니다. 상태 코드: {(int)response.StatusCode}";

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                message = "로그인이 만료되었습니다. 다시 로그인해주세요.";
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                message = "관리자 권한이 필요합니다.";
            }
            else
            {
                string responseBody =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                try
                {
                    using var document = JsonDocument.Parse(responseBody);

                    if (document.RootElement.TryGetProperty(
                            "message", out var messageElement)
                        && messageElement.ValueKind == JsonValueKind.String)
                    {
                        message = messageElement.GetString() ?? message;
                    }
                }
                catch (JsonException)
                {
                    // JSON이 아니면 기본 오류 메시지 사용
                }
            }

            throw new HttpRequestException(
                message,
                null,
                response.StatusCode);
        }
    }
    // 대회 참가 신청
    public async Task<string> ApplyCompetitionAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/competitions/{competitionId}/applications");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"참가 신청 실패: {responseBody}");
        }

        using var json = JsonDocument.Parse(responseBody);

        return json.RootElement
            .GetProperty("message")
            .GetString() ?? "참가 신청이 완료되었습니다.";
    }

    // 대회 참가 신청자 목록 조회 (관리자)
    public async Task<List<CompetitionApplication>> GetCompetitionApplicationsAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/competitions/{competitionId}/applications");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string message = "참가 신청자 목록을 불러오지 못했습니다.";

            try
            {
                string json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("message", out var msg))
                {
                    message = msg.GetString() ?? message;
                }
            }
            catch
            {
            }

            throw new HttpRequestException(
                message,
                null,
                response.StatusCode);
        }

        // 서버 JSON 응답 읽기
        string responseJson =
            await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(responseJson);

        // applications 배열 추출
        if (!document.RootElement.TryGetProperty(
            "applications",
            out JsonElement applicationsElement))
        {
            throw new InvalidOperationException(
                "서버 응답에 applications 데이터가 없습니다.");
        }

        // applications 배열을 모델 목록으로 변환
        List<CompetitionApplication>? applications =
            JsonSerializer.Deserialize<List<CompetitionApplication>>(
                applicationsElement.GetRawText(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        return applications ?? new List<CompetitionApplication>();
    }


    // 참가 신청 승인 (관리자)
    public Task ApproveCompetitionApplicationAsync(
        string accessToken,
        int competitionId,
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        string path =
            $"api/competitions/{competitionId}/applications/{applicationId}/approve";

        return SendAdminRequestAsync(
            HttpMethod.Post,
            path,
            accessToken,
            null,
            cancellationToken);
    }

    // 참가 신청 거절 (관리자)
    public Task RejectCompetitionApplicationAsync(
        string accessToken,
        int competitionId,
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        string path =
            $"api/competitions/{competitionId}/applications/{applicationId}/reject";

        return SendAdminRequestAsync(
            HttpMethod.Post,
            path,
            accessToken,
            null,
            cancellationToken);
    }
    // 내 대회 참가 신청 상태 조회
    public async Task<CompetitionApplication?> GetMyCompetitionApplicationAsync(
        string accessToken,
        int competitionId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/competitions/{competitionId}/applications/my");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        // 신청 내역이 없는 경우
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<CompetitionApplication>(
                cancellationToken: cancellationToken);

        return result;
    }
    public async Task<ActiveCompetitionResponse> GetMyActiveCompetitionAsync(
    string accessToken,
    CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "api/competitions/my/active");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ActiveCompetitionResponse>(
                cancellationToken: cancellationToken);

        if (result is null)
        {
            throw new InvalidOperationException(
                "진행 중인 대회 응답이 올바르지 않습니다.");
        }

        if (result.IsParticipating
            && (result.Competition is null
                || result.Competition.CompetitionId <= 0))
        {
            throw new InvalidOperationException(
                "대회 계좌 정보를 확인할 수 없습니다.");
        }

        return result;
    }
    public async Task<TradeResponse> SendCompetitionTradeAsync(
    string accessToken,
    int competitionId,
    bool isBuy,
    TradeRequest order,
    CancellationToken cancellationToken)
    {
        if (competitionId <= 0)
            throw new ArgumentException("올바르지 않은 대회 번호입니다.");

        string path = isBuy
            ? $"api/competitions/{competitionId}/trades/buy"
            : $"api/competitions/{competitionId}/trades/sell";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        request.Content = JsonContent.Create(order);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string message =
                $"대회 주문 요청에 실패했습니다. 상태 코드: {(int)response.StatusCode}";

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                message = "로그인이 만료되었습니다. 다시 로그인해주세요.";
            }
            else
            {
                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                try
                {
                    using var document = JsonDocument.Parse(body);

                    if (document.RootElement.TryGetProperty(
                            "message", out var messageElement)
                        && messageElement.ValueKind == JsonValueKind.String)
                    {
                        message = messageElement.GetString() ?? message;
                    }
                }
                catch (JsonException)
                {
                }
            }

            throw new HttpRequestException(
                message,
                null,
                response.StatusCode);
        }

        var result = await response.Content
            .ReadFromJsonAsync<TradeResponse>(
                cancellationToken: cancellationToken);

        string expectedType = isBuy ? "BUY" : "SELL";

        if (result is null
            || result.TradeId <= 0
            || result.ClientOrderId != order.ClientOrderId
            || result.StockId != order.StockId
            || result.Quantity != order.Quantity
            || result.TradeType != expectedType
            || result.UnitPrice <= 0
            || result.TotalAmount != result.UnitPrice * result.Quantity)
        {
            throw new InvalidOperationException(
                "대회 주문 응답을 확인하지 못했습니다. "
                + "새 주문을 보내기 전에 거래 결과를 확인해주세요.");
        }

        return result;
    }

    public async Task<ResearchReply> AskResearchAsync(
     string accessToken,
     string question,
     CancellationToken cancellationToken,
     Guid? conversationId = null,
     string mode = "research",
     bool usePreviousContext = false)
    {
        question = question.Trim();

        if (question.Length < 2 || question.Length > 400)
        {
            throw new InvalidOperationException(
                "질문은 2~400자로 입력해주세요.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_httpClient.BaseAddress!, "api/research/ask"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        request.Content = JsonContent.Create(new
        {
            question,
            conversationId,
            mode,
            usePreviousContext
        });

        using var response = await _researchHttpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "로그인이 만료되었습니다. 다시 로그인해주세요.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "요청 횟수 제한입니다. 1분 후 다시 시도해주세요.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                "대화가 만료되었거나 접근할 수 없습니다. 새 대화를 시작해주세요.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"자료 조사에 실패했습니다. 상태 코드: {(int)response.StatusCode}. "
                + "서버 로그를 확인해주세요.");
        }

        var result = await response.Content
            .ReadFromJsonAsync<ResearchReply>(
                cancellationToken: cancellationToken);

        if (result is null || string.IsNullOrWhiteSpace(result.Answer))
        {
            throw new InvalidOperationException(
                "챗봇 답변을 확인할 수 없습니다.");
        }

        result.Sources ??= new List<ResearchLink>();

        return result;
    }
}