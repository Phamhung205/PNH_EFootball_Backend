namespace Appwebbongda.DTOs
{
    // DTO doi mat khau (LOI 7)
    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    // DTO cap nhat ho so (LOI 2)
    public class UpdateProfileDto
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        // Anh dai dien (base64 hoac URL)
        public string? AvatarUrl { get; set; }
    }

    // DTO quen mat khau - buoc 1: gui OTP (LOI 4)
    public class ForgotPasswordDto
    {
        public string Email { get; set; } = string.Empty;
    }

    // DTO quen mat khau - buoc 2: dat lai mk bang OTP (LOI 4)
    public class ResetPasswordDto
    {
        public string Email { get; set; } = string.Empty;
        public string OtpCode { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}