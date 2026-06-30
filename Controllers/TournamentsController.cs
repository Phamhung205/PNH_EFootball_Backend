using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TournamentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TournamentsController(AppDbContext context)
        {
            _context = context;
        }

        // Lay id user tu token
        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        // Lay role tu token (Admin / BTC / User)
        private string GetCurrentRole() =>
            User.FindFirstValue(ClaimTypes.Role) ?? "User";

        private bool IsAdmin() =>
            string.Equals(GetCurrentRole(), "Admin", StringComparison.OrdinalIgnoreCase);

        // Kiem tra user hien tai co duoc phep sua/xoa giai nay khong.
        // Admin: sua moi giai. BTC: chi sua giai do chinh minh tao.
        private bool CanEditTournament(Tournament t)
        {
            if (IsAdmin()) return true;
            var uid = GetCurrentUserId();
            return uid != null && t.CreatedByUserId == uid.Value;
        }

        public class UpdateStatusDto
        {
            public string Status { get; set; } = string.Empty;
        }

        // DTO tạo / cập nhật giải đấu
        public class TournamentDto
        {
            public string Name { get; set; } = string.Empty;
            public string Format { get; set; } = "League";
            public string? Status { get; set; }
            public string? Description { get; set; }
            public int? MaxTeams { get; set; }
            public DateTime? StartDate { get; set; }
            // Cho phep dang ky tham du hay khong (admin bat/tat)
            public bool? AllowRegistration { get; set; }
        }

        /// <summary>
        /// GET /api/tournaments — Lấy danh sách tất cả giải đấu (công khai)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var query = _context.Tournaments.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status == status);

            var tournaments = await query.ToListAsync();
            return Ok(new { success = true, data = tournaments });
        }

        /// <summary>
        /// GET /api/tournaments/{id} — Chi tiết giải đấu (công khai)
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var tournament = await _context.Tournaments
                .Include(t => t.Teams)
                .FirstOrDefaultAsync(t => t.TournamentId == id);

            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            return Ok(new { success = true, data = tournament });
        }

        /// <summary>
        /// POST /api/tournaments — Tạo giải đấu mới (CHỈ ADMIN)
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Create([FromBody] TournamentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Tên giải đấu không được để trống." });

            var tournament = new Tournament
            {
                Name = dto.Name,
                Format = dto.Format ?? "League",
                Status = dto.Status ?? "Sắp khởi tranh",
                Description = dto.Description,
                MaxTeams = dto.MaxTeams ?? 16,
                StartDate = dto.StartDate ?? DateTime.Now,
                // Luu nguoi tao giai (de BTC chi sua duoc giai cua minh)
                CreatedByUserId = GetCurrentUserId(),
                // Cho phep dang ky hay khong (mac dinh false neu khong gui)
                AllowRegistration = dto.AllowRegistration ?? false
            };

            _context.Tournaments.Add(tournament);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Tạo giải đấu thành công!", data = tournament });
        }

        /// <summary>
        /// PUT /api/tournaments/{id} — Cập nhật giải (ADMIN sửa mọi giải, BTC sửa giải mình tạo)
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Update(int id, [FromBody] TournamentDto dto)
        {
            var tournament = await _context.Tournaments.FindAsync(id);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // BTC chi duoc sua giai do chinh minh tao
            if (!CanEditTournament(tournament))
                return StatusCode(403, new { success = false, message = "Ban chi duoc sua giai do chinh minh tao." });

            if (!string.IsNullOrWhiteSpace(dto.Name)) tournament.Name = dto.Name;
            if (!string.IsNullOrWhiteSpace(dto.Format)) tournament.Format = dto.Format;
            if (!string.IsNullOrWhiteSpace(dto.Status)) tournament.Status = dto.Status;
            if (dto.Description != null) tournament.Description = dto.Description;
            if (dto.MaxTeams.HasValue) tournament.MaxTeams = dto.MaxTeams.Value;
            if (dto.StartDate.HasValue) tournament.StartDate = dto.StartDate.Value;
            if (dto.AllowRegistration.HasValue) tournament.AllowRegistration = dto.AllowRegistration.Value;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật giải đấu thành công!", data = tournament });
        }

        /// <summary>
        /// DELETE /api/tournaments/{id} — Xóa giải (ADMIN mọi giải, BTC giải mình tạo)
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Delete(int id)
        {
            var tournament = await _context.Tournaments.FindAsync(id);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // BTC chi duoc xoa giai do chinh minh tao
            if (!CanEditTournament(tournament))
                return StatusCode(403, new { success = false, message = "Ban chi duoc xoa giai do chinh minh tao." });

            _context.Tournaments.Remove(tournament);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Xóa giải đấu thành công!" });
        }

        /// <summary>
        /// PUT /api/tournaments/{id}/status — Cập nhật trạng thái (ADMIN mọi giải, BTC giải mình tạo)
        /// </summary>
        [HttpPut("{id}/status")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Status))
                    return BadRequest(new { success = false, message = "Trạng thái không được để trống." });

                var tournament = await _context.Tournaments.FindAsync(id);
                if (tournament == null)
                    return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

                // BTC chi duoc doi trang thai giai do chinh minh tao
                if (!CanEditTournament(tournament))
                    return StatusCode(403, new { success = false, message = "Ban chi duoc sua giai do chinh minh tao." });

                tournament.Status = dto.Status;
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Cập nhật trạng thái thành công!", data = tournament });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}