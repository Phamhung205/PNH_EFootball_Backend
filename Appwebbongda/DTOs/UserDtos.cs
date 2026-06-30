using System;

namespace Appwebbongda.DTOs
{
    // Du lieu 1 nguoi dung tra ve cho admin (KHONG co PasswordHash)
    public class UserListItemDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string Role { get; set; } = "User";
        public string? AvatarUrl { get; set; }
        public string? Provider { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Admin doi quyen 1 nguoi dung
    public class ChangeRoleDto
    {
        // "Admin" hoac "User"
        public string Role { get; set; } = string.Empty;
    }
}