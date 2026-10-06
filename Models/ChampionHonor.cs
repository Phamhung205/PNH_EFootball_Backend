// ============================================================
// BACKEND - FILE THÊM MỚI
// Models/ChampionHonor.cs
// ============================================================

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Appwebbongda.Models
{
    public class ChampionHonor
    {
        [Key]
        public int Id { get; set; }

        // ID giải trong hệ thống.
        // Null đối với dữ liệu mùa cũ do Admin nhập thủ công.
        public int? TournamentId { get; set; }

        [ForeignKey(nameof(TournamentId))]
        public Tournament? Tournament { get; set; }

        // Nếu xác định được tài khoản người chơi thì lưu UserId.
        // Bản ghi thủ công có thể không cần UserId.
        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        [MaxLength(255)]
        public string CompetitionName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Season { get; set; }

        [Required]
        [MaxLength(255)]
        public string ChampionName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? TeamName { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? TeamLogoUrl { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? ChampionAvatarUrl { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime EndDate { get; set; }

        // 1 = Vô địch
        // 2 = Á quân
        // 3 = Hạng ba
        public int Placement { get; set; } = 1;

        // Auto   = Hệ thống tự thêm khi giải kết thúc.
        // Manual = Admin thêm thủ công.
        [Required]
        [MaxLength(20)]
        public string Source { get; set; } = "Manual";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}