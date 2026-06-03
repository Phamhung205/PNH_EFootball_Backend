using System.ComponentModel.DataAnnotations;

namespace Appwebbongda.DTOs
{
    public class RegisterRequest
    {
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        [MaxLength(100, ErrorMessage = "Họ tên tối đa 100 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        [MaxLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        [MinLength(6, ErrorMessage = "Mật khẩu phải chứa ít nhất 6 ký tự.")]
        [MaxLength(50, ErrorMessage = "Mật khẩu tối đa 50 ký tự.")]
        public string Password { get; set; } = string.Empty;
    }

    public class VerifyOtpRequest
    {
        [Required(ErrorMessage = "Thông tin liên hệ (Email hoặc SĐT) không được để trống.")]
        public string ContactInfo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã OTP không được để trống.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải chứa đúng 6 ký tự.")]
        public string OtpCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Họ tên không được để trống.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        public string Password { get; set; } = string.Empty;
    }

    public class ExternalAuthDto
    {
        [Required(ErrorMessage = "Nhà cung cấp (Provider) không được để trống.")]
        public string Provider { get; set; } = string.Empty; // E.g., "Google", "Facebook"

        public string? IdToken { get; set; }

        public string? AccessToken { get; set; }
    }

    public class ExternalUserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Picture { get; set; }
    }

    public class LoginRequest
    {
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        public string Password { get; set; } = string.Empty;
    }
}
