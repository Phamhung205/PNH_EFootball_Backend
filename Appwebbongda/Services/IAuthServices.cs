using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

    // ========== EMAIL THAT qua Gmail SMTP ==========
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        public EmailSender(IConfiguration config) => _config = config;

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            var host = _config["Email:Host"] ?? "smtp.gmail.com";
            var portStr = _config["Email:Port"] ?? "587";
            var fromEmail = _config["Email:From"];
            var appPassword = _config["Email:Password"];
            var fromName = _config["Email:FromName"] ?? "PNH Football";

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(appPassword))
            {
                Console.WriteLine($"[EMAIL - CHUA CAU HINH] -> {email} | {subject} | {message}");
                return;
            }

            int port = int.TryParse(portStr, out var p) ? p : 587;

            var mail = new MailMessage();
            mail.From = new MailAddress(fromEmail, fromName);
            mail.To.Add(email);
            mail.Subject = subject;
            mail.Body = message;
            mail.IsBodyHtml = false;

            using var smtp = new SmtpClient(host, port)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromEmail, appPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            await smtp.SendMailAsync(mail);
            Console.WriteLine($"[EMAIL DA GUI] -> {email} | {subject}");
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