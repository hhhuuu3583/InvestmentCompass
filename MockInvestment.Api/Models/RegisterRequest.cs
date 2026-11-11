using System.ComponentModel.DataAnnotations;

namespace MockInvestment.Api.Models;

public class RegisterRequest
{
    [Required(ErrorMessage = "아이디를 입력해주세요.")]
    [StringLength(
        50,
        MinimumLength = 3,
        ErrorMessage = "아이디는 3~50자로 입력해주세요.")]
    [RegularExpression(
        @"^[a-z0-9_]+$",
        ErrorMessage = "아이디는 영문 소문자, 숫자, 밑줄만 가능합니다.")]
    public string LoginId { get; set; } = string.Empty;

    [Required(ErrorMessage = "비밀번호를 입력해주세요.")]
    [StringLength(
        128,
        MinimumLength = 8,
        ErrorMessage = "비밀번호는 8~128자로 입력해주세요.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "비밀번호 확인을 입력해주세요.")]
    [Compare(
        nameof(Password),
        ErrorMessage = "비밀번호가 일치하지 않습니다.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "닉네임을 입력해주세요.")]
    [StringLength(
        30,
        MinimumLength = 1,
        ErrorMessage = "닉네임은 1~30자로 입력해주세요.")]
    public string Nickname { get; set; } = string.Empty;
}