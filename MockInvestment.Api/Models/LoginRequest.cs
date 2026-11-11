using System.ComponentModel.DataAnnotations;

namespace MockInvestment.Api.Models;

public class LoginRequest
{
    [Required(ErrorMessage = "아이디를 입력해주세요.")]
    [StringLength(
        50,
        ErrorMessage = "아이디는 50자 이내로 입력해주세요.")]
    public string LoginId { get; set; } = string.Empty;

    [Required(ErrorMessage = "비밀번호를 입력해주세요.")]
    [StringLength(
        128,
        ErrorMessage = "비밀번호는 128자 이내로 입력해주세요.")]
    public string Password { get; set; } = string.Empty;
}