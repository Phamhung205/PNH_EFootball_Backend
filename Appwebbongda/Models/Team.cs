using Appwebbongda.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Appwebbongda.Models
{
    public class Team
    {
        [Key]
        public int TeamId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        // Khóa ngoại liên kết với giải đấu (Có thể null nếu đội chưa tham gia giải nào)
        [ForeignKey("Tournament")]
        public int? TournamentId { get; set; }
        public Tournament? Tournament { get; set; }

        public string Status { get; set; } = "Chờ duyệt";

        // Cho phép lưu logo dạng URL, emoji, hoặc base64 (ảnh upload dài) → nvarchar(max)
        [Column(TypeName = "nvarchar(max)")]
        public string? LogoUrl { get; set; }
    }
}