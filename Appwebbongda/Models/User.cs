using System;
using System.ComponentModel.DataAnnotations;

namespace Appwebbongda.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? PhoneNumber { get; set; }

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Provider { get; set; }

        [MaxLength(255)]
        public string? ProviderId { get; set; }

        // Vai trò người dùng. Mặc định "User". Admin = "Admin".
        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = "User";

        // ANH DAI DIEN (URL hoac base64 anh upload tu may). Null neu chua co.
        public string? AvatarUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}