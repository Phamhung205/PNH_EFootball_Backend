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

        // Lay role cua user hien tai (Admin/BTC/User)
        private string GetCurrentRole() =>
            User.FindFirstValue(ClaimTypes.Role) ?? "User";

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

            // CHI USER (thanh vien) duoc dang ky. Admin/BTC khong dang ky truc tiep.
            var role = GetCurrentRole();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "BTC", StringComparison.OrdinalIgnoreCase))
            {
                var roleLabel = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Ban To Chuc";
                return BadRequest(new { success = false, message = $"Ban la {roleLabel} nen khong the dang ky tham du." });
            }

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
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> GetList(int tournamentId)
        {
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

        // ===================================================================
        // 6. ADMIN/BTC: Duyet 1 dang ky (chuyen sang trang thai da duyet)
        // PUT /api/Registration/{registrationId}/approve
        // Ghi chu: hien tai dang ky mac dinh "Registered". Duyet se danh dau "Approved".
        // ===================================================================
        [HttpPut("{registrationId}/approve")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Approve(int registrationId)
        {
            var reg = await _context.Registrations.FindAsync(registrationId);
            if (reg == null) return NotFound(new { success = false, message = "Khong tim thay dang ky." });

            reg.Status = "Approved";
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Da duyet dang ky." });
        }

        // ===================================================================
        // 7. ADMIN/BTC: Tu choi / xoa 1 dang ky
        // DELETE /api/Registration/{registrationId}/reject
        // ===================================================================
        [HttpDelete("{registrationId}/reject")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Reject(int registrationId)
        {
            var reg = await _context.Registrations.FindAsync(registrationId);
            if (reg == null) return NotFound(new { success = false, message = "Khong tim thay dang ky." });

            _context.Registrations.Remove(reg);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Da tu choi/xoa dang ky." });
        }

        // ===================================================================
        // 8. ADMIN/BTC: Chia doi TU DONG (random) tu danh sach nguoi da dang ky
        // POST /api/Registration/{tournamentId}/auto-assign
        // Moi nguoi dang ky se tao thanh 1 doi (ten = ten nguoi choi), hoac
        // gan vao doi da co neu giai da tao san doi. O day: TAO doi moi theo ten nguoi.
        // ===================================================================
        [HttpPost("{tournamentId}/auto-assign")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> AutoAssign(int tournamentId)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            // Lay danh sach dang ky chua duoc gan doi
            var regs = await _context.Registrations
                .Include(r => r.User)
                .Where(r => r.TournamentId == tournamentId && r.TeamId == null)
                .ToListAsync();

            if (regs.Count == 0)
                return BadRequest(new { success = false, message = "Khong co dang ky nao can chia doi." });

            // Lay danh sach DOI CO SAN cua giai (khong tao doi moi)
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            if (teams.Count == 0)
                return BadRequest(new { success = false, message = "Giai chua co doi nao. Hay tao doi truoc khi chia." });

            // Xao tron ngau nhien ca nguoi va doi
            var rng = new Random();
            var shuffledRegs = regs.OrderBy(_ => rng.Next()).ToList();
            var shuffledTeams = teams.OrderBy(_ => rng.Next()).ToList();

            int assigned = 0;
            // Gan lan luot moi nguoi vao 1 doi (chia vong tron neu nguoi nhieu hon doi)
            for (int i = 0; i < shuffledRegs.Count; i++)
            {
                var reg = shuffledRegs[i];
                var team = shuffledTeams[i % shuffledTeams.Count]; // vong tron neu het doi
                reg.TeamId = team.TeamId;
                reg.Status = "Assigned";
                assigned++;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Da gan {assigned} nguoi vao {shuffledTeams.Count} doi!",
                total = assigned
            });
        }
        // ===================================================================
        // 9. ADMIN/BTC: Sua ten nguoi dang ky (sua FullName cua user)
        // PUT /api/Registration/{registrationId}/edit-name
        // ===================================================================
        public class EditNameDto { public string? FullName { get; set; } }

        [HttpPut("{registrationId}/edit-name")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> EditName(int registrationId, [FromBody] EditNameDto dto)
        {
            var reg = await _context.Registrations
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == registrationId);
            if (reg == null) return NotFound(new { success = false, message = "Khong tim thay dang ky." });
            if (reg.User == null) return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            if (!string.IsNullOrWhiteSpace(dto.FullName))
            {
                reg.User.FullName = dto.FullName.Trim();
                await _context.SaveChangesAsync();
            }
            return Ok(new { success = true, message = "Da sua ten." });
        }
        // ===================================================================
        // 10. Lay danh sach DOI kem TEN NGUOI duoc gan (cho phan chia bang)
        // GET /api/Registration/{tournamentId}/team-assignments
        // Tra: [{ teamId, teamName, playerName }]
        // ===================================================================
        [HttpGet("{tournamentId}/team-assignments")]
        public async Task<IActionResult> TeamAssignments(int tournamentId)
        {
            // Lay cac dang ky da duoc gan doi (co TeamId)
            var assignments = await _context.Registrations
                .Include(r => r.User)
                .Include(r => r.Team)
                .Where(r => r.TournamentId == tournamentId && r.TeamId != null)
                .Select(r => new
                {
                    teamId = r.TeamId,
                    teamName = r.Team != null ? r.Team.Name : null,
                    playerName = r.User != null ? r.User.FullName : null
                })
                .ToListAsync();

            return Ok(new { success = true, data = assignments });
        }
    }
}