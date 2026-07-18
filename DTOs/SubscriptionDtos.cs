using System;
using System.ComponentModel.DataAnnotations;

namespace Appwebbongda.DTOs
{
    /// <summary>Yeu cau kich hoat goi cho 1 tai khoan.</summary>
    public class ActivatePlanRequest
    {
        /// <summary>Email tai khoan can kich hoat goi.</summary>
        [Required(ErrorMessage = "Thieu email.")]
        [EmailAddress(ErrorMessage = "Email khong hop le.")]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        /// <summary>Goi muon kich hoat: "pro" hoac "ultra".</summary>
        [Required(ErrorMessage = "Thieu ten goi.")]
        [MaxLength(20)]
        public string Plan { get; set; } = string.Empty;

        /// <summary>So thang dang ky (1-24). Mac dinh 1.</summary>
        [Range(1, 24, ErrorMessage = "So thang phai tu 1 den 24.")]
        public int Months { get; set; } = 1;
    }

    /// <summary>Yeu cau huy goi cua 1 tai khoan.</summary>
    public class CancelPlanRequest
    {
        [Required(ErrorMessage = "Thieu email.")]
        [EmailAddress(ErrorMessage = "Email khong hop le.")]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>Thong tin goi tra ve cho frontend.</summary>
    public class SubscriptionStatusDto
    {
        public string Plan { get; set; } = "free";
        public string Role { get; set; } = "User";
        public DateTime? PlanExpiry { get; set; }
        public int? DaysRemaining { get; set; }

        /// <summary>Co duoc tao giai khong (Admin hoac BTC).</summary>
        public bool CanCreateTournament { get; set; }

        public int MaxTournaments { get; set; }
        public int MaxTeamsPerTournament { get; set; }
    }
}