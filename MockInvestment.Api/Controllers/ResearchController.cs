using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MockInvestment.Api.Models;
using MockInvestment.Api.Services;

namespace MockInvestment.Api.Controllers;

[ApiController]
[Route("api/research")]
[Authorize]
public sealed class ResearchController : ControllerBase
{
    private readonly ResearchDialogueService _service;
    private readonly ILogger<ResearchController> _logger;
    private readonly TavilySearchService _searchService;

    public ResearchController(
    ResearchDialogueService service,
    TavilySearchService searchService,
    ILogger<ResearchController> logger)
    {
        _service = service;
        _searchService = searchService;
        _logger = logger;
    }

    [HttpPost("ask")]
    [EnableRateLimiting("Research")]
    public async Task<IActionResult> Ask(
        [FromBody] ResearchRequest request,
        CancellationToken cancellationToken)
    {
        string? userId = User.FindFirst("sub")?.Value;

        if (!int.TryParse(userId, out int parsedUserId)
            || parsedUserId <= 0)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new
            {
                code = "EMPTY_QUESTION",
                message = "질문을 입력해주세요."
            });
        }

        try
        {
            var result = await _service.AskAsync(parsedUserId, request, cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Conflict(new
            {
                code = "CONVERSATION_UNAVAILABLE",
                message = "대화가 만료되었거나 접근할 수 없습니다. "
                    + "새 대화를 시작해주세요."
            });
        }
        catch (ArgumentException)
        {
            return BadRequest(new
            {
                code = "INVALID_QUESTION",
                message = "질문은 2~400자로 입력해주세요."
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new
            {
                code = "RESEARCH_TIMEOUT",
                message = "자료 조사 시간이 초과되었습니다. 질문 범위를 좁혀주세요."
            });
        }
        catch (HttpRequestException ex)
        {
            // 키, 질문, 외부 응답 원문은 기록하지 않음
            _logger.LogWarning("AI 연결 실패. HTTP={StatusCode}, 원인={Reason}, 상세={Detail}",
                ex.StatusCode, ex.Message, ex.GetBaseException().Message);

            return StatusCode(502, new
            {
                code = "AI_PROVIDER_ERROR",
                message = "AI 서비스 요청에 실패했습니다. 서버 로그의 상태 코드를 확인해주세요."
            });
        }
        catch (System.Text.Json.JsonException)
        {
            _logger.LogWarning("AI 서비스 응답 JSON 분석 실패");

            return StatusCode(502, new
            {
                code = "AI_RESPONSE_ERROR",
                message = "AI 서비스 응답을 처리하지 못했습니다."
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "AI 답변 검증 실패: {Reason}",
                ex.Message);

            return StatusCode(503, new
            {
                code = "RESEARCH_UNAVAILABLE",
                message = "설정을 확인하거나 질문 범위를 좁힌 뒤 다시 시도해주세요."
            });
        }
    }

    [HttpPost("search")]
    [EnableRateLimiting("Research")]
    public async Task<IActionResult> Search(
    [FromBody] ResearchRequest request,
    CancellationToken cancellationToken)
    {
        string? userId = User.FindFirst("sub")?.Value;

        if (!int.TryParse(userId, out int parsedUserId)
            || parsedUserId <= 0)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Question)
            || request.Question.Trim().Length > 400)
        {
            return BadRequest(new
            {
                code = "INVALID_SEARCH_QUERY",
                message = "검색어는 2~400자로 입력해주세요."
            });
        }

        try
        {
            var sources = await _searchService.SearchAsync(
                request.Question,
                cancellationToken);

            return Ok(new
            {
                searchedAt = DateTimeOffset.UtcNow,
                count = sources.Count,
                sources
            });
        }
        catch (ArgumentException)
        {
            return BadRequest(new
            {
                code = "INVALID_SEARCH_QUERY",
                message = "검색어는 2~400자로 입력해주세요."
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new
            {
                code = "SEARCH_TIMEOUT",
                message = "검색 응답 시간이 초과되었습니다."
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "검색 연결 실패. HTTP={StatusCode}, 원인={Reason}",
                ex.StatusCode,
                ex.Message);

            return StatusCode(502, new
            {
                code = "SEARCH_PROVIDER_ERROR",
                message = "검색 서비스 요청에 실패했습니다."
            });
        }
        catch (System.Text.Json.JsonException)
        {
            return StatusCode(502, new
            {
                code = "SEARCH_RESPONSE_ERROR",
                message = "검색 결과를 처리하지 못했습니다."
            });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(503, new
            {
                code = "SEARCH_UNAVAILABLE",
                message = "검색 설정 또는 응답 형식을 확인해주세요."
            });
        }
    }
}