using System;

namespace Appwebbongda.Models
{
    /// <summary>
    /// Kho doi CA NHAN cua tung user.
    /// Moi khi user them mot doi vao giai, doi do (ten + logo) duoc luu vao day.
    /// Lan sau tao giai khac, user chon lai tu kho thay vi go + tai logo lai.
    ///
    /// Khac Team: Team gan voi 1 GIAI cu the. TeamLibrary gan voi 1 USER,
    /// dung chung cho moi giai cua user do, KHONG lan sang user khac.
    /// </summary>
    public class TeamLibrary
    {
        public int Id { get; set; }

        // Chu so huu — moi user co kho rieng
        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }

        // Lan cuoi dung doi nay (de sap xep doi hay dung len dau)
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
    }
}