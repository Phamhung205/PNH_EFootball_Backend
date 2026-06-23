using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Appwebbongda.Data;
using Appwebbongda.Models;
using Appwebbongda.DTOs;
using Appwebbongda.Services;
using Google.Apis.Auth;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IOtpService _otpService;
        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly IConfiguration _config;

        private const string AdminEmail = "aadmin588@gmail.com";

        public AuthController(
            AppDbContext context,
            IJwtService jwtService,
            IOtpService otpService,
            IEmailSender emailSender,
            ISmsSender smsSender,
            IConfiguration config)
        {
            _context = context;
            _jwtService = jwtService;
            _otpService = otpService;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _config = config;
        }

        private static string HashPassword(string password) =>
            BCrypt.Net.BCrypt.HashPassword(password);

        private static bool VerifyPassword(string password, string hash)
        {
            try { return BCrypt.Net.BCrypt.Verify(password, hash); }
            catch { return false; }
        }

        private static string RoleForEmail(string email) =>
            string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase) ? "Admin" : "User";

        // ---------- LOGIN thuong ----------
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
                if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
                    return Unauthorized(new { message = "Email hoac mat khau khong chinh xac!" });

                var jwtToken = _jwtService.GenerateToken(user);
                return Ok(new
                {
                    token = jwtToken,
                    user = new { user.Id, user.Email, user.FullName, user.Role }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Loi he thong khi dang nhap.", error = ex.Message });
            }
        }

        // ---------- SEND OTP ----------
        [HttpPost("register/send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] RegisterRequest request)
        {
            var userExists = await _context.Users.AnyAsync(u => u.Email == request.Email);
            if (userExists)
                return Conflict(new { success = false, message = "Email nay da duoc su dung." });

            var otpCode = _otpService.GenerateOtp(request.Email);
            await _emailSender.SendEmailAsync(request.Email, "Xac thuc OTP - PNH Football",
                $"Ma OTP cua ban la: {otpCode}. Hieu luc 5 phut.");

            return Ok(new { success = true, message = "Da gui OTP den email." });
        }

        // ---------- VERIFY OTP + tao user ----------
        [HttpPost("register/verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            if (!_otpService.ValidateOtp(request.ContactInfo, request.OtpCode))
                return BadRequest(new { success = false, message = "Ma OTP khong chinh xac hoac da het han." });

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return Conflict(new { success = false, message = "Email nay da duoc su dung." });

            var newUser = new User
            {
                FullName = request.FullName,
                Email = request.Email,
                PhoneNumber = null,
                PasswordHash = HashPassword(request.Password),
                Role = RoleForEmail(request.Email),
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            var token = _jwtService.GenerateToken(newUser);
            return Ok(new
            {
                success = true,
                message = "Dang ky thanh cong.",
                data = new { token, user = new { newUser.Id, newUser.FullName, newUser.Email, newUser.Role } }
            });
        }

        // ---------- GOOGLE LOGIN THAT ----------
        [HttpPost("login/external")]
        public async Task<IActionResult> ExternalLogin([FromBody] ExternalAuthDto dto)
        {
            if (!string.Equals(dto.Provider, "Google", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Hien chi ho tro dang nhap Google." });

            if (string.IsNullOrEmpty(dto.IdToken))
                return BadRequest(new { success = false, message = "Thieu idToken tu Google." });

            GoogleJsonWebSignature.Payload payload;
            try
            {
                var clientId = _config["Google:ClientId"];
                var settings = new GoogleJsonWebSignature.ValidationSettings();
                if (!string.IsNullOrEmpty(clientId))
                    settings.Audience = new[] { clientId };

                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
            }
            catch (Exception)
            {
                return Unauthorized(new { success = false, message = "idToken Google khong hop le." });
            }

            var email = payload.Email;
            var name = payload.Name ?? email;
            var googleId = payload.Subject;

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                (u.Provider == "Google" && u.ProviderId == googleId) || u.Email == email);

            if (user == null)
            {
                user = new User
                {
                    FullName = name,
                    Email = email,
                    PasswordHash = HashPassword(Guid.NewGuid().ToString()),
                    Provider = "Google",
                    ProviderId = googleId,
                    Role = RoleForEmail(email),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else if (string.IsNullOrEmpty(user.Provider))
            {
                user.Provider = "Google";
                user.ProviderId = googleId;
                user.Role = RoleForEmail(email);
                await _context.SaveChangesAsync();
            }

            var token = _jwtService.GenerateToken(user);
            return Ok(new
            {
                success = true,
                message = "Dang nhap Google thanh cong.",
                data = new { token, user = new { user.Id, user.FullName, user.Email, user.Role } }
            });
        }

        // ===================================================================
        // MOI: lay id user tu token
        // ===================================================================
        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        // GET /api/Auth/me
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null) return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            return Ok(new
            {
                success = true,
                data = new { user.Id, user.Email, user.FullName, user.PhoneNumber, user.Role }
            });
        }

        // PUT /api/Auth/profile  -- sua LOI 2
        [HttpPut("profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null) return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            if (!string.IsNullOrWhiteSpace(dto.FullName)) user.FullName = dto.FullName.Trim();
            if (dto.PhoneNumber != null) user.PhoneNumber = dto.PhoneNumber;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cap nhat ho so thanh cong.",
                data = new { user.Id, user.Email, user.FullName, user.PhoneNumber, user.Role }
            });
        }

        // PUT /api/Auth/change-password  -- sua LOI 7
        [HttpPut("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null) return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            if (!VerifyPassword(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { success = false, message = "Mat khau hien tai khong dung." });

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
                return BadRequest(new { success = false, message = "Mat khau moi phai tu 6 ky tu." });

            user.PasswordHash = HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Doi mat khau thanh cong." });
        }

        // ===================================================================
        // MOI: POST /api/Auth/forgot-password -- gui OTP ve email (LOI 4)
        // ===================================================================
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var email = (dto.Email ?? "").Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            // Bao mat: luon tra ve thanh cong du email co ton tai hay khong
            // (tranh lo email nao da dang ky). Chi gui OTP neu user ton tai.
            if (user != null)
            {
                var otp = _otpService.GenerateOtp("reset:" + email.ToLowerInvariant());
                await _emailSender.SendEmailAsync(email, "Dat lai mat khau - PNH Football",
                    $"Ma OTP dat lai mat khau cua ban la: {otp}. Hieu luc 5 phut. Neu khong phai ban yeu cau, hay bo qua email nay.");
            }

            return Ok(new { success = true, message = "Neu email ton tai, ma OTP da duoc gui." });
        }

        // ===================================================================
        // MOI: POST /api/Auth/reset-password -- dat lai mk bang OTP (LOI 4)
        // ===================================================================
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var email = (dto.Email ?? "").Trim();

            if (!_otpService.ValidateOtp("reset:" + email.ToLowerInvariant(), dto.OtpCode))
                return BadRequest(new { success = false, message = "Ma OTP khong dung hoac da het han." });

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
                return BadRequest(new { success = false, message = "Mat khau moi phai tu 6 ky tu." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return NotFound(new { success = false, message = "Khong tim thay tai khoan." });

            user.PasswordHash = HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Dat lai mat khau thanh cong. Hay dang nhap bang mat khau moi." });
        }

    }
}