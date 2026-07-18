using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Appwebbongda.Data;
using Appwebbongda.DTOs;
using Appwebbongda.Services;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    /* ════════════════════════════════════════════════════════════════
       GOI DANG KY (Subscription)

       LUONG KHI CO THANH TOAN THAT:
         Thanh toan thanh cong
             -> goi POST /api/subscription/activate
             -> Plan + PlanExpiry duoc luu, Role len "BTC"
             -> CAP LAI JWT MOI (vi Role nam trong token!)
             -> frontend luu de token cu

       LUU Y BAO MAT:
         Endpoint activate hien CHI ADMIN goi duoc. Neu mo cho user thuong
         thi ai cung tu nang cap mien phi duoc. Khi gan cong thanh toan,
         hay goi ham nay TU webhook sau khi da xac minh giao dich.
       ════════════════════════════════════════════════════════════════ */

    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ISubscriptionService _subscription;
        private readonly IJwtService _jwtService;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(
            AppDbContext context,
            ISubscriptionService subscription,
            IJwtService jwtService,
            ILogger<SubscriptionController> logger)
        {
            _context = context;
            _subscription = subscription;
            _jwtService = jwtService;
            _logger = logger;
        }

        /// <summary>Lay email cua nguoi dang goi API tu token.</summary>
        private string? CurrentEmail() =>
            User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email");

        // ───────────────────────────────────────────────
        // GET /api/subscription/plans
        // Bang gia — frontend nen doc tu day de gia luon dong bo.
        // ───────────────────────────────────────────────
        [HttpGet("plans")]
        [AllowAnonymous]
        public IActionResult GetPlans()
        {
            var plans = _subscription.GetPlans().Select(p => new
            {
                id = p.Id,
                name = p.Name,
                pricePerMonth = p.PricePerMonth,
                maxTournaments = p.MaxTournaments,
                maxTeamsPerTournament = p.MaxTeamsPerTournament,
                grantsOrganizerRole = p.GrantsOrganizerRole
            });

            return Ok(new { success = true, data = plans });
        }

        // ───────────────────────────────────────────────
        // GET /api/subscription/me
        // Trang thai goi cua chinh minh (co kiem tra het han).
        // ───────────────────────────────────────────────
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMyStatus()
        {
            var email = CurrentEmail();
            if (string.IsNullOrEmpty(email))
                return Unauthorized(new { success = false, message = "Token khong hop le." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            // Kiem tra het han ngay khi doc — het han thi ha quyen va luu lai
            if (_subscription.ApplyExpiryIfNeeded(user))
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Goi cua {Email} da het han, ha ve free/User.", email);
            }

            var plan = _subscription.GetPlan(user.Plan);
            var dto = new SubscriptionStatusDto
            {
                Plan = user.Plan,
                Role = user.Role,
                PlanExpiry = user.PlanExpiry,
                DaysRemaining = _subscription.GetDaysRemaining(user),
                CanCreateTournament =
                    user.Role == SubscriptionService.RoleAdmin ||
                    user.Role == SubscriptionService.RoleOrganizer,
                MaxTournaments = plan.MaxTournaments,
                MaxTeamsPerTournament = plan.MaxTeamsPerTournament
            };

            return Ok(new { success = true, data = dto });
        }

        // ───────────────────────────────────────────────
        // POST /api/subscription/activate   (CHI ADMIN)
        // Kich hoat goi -> nang Role len BTC -> cap JWT moi.
        // ───────────────────────────────────────────────
        [HttpPost("activate")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Activate([FromBody] ActivatePlanRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Du lieu khong hop le." });

            if (!_subscription.IsValidPlan(request.Plan))
                return BadRequest(new { success = false, message = "Goi khong hop le. Chi nhan: free, pro, ultra." });

            var email = request.Email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return NotFound(new { success = false, message = "Khong tim thay tai khoan voi email nay." });

            var oldRole = user.Role;

            // Toan bo luat nam trong service
            _subscription.ActivatePlan(user, request.Plan, request.Months);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Kich hoat goi {Plan} ({Months} thang) cho {Email}. Role: {Old} -> {New}",
                user.Plan, request.Months, email, oldRole, user.Role);

            // Neu Admin kich hoat cho CHINH MINH -> tra token moi luon.
            // Neu kich hoat cho nguoi khac -> ho phai dang nhap lai de nhan Role moi.
            var isSelf = string.Equals(CurrentEmail(), email, StringComparison.OrdinalIgnoreCase);
            string? newToken = isSelf ? _jwtService.GenerateToken(user) : null;

            return Ok(new
            {
                success = true,
                message = isSelf
                    ? "Kich hoat goi thanh cong."
                    : "Kich hoat goi thanh cong. Nguoi dung can dang nhap lai de nhan quyen BTC.",
                data = new
                {
                    token = newToken,   // null neu kich hoat ho nguoi khac
                    requiresRelogin = !isSelf,
                    user = new
                    {
                        user.Id,
                        user.Email,
                        user.FullName,
                        user.Role,
                        user.Plan,
                        user.PlanExpiry,
                        daysRemaining = _subscription.GetDaysRemaining(user)
                    }
                }
            });
        }

        // ───────────────────────────────────────────────
        // POST /api/subscription/cancel   (CHI ADMIN)
        // Huy goi -> ve free + Role "User".
        // ───────────────────────────────────────────────
        [HttpPost("cancel")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Cancel([FromBody] CancelPlanRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Du lieu khong hop le." });

            var email = request.Email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return NotFound(new { success = false, message = "Khong tim thay tai khoan voi email nay." });

            var oldRole = user.Role;
            _subscription.CancelPlan(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Huy goi cua {Email}. Role: {Old} -> {New}", email, oldRole, user.Role);

            var isSelf = string.Equals(CurrentEmail(), email, StringComparison.OrdinalIgnoreCase);

            return Ok(new
            {
                success = true,
                message = "Da huy goi, tai khoan ve goi FREE.",
                data = new
                {
                    token = isSelf ? _jwtService.GenerateToken(user) : null,
                    requiresRelogin = !isSelf,
                    user = new { user.Id, user.Email, user.FullName, user.Role, user.Plan, user.PlanExpiry }
                }
            });
        }
    }
}