using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Appwebbongda.Data;
using Appwebbongda.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeeController : ControllerBase
    {
        private readonly AppDbContext _context;
        public FeeController(AppDbContext context) { _context = context; }

        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }
        private string GetRole() => User.FindFirstValue(ClaimTypes.Role) ?? "User";

        private bool IsAdminBtc()
        {
            var r = GetRole();
            return string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r, "BTC", StringComparison.OrdinalIgnoreCase);
        }

        // Kiem tra co quyen xem trang phi: admin/btc/nguoi tao giai OR user da dang ky
        private async Task<bool> CanView(int tournamentId, int userId)
        {
            if (IsAdminBtc()) return true;
            var t = await _context.Tournaments.FindAsync(tournamentId);
            if (t != null && t.CreatedByUserId == userId) return true;
            return await _context.Registrations
                .AnyAsync(r => r.TournamentId == tournamentId && r.UserId == userId);
        }

        // ===================================================================
        // 1. Lay thong tin phi cua giai (phi, ngan hang, tien thuong, tong quy)
        // GET /api/Fee/{tournamentId}
        // ===================================================================
        [HttpGet("{tournamentId}")]
        [Authorize]
        public async Task<IActionResult> GetFeeInfo(int tournamentId)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Can dang nhap." });

            if (!await CanView(tournamentId, uid.Value))
                return StatusCode(403, new { success = false, message = "Ban chua dang ky giai nay." });

            var t = await _context.Tournaments.FindAsync(tournamentId);
            if (t == null) return NotFound(new { success = false, message = "Khong tim thay giai." });

            // Dem so nguoi da dong / tong so dang ky
            int totalReg = await _context.Registrations.CountAsync(r => r.TournamentId == tournamentId);
            int paidCount = await _context.Registrations.CountAsync(r => r.TournamentId == tournamentId && r.HasPaid);

            // Trang thai dong phi cua CHINH user nay
            var myReg = await _context.Registrations
                .FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == uid.Value);
            bool iPaid = myReg?.HasPaid ?? false;

            // Tong quy = so nguoi da dong x phi. Con lai sau khi cat phi admin.
            int totalFund = paidCount * t.EntryFee;
            int afterAdmin = totalFund - t.AdminFee;
            if (afterAdmin < 0) afterAdmin = 0;

            return Ok(new
            {
                success = true,
                entryFee = t.EntryFee,
                adminFee = t.AdminFee,
                bankName = t.BankName,
                bankAccount = t.BankAccount,
                bankHolder = t.BankHolder,
                prize1 = t.Prize1,
                prize2 = t.Prize2,
                prize3 = t.Prize3,
                totalReg,
                paidCount,
                totalFund,
                afterAdmin,
                iPaid,
                isAdminBtc = IsAdminBtc()
            });
        }

        // ===================================================================
        // 2. Danh sach dong phi (ai da dong, ai chua) - cho admin/btc
        // GET /api/Fee/{tournamentId}/list
        // ===================================================================
        [HttpGet("{tournamentId}/list")]
        [Authorize]
        public async Task<IActionResult> GetPayList(int tournamentId)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Can dang nhap." });
            if (!await CanView(tournamentId, uid.Value))
                return StatusCode(403, new { success = false, message = "Khong co quyen." });

            var list = await _context.Registrations
                .Include(r => r.User)
                .Where(r => r.TournamentId == tournamentId)
                .OrderByDescending(r => r.HasPaid)
                .Select(r => new
                {
                    registrationId = r.Id,
                    userId = r.UserId,
                    fullName = r.User != null ? r.User.FullName : "Nguoi dung",
                    hasPaid = r.HasPaid,
                    paidAt = r.PaidAt
                })
                .ToListAsync();

            return Ok(new { success = true, data = list });
        }

        // ===================================================================
        // 3. Admin xac nhan / huy dong phi cho 1 nguoi
        // PUT /api/Fee/{registrationId}/toggle-paid
        // ===================================================================
        [HttpPut("{registrationId}/toggle-paid")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> TogglePaid(int registrationId)
        {
            var reg = await _context.Registrations.FindAsync(registrationId);
            if (reg == null) return NotFound(new { success = false, message = "Khong tim thay dang ky." });

            reg.HasPaid = !reg.HasPaid;
            reg.PaidAt = reg.HasPaid ? DateTime.UtcNow : (DateTime?)null;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, hasPaid = reg.HasPaid, message = reg.HasPaid ? "Da xac nhan dong phi." : "Da huy dong phi." });
        }

        // ===================================================================
        // 4. Admin cau hinh phi + ngan hang + tien thuong
        // PUT /api/Fee/{tournamentId}/config
        // ===================================================================
        public class FeeConfigDto
        {
            public int? EntryFee { get; set; }
            public int? AdminFee { get; set; }
            public string? BankName { get; set; }
            public string? BankAccount { get; set; }
            public string? BankHolder { get; set; }
            public int? Prize1 { get; set; }
            public int? Prize2 { get; set; }
            public int? Prize3 { get; set; }
        }

        [HttpPut("{tournamentId}/config")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> SetConfig(int tournamentId, [FromBody] FeeConfigDto dto)
        {
            var t = await _context.Tournaments.FindAsync(tournamentId);
            if (t == null) return NotFound(new { success = false, message = "Khong tim thay giai." });

            if (dto.EntryFee.HasValue) t.EntryFee = dto.EntryFee.Value;
            if (dto.AdminFee.HasValue) t.AdminFee = dto.AdminFee.Value;
            if (dto.BankName != null) t.BankName = dto.BankName;
            if (dto.BankAccount != null) t.BankAccount = dto.BankAccount;
            if (dto.BankHolder != null) t.BankHolder = dto.BankHolder;
            if (dto.Prize1.HasValue) t.Prize1 = dto.Prize1.Value;
            if (dto.Prize2.HasValue) t.Prize2 = dto.Prize2.Value;
            if (dto.Prize3.HasValue) t.Prize3 = dto.Prize3.Value;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Da luu cau hinh phi." });
        }
    }
}