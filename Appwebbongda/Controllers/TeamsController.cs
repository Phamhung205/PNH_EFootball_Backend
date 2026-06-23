using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api")]
    public class TeamsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TeamsController(AppDbContext context)
        {
            _context = context;
        }

        public class TeamDto
        {
            public string Name { get; set; } = string.Empty;
            public string? LogoUrl { get; set; }
        }

        public class SaveGroupsDto
        {
            public Dictionary<string, List<int>> Groups { get; set; } = new();
        }

        /// <summary>
        /// GET /api/tournaments/{tournamentId}/teams
        /// CHI tra ve doi DUNG giai (loc chat TournamentId). Doi mo coi (null) khong bao gio lot vao.
        /// </summary>
        [HttpGet("tournaments/{tournamentId}/teams")]
        public async Task<IActionResult> GetByTournament(int tournamentId)
        {
            // Loc chat: TournamentId PHAI bang dung tournamentId (doi null bi loai)
            var teams = await _context.Teams
                .Where(t => t.TournamentId != null && t.TournamentId == tournamentId)
                .ToListAsync();

            return Ok(new { success = true, data = teams });
        }

        /// <summary>
        /// GET /api/teams/library
        /// THU VIEN DOI: lay tat ca doi tu MOI giai (kem ten giai goc) de tai lai vao giai moi.
        /// Gop doi trung ten (giu 1 ban dai dien, uu tien ban co logo).
        /// </summary>
        [HttpGet("teams/library")]
        public async Task<IActionResult> GetLibrary([FromQuery] int? excludeTournamentId = null)
        {
            try
            {
                // CUC NHE: de DATABASE gom nhom, chi keo ve ket qua cuoi (it du lieu).
                // Tranh tai het doi ve RAM (gay OutOfMemory tren server RAM thap).
                // Gom theo ten doi (thuong), lay 1 logo dai dien, dem so luong.
                var grouped = await _context.Teams
                    .Where(t => t.TournamentId != null && t.Name != null && t.Name != "")
                    .GroupBy(t => t.Name!.ToLower())
                    .Select(g => new
                    {
                        name = g.Select(x => x.Name).FirstOrDefault(),
                        logoUrl = g.Where(x => x.LogoUrl != null && x.LogoUrl != "")
                                   .Select(x => x.LogoUrl).FirstOrDefault(),
                        count = g.Count()
                    })
                    .OrderBy(x => x.name)
                    .Take(500)   // gioi han an toan, tranh tra qua nhieu
                    .ToListAsync();

                return Ok(new { success = true, data = grouped });
            }
            catch (Exception ex)
            {
                return Ok(new { success = true, data = new object[0], warning = ex.Message });
            }
        }

        /// <summary>
        /// POST /api/tournaments/{tournamentId}/teams
        /// Them doi - LUON gan TournamentId tu URL. Tu choi neu giai khong ton tai.
        /// </summary>
        [HttpPost("tournaments/{tournamentId}/teams")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int tournamentId, [FromBody] TeamDto dto)
        {
            // Bao ve: id giai phai hop le (> 0) va ton tai
            if (tournamentId <= 0)
                return BadRequest(new { success = false, message = "Thiếu hoặc sai ID giải đấu." });

            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Tên đội bóng không được để trống." });

            var team = new Team
            {
                Name = dto.Name.Trim(),
                LogoUrl = dto.LogoUrl,
                TournamentId = tournamentId,   // LUON gan dung giai tu URL
                Status = "Đã duyệt"
            };

            _context.Teams.Add(team);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Thêm đội bóng thành công!", data = team });
        }

        [HttpGet("teams/{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            return Ok(new { success = true, data = team });
        }

        [HttpPut("teams/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, [FromBody] TeamDto dto)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            if (!string.IsNullOrWhiteSpace(dto.Name)) team.Name = dto.Name.Trim();
            if (dto.LogoUrl != null) team.LogoUrl = dto.LogoUrl;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật đội thành công!", data = team });
        }

        [HttpDelete("teams/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            var relatedMatches = await _context.Matches
                .Where(m => m.HomeTeamId == id || m.AwayTeamId == id)
                .ToListAsync();
            _context.Matches.RemoveRange(relatedMatches);

            _context.Teams.Remove(team);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa đội bóng thành công!" });
        }

        // ════════════════════════════════════════════════════════════
        //  DON DEP DOI MO COI (TournamentId = null)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// GET /api/teams/orphans — xem cac doi mo coi (TournamentId null) (CHI ADMIN)
        /// </summary>
        [HttpGet("teams/orphans")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetOrphans()
        {
            var orphans = await _context.Teams
                .Where(t => t.TournamentId == null)
                .ToListAsync();
            return Ok(new { success = true, count = orphans.Count, data = orphans });
        }

        /// <summary>
        /// DELETE /api/teams/orphans — XOA het doi mo coi (TournamentId null) (CHI ADMIN)
        /// Goi 1 lan de don du lieu loi cu.
        /// </summary>
        [HttpDelete("teams/orphans")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteOrphans()
        {
            var orphans = await _context.Teams
                .Where(t => t.TournamentId == null)
                .ToListAsync();

            // Xoa cac tran lien quan truoc
            var ids = orphans.Select(t => t.TeamId).ToList();
            if (ids.Count > 0)
            {
                var relatedMatches = await _context.Matches
                    .Where(m => ids.Contains(m.HomeTeamId) || ids.Contains(m.AwayTeamId))
                    .ToListAsync();
                _context.Matches.RemoveRange(relatedMatches);
                _context.Teams.RemoveRange(orphans);
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = $"Đã xóa {orphans.Count} đội mồ côi.", deleted = orphans.Count });
        }

        // ════════════════════════════════════════════════════════════
        //  GIAI DOAN 1 - CHIA BANG (giu nguyen)
        // ════════════════════════════════════════════════════════════

        [HttpPut("tournaments/{tournamentId}/groups")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SaveGroups(int tournamentId, [FromBody] SaveGroupsDto dto)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            var assign = new Dictionary<int, string>();
            if (dto?.Groups != null)
            {
                foreach (var kv in dto.Groups)
                    foreach (var teamId in kv.Value)
                        assign[teamId] = kv.Key;
            }

            foreach (var team in teams)
                team.GroupName = assign.TryGetValue(team.TeamId, out var g) ? g : null;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Lưu phân bảng thành công!", data = teams });
        }

        [HttpGet("tournaments/{tournamentId}/groups")]
        public async Task<IActionResult> GetGroups(int tournamentId)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId && t.GroupName != null)
                .ToListAsync();

            var grouped = teams
                .GroupBy(t => t.GroupName!)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());

            return Ok(new { success = true, data = grouped });
        }
    }
}