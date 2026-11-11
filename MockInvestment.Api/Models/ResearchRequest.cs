using System.ComponentModel.DataAnnotations;

namespace MockInvestment.Api.Models;

public sealed class ResearchRequest
{
    public Guid? ConversationId { get; set; }

    // 자료 조사에서 이전 질문을 참고할지 사용자가 선택
    public bool UsePreviousContext { get; set; } = false;

    [Required(ErrorMessage = "질문을 입력해주세요.")]
    [StringLength(400, MinimumLength = 2,
        ErrorMessage = "질문은 2~400자로 입력해주세요.")]
    public string Question { get; set; } = "";

    // general: 검색 없는 일반 설명
    // research: 검색 기반 자료 조사
    [Required(ErrorMessage = "답변 방식을 선택해주세요.")]
    [RegularExpression(
        "^(general|research)$",
        ErrorMessage = "답변 방식은 general 또는 research여야 합니다.")]
    public string Mode { get; set; } = "research";
}