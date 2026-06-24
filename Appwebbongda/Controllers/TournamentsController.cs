using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System;
using System.Linq;
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
            public string? LogoUrl { get; set; }
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
        [Authorize(Roles = "Admin")]
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
                LogoUrl = dto.LogoUrl
            };

            _context.Tournaments.Add(tournament);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Tạo giải đấu thành công!", data = tournament });
        }

        /// <summary>
        /// PUT /api/tournaments/{id} — Cập nhật thông tin giải đấu (CHỈ ADMIN)
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, [FromBody] TournamentDto dto)
        {
            var tournament = await _context.Tournaments.FindAsync(id);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            if (!string.IsNullOrWhiteSpace(dto.Name)) tournament.Name = dto.Name;
            if (!string.IsNullOrWhiteSpace(dto.Format)) tournament.Format = dto.Format;
            if (!string.IsNullOrWhiteSpace(dto.Status)) tournament.Status = dto.Status;
            if (dto.Description != null) tournament.Description = dto.Description;
            if (dto.MaxTeams.HasValue) tournament.MaxTeams = dto.MaxTeams.Value;
            if (dto.StartDate.HasValue) tournament.StartDate = dto.StartDate.Value;
            if (dto.LogoUrl != null) tournament.LogoUrl = dto.LogoUrl;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật giải đấu thành công!", data = tournament });
        }

        /// <summary>
        /// DELETE /api/tournaments/{id} — Xóa giải đấu (CHỈ ADMIN)
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var exists = await _context.Tournaments.AnyAsync(t => t.TournamentId == id);
                if (!exists)
                    return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

                // Xoa NHANH bang ExecuteDeleteAsync: chay thang lenh DELETE trong SQL,
                // KHONG tai du lieu ve RAM (nhanh hon nhieu voi giai nhieu tran/doi).
                // Thu tu: tran -> doi -> bang -> giai (tranh loi khoa ngoai).
                await _context.Matches.Where(m => m.TournamentId == id).ExecuteDeleteAsync();
                await _context.Teams.Where(t => t.TournamentId == id).ExecuteDeleteAsync();
                await _context.Groups.Where(g => g.TournamentId == id).ExecuteDeleteAsync();
                await _context.Tournaments.Where(t => t.TournamentId == id).ExecuteDeleteAsync();

                return Ok(new { success = true, message = "Xóa giải đấu thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xóa giải: " + (ex.InnerException?.Message ?? ex.Message) });
            }
        }

        /// <summary>
        /// PUT /api/tournaments/{id}/status — Cập nhật trạng thái (CHỈ ADMIN)
        /// </summary>
        [HttpPut("{id}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Status))
                    return BadRequest(new { success = false, message = "Trạng thái không được để trống." });

                var tournament = await _context.Tournaments.FindAsync(id);
                if (tournament == null)
                    return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

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