using System.ComponentModel.DataAnnotations;

namespace MockInvestment.Api.Models;

public class TradeRequest : IValidatableObject
{
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "종목번호는 1 이상이어야 합니다.")]
    public int StockId { get; set; }

    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "수량은 1 이상의 정수여야 합니다.")]
    public int Quantity { get; set; }

    public Guid ClientOrderId { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (ClientOrderId == Guid.Empty)
        {
            yield return new ValidationResult(
                "고유 주문번호를 입력해주세요.",
                new[] { nameof(ClientOrderId) });
        }
    }
}