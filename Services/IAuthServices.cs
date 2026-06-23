using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Appwebbongda.Models;

namespace Appwebbongda.Services
{
    public interface IJwtService
    {
        string GenerateToken(User user);
    }

    public interface IOtpService
    {
        string GenerateOtp(string contactInfo);
        bool ValidateOtp(string contactInfo, string otpCode);
    }

    public interface IEmailSender
    {
        Task SendEmailAsync(string email, string subject, string message);
    }

    public interface ISmsSender
    {
        Task SendSmsAsync(string phoneNumber, string message);
    }

    // ========== JWT THAT ==========
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _config;
        public JwtService(IConfiguration config) => _config = config;

        public string GenerateToken(User user)
        {
            var key = _config["Jwt:Key"]
                ?? throw new InvalidOperationException("Thieu Jwt:Key. Dat qua bien moi truong Jwt__Key.");
            var issuer = _config["Jwt:Issuer"] ?? "PNHFootball";
            var audience = _config["Jwt:Audience"] ?? "PNHFootballUsers";

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("fullName", user.FullName),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    // ========== OTP THAT (ngau nhien 6 so, luu tam, het han 5 phut) ==========
    public class OtpService : IOtpService
    {
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expiry)> _store
            = new ConcurrentDictionary<string, (string, DateTime)>();

        public string GenerateOtp(string contactInfo)
        {
            var number = RandomNumberGenerator.GetInt32(0, 1000000);
            var code = number.ToString("D6");

            var key = (contactInfo ?? "").Trim().ToLowerInvariant();
            _store[key] = (code, DateTime.UtcNow.AddMinutes(5));
            return code;
        }

        public bool ValidateOtp(string contactInfo, string otpCode)
        {
            var key = (contactInfo ?? "").Trim().ToLowerInvariant();
            if (!_store.TryGetValue(key, out var entry)) return false;

            if (DateTime.UtcNow > entry.Expiry)
            {
                _store.TryRemove(key, out _);
                return false;
            }

            var ok = entry.Code == (otpCode ?? "").Trim();
            if (ok) _store.TryRemove(key, out _);
            return ok;
        }
    }

    // ========== EMAIL THAT qua BREVO API (cong 443 - Render KHONG chan) ==========
    // Brevo (Sendinblue): free 300 email/ngay, gui toi MOI email sau khi xac minh sender.
    // KHONG can domain rieng. Goi qua HTTPS nen chay duoc tren Render.
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private static readonly HttpClient _http = new HttpClient();

        public EmailSender(IConfiguration config) => _config = config;

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            var apiKey = _config["Brevo:ApiKey"];                 // API key Brevo (xkeysib-...)
            var fromEmail = _config["Brevo:From"];                // email sender da xac minh trong Brevo
            var fromName = _config["Brevo:FromName"] ?? "PNH Football";

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
            {
                Console.WriteLine($"[EMAIL - CHUA CAU HINH BREVO] -> {email} | {subject} | {message}");
                return;
            }

            var payload = new
            {
                sender = new { name = fromName, email = fromEmail },
                to = new[] { new { email = email } },
                subject = subject,
                textContent = message
            };
            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            req.Headers.Add("api-key", apiKey);
            req.Headers.Add("accept", "application/json");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            var resBody = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"[BREVO LOI {(int)res.StatusCode}] {resBody}");
                throw new Exception($"Gui email that bai: {(int)res.StatusCode} - {resBody}");
            }
            Console.WriteLine($"[EMAIL DA GUI qua Brevo] -> {email} | {subject}");
        }
    }

    public class SmsSender : ISmsSender
    {
        public Task SendSmsAsync(string phoneNumber, string message)
        {
            Console.WriteLine($"[SMS] -> {phoneNumber} | {message}");
            return Task.CompletedTask;
        }
    }
}