using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Appwebbongda.Models
{
    // BANG MOI: Luu thong tin user dang ky tham du 1 giai dau.
    // 1 user chi dang ky 1 lan cho moi giai (kiem tra o controller).
    // Khi admin random chia doi (Dot 3), se gan TeamId cho tung ban ghi nay.
    public class Registration
    {
        [Key]
        public int Id { get; set; }

        // Giai dau ma user dang ky
        [Required]
        public int TournamentId { get; set; }

        [ForeignKey("TournamentId")]
        public Tournament? Tournament { get; set; }

        // User dang ky
        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        // Doi duoc gan sau khi admin random chia doi (Dot 3). Null neu chua chia.
        public int? TeamId { get; set; }

        [ForeignKey("TeamId")]
        public Team? Team { get; set; }

        // Trang thai: "Registered" (da dang ky) | "Assigned" (da duoc gan doi)
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Registered";

        // Thoi diem dang ky
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}