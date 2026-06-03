using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
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

        // DTO nhận dữ liệu khi thêm/sửa đội
        public class TeamDto
        {
            public string Name { get; set; } = string.Empty;
            public string? LogoUrl { get; set; }
        }

        /// <summary>
        /// GET /api/tournaments/{tournamentId}/teams
        /// Lấy danh sách đội bóng của 1 giải đấu (công khai)
        /// </summary>
        [HttpGet("tournaments/{tournamentId}/teams")]
        public async Task<IActionResult> GetByTournament(int tournamentId)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            return Ok(new { success = true, data = teams });
        }

        /// <summary>
        /// POST /api/tournaments/{tournamentId}/teams
        /// Thêm đội bóng mới vào giải đấu (CHỈ ADMIN)
        /// </summary>
        [HttpPost("tournaments/{tournamentId}/teams")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int tournamentId, [FromBody] TeamDto dto)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Tên đội bóng không được để trống." });

            var team = new Team
            {
                Name = dto.Name,
                LogoUrl = dto.LogoUrl,
                TournamentId = tournamentId,
                Status = "Đã duyệt"
            };

            _context.Teams.Add(team);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Thêm đội bóng thành công!", data = team });
        }

        /// <summary>
        /// GET /api/teams/{id} — Lấy chi tiết 1 đội (công khai)
        /// </summary>
        [HttpGet("teams/{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            return Ok(new { success = true, data = team });
        }

        /// <summary>
        /// PUT /api/teams/{id} — Cập nhật đội (CHỈ ADMIN)
        /// </summary>
        [HttpPut("teams/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, [FromBody] TeamDto dto)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            if (!string.IsNullOrWhiteSpace(dto.Name)) team.Name = dto.Name;
            if (dto.LogoUrl != null) team.LogoUrl = dto.LogoUrl;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật đội thành công!", data = team });
        }

        /// <summary>
        /// DELETE /api/teams/{id} — Xóa đội (và các trận liên quan) (CHỈ ADMIN)
        /// </summary>
        [HttpDelete("teams/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            // Xóa các trận đấu liên quan trước (vì FK Restrict)
            var relatedMatches = await _context.Matches
                .Where(m => m.HomeTeamId == id || m.AwayTeamId == id)
                .ToListAsync();
            _context.Matches.RemoveRange(relatedMatches);

            _context.Teams.Remove(team);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa đội bóng thành công!" });
        }
    }
}