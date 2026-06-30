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
    public class RegistrationController : ControllerBase
    {
        private readonly AppDbContext _context;

        public RegistrationController(AppDbContext context)
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

        private bool IsAdmin() =>
            string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);

        // ===================================================================
        // 1. USER: Dang ky tham du 1 giai
        // POST /api/Registration/{tournamentId}
        // ===================================================================
        [HttpPost("{tournamentId}")]
        [Authorize]
        public async Task<IActionResult> Register(int tournamentId)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            // Giai phai dang mo cho dang ky
            if (!tournament.AllowRegistration)
                return BadRequest(new { success = false, message = "Giai nay hien khong mo dang ky." });

            // Kiem tra da dang ky chua (1 user chi dang ky 1 lan moi giai)
            var existed = await _context.Registrations
                .AnyAsync(r => r.TournamentId == tournamentId && r.UserId == uid.Value);
            if (existed)
                return Conflict(new { success = false, message = "Ban da dang ky giai nay roi." });

            var reg = new Registration
            {
                TournamentId = tournamentId,
                UserId = uid.Value,
                Status = "Registered",
                CreatedAt = DateTime.UtcNow
            };
            _context.Registrations.Add(reg);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Dang ky tham du thanh cong!",
                data = new { reg.Id, reg.TournamentId, reg.Status, reg.CreatedAt }
            });
        }

        // ===================================================================
        // 2. USER: Huy dang ky
        // DELETE /api/Registration/{tournamentId}
        // ===================================================================
        [HttpDelete("{tournamentId}")]
        [Authorize]
        public async Task<IActionResult> Unregister(int tournamentId)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var reg = await _context.Registrations
                .FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == uid.Value);
            if (reg == null)
                return NotFound(new { success = false, message = "Ban chua dang ky giai nay." });

            // Neu da duoc chia doi roi thi khong cho huy (tranh vo cau truc giai)
            if (reg.Status == "Assigned" || reg.TeamId != null)
                return BadRequest(new { success = false, message = "Ban da duoc xep doi, khong the huy dang ky." });

            _context.Registrations.Remove(reg);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Da huy dang ky." });
        }

        // ===================================================================
        // 3. USER: Kiem tra minh da dang ky giai nay chua
        // GET /api/Registration/{tournamentId}/status
        // ===================================================================
        [HttpGet("{tournamentId}/status")]
        [Authorize]
        public async Task<IActionResult> MyStatus(int tournamentId)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var reg = await _context.Registrations
                .FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == uid.Value);

            return Ok(new
            {
                success = true,
                data = new
                {
                    registered = reg != null,
                    status = reg?.Status,
                    teamId = reg?.TeamId
                }
            });
        }

        // ===================================================================
        // 4. ADMIN: Xem danh sach nguoi da dang ky 1 giai
        // GET /api/Registration/{tournamentId}/list
        // LUU Y: chi tra ten tai khoan (FullName), KHONG tra Email de bao mat
        // ===================================================================
        [HttpGet("{tournamentId}/list")]
        [Authorize]
        public async Task<IActionResult> GetList(int tournamentId)
        {
            if (!IsAdmin())
                return StatusCode(403, new { success = false, message = "Chi admin moi xem duoc danh sach dang ky." });

            var list = await _context.Registrations
                .Include(r => r.User)
                .Include(r => r.Team)
                .Where(r => r.TournamentId == tournamentId)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.UserId,
                    // CHI lay ten tai khoan, KHONG lay email
                    userName = r.User != null ? r.User.FullName : "",
                    r.Status,
                    r.TeamId,
                    teamName = r.Team != null ? r.Team.Name : null,
                    r.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                total = list.Count,
                data = list
            });
        }

        // ===================================================================
        // 5. USER: Xem cac giai minh da dang ky
        // GET /api/Registration/my
        // ===================================================================
        [HttpGet("my")]
        [Authorize]
        public async Task<IActionResult> MyRegistrations()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            var list = await _context.Registrations
                .Include(r => r.Tournament)
                .Include(r => r.Team)
                .Where(r => r.UserId == uid.Value)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.TournamentId,
                    tournamentName = r.Tournament != null ? r.Tournament.Name : "",
                    r.Status,
                    r.TeamId,
                    teamName = r.Team != null ? r.Team.Name : null,
                    r.CreatedAt
                })
                .ToListAsync();

            return Ok(new { success = true, data = list });
        }
    }
}