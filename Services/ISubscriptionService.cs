using System;
using System.Collections.Generic;
using System.Linq;
using Appwebbongda.Models;

namespace Appwebbongda.Services
{
    /* ════════════════════════════════════════════════════════════════
       GOI DANG KY — toan bo LUAT nam o day (1 cho duy nhat).
       Controller chi goi service, khong tu xu ly logic.

       LUAT QUAN TRONG:
       1. Mua goi tra phi (pro/ultra) -> Role tu dong len "BTC" (duoc tao giai).
       2. Het han goi -> ve "free" + Role ve "User" (mat quyen tao giai).
       3. Admin KHONG BAO GIO bi doi Role (du mua hay het han).
       4. Chi ha quyen khi PlanExpiry != null. Nen tai khoan duoc Admin
          cap BTC thu cong (khong qua goi) se KHONG bi anh huong.
       ════════════════════════════════════════════════════════════════ */

    /// <summary>Thong tin 1 goi trong bang gia (backend la nguon chuan).</summary>
    public class PlanInfo
    {
        public string Id { get; set; } = "free";
        public string Name { get; set; } = "FREE";
        public int PricePerMonth { get; set; }      // VND / thang
        public int MaxTournaments { get; set; }     // -1 = khong gioi han
        public int MaxTeamsPerTournament { get; set; }
        public bool GrantsOrganizerRole { get; set; } // true = duoc len BTC
    }

    public interface ISubscriptionService
    {
        /// <summary>Danh sach goi + gia (dung cho frontend hien bang gia).</summary>
        IReadOnlyList<PlanInfo> GetPlans();

        /// <summary>Lay 1 goi theo id. Tra ve goi free neu id sai.</summary>
        PlanInfo GetPlan(string? planId);

        /// <summary>Id goi co hop le khong (free/pro/ultra).</summary>
        bool IsValidPlan(string? planId);

        /// <summary>
        /// Kiem tra goi het han. Neu het han thi ha ve free + Role "User".
        /// Tra ve true neu CO thay doi (controller can luu DB).
        /// </summary>
        bool ApplyExpiryIfNeeded(User user);

        /// <summary>
        /// Kich hoat goi cho user: dat Plan, PlanExpiry (+ so thang) va nang Role len BTC.
        /// Neu user dang con han thi CONG DON them thoi gian.
        /// </summary>
        void ActivatePlan(User user, string planId, int months);

        /// <summary>Huy goi: ve free + Role "User" (tru Admin).</summary>
        void CancelPlan(User user);

        /// <summary>So ngay con lai cua goi. Null neu khong dung goi tra phi.</summary>
        int? GetDaysRemaining(User user);
    }

    public class SubscriptionService : ISubscriptionService
    {
        // Cac hang so vai tro — tranh go chuoi lung tung nhieu noi
        public const string RoleAdmin = "Admin";
        public const string RoleOrganizer = "BTC";
        public const string RoleUser = "User";

        // BANG GIA — sua gia/gioi han tai day (frontend nen doc tu API nay)
        private static readonly List<PlanInfo> Plans = new()
        {
            new PlanInfo
            {
                Id = "free", Name = "FREE", PricePerMonth = 0,
                MaxTournaments = 2, MaxTeamsPerTournament = 16,
                GrantsOrganizerRole = false
            },
            new PlanInfo
            {
                Id = "pro", Name = "PRO", PricePerMonth = 29000,
                MaxTournaments = 10, MaxTeamsPerTournament = 32,
                GrantsOrganizerRole = true
            },
            new PlanInfo
            {
                Id = "ultra", Name = "ULTRA", PricePerMonth = 59000,
                MaxTournaments = -1, MaxTeamsPerTournament = -1,
                GrantsOrganizerRole = true
            },
        };

        public IReadOnlyList<PlanInfo> GetPlans() => Plans;

        public PlanInfo GetPlan(string? planId)
        {
            var id = (planId ?? "free").Trim().ToLowerInvariant();
            return Plans.FirstOrDefault(p => p.Id == id) ?? Plans[0];
        }

        public bool IsValidPlan(string? planId)
        {
            var id = (planId ?? "").Trim().ToLowerInvariant();
            return Plans.Any(p => p.Id == id);
        }

        public bool ApplyExpiryIfNeeded(User user)
        {
            if (user == null) return false;

            // Admin luon giu nguyen quyen
            if (string.Equals(user.Role, RoleAdmin, StringComparison.OrdinalIgnoreCase))
                return false;

            // Khong co han su dung -> khong phai goi tra phi -> bo qua
            if (user.PlanExpiry == null) return false;

            // Con han -> khong lam gi
            if (user.PlanExpiry.Value > DateTime.UtcNow) return false;

            // ===== DA HET HAN: ha ve goi free =====
            user.Plan = "free";
            user.PlanExpiry = null;

            // Chi ha quyen neu dang la BTC (do mua goi ma co)
            if (string.Equals(user.Role, RoleOrganizer, StringComparison.OrdinalIgnoreCase))
                user.Role = RoleUser;

            return true;
        }

        public void ActivatePlan(User user, string planId, int months)
        {
            if (user == null) return;

            var plan = GetPlan(planId);
            if (months <= 0) months = 1;

            if (plan.Id == "free")
            {
                CancelPlan(user);
                return;
            }

            // Neu goi cu con han -> cong don tiep; nguoc lai tinh tu hom nay
            var start = (user.PlanExpiry != null && user.PlanExpiry.Value > DateTime.UtcNow)
                ? user.PlanExpiry.Value
                : DateTime.UtcNow;

            user.Plan = plan.Id;
            user.PlanExpiry = start.AddMonths(months);

            // Nang quyen len BTC de duoc tao giai (Admin giu nguyen Admin)
            if (plan.GrantsOrganizerRole &&
                !string.Equals(user.Role, RoleAdmin, StringComparison.OrdinalIgnoreCase))
            {
                user.Role = RoleOrganizer;
            }
        }

        public void CancelPlan(User user)
        {
            if (user == null) return;

            user.Plan = "free";
            user.PlanExpiry = null;

            // Admin giu nguyen; BTC (do mua goi) ha ve User
            if (string.Equals(user.Role, RoleOrganizer, StringComparison.OrdinalIgnoreCase))
                user.Role = RoleUser;
        }

        public int? GetDaysRemaining(User user)
        {
            if (user?.PlanExpiry == null) return null;
            var days = (int)Math.Ceiling((user.PlanExpiry.Value - DateTime.UtcNow).TotalDays);
            return days > 0 ? days : 0;
        }
    }
}