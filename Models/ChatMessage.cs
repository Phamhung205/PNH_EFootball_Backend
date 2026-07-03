using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Appwebbongda.Models
{
    // Tin nhan chat trong 1 giai dau
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        // Thuoc giai dau nao
        [Required]
        public int TournamentId { get; set; }

        [ForeignKey("TournamentId")]
        public Tournament? Tournament { get; set; }

        // Nguoi gui
        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        // Noi dung tin nhan
        [Required]
        public string Content { get; set; } = string.Empty;

        // Thoi diem gui
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}