using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api")]
    public class MatchesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MatchesController(AppDbContext context)
        {
            _context = context;
        }

        // DTO cập nhật tỉ số
        public class UpdateScoreDto
        {
            public int HomeScore { get; set; }
            public int AwayScore { get; set; }
        }

        // DTO tạo lịch tự động
        public class GenerateScheduleDto
        {
            public string Type { get; set; } = "single"; // single = lượt đi, double = đi/về
        }

        /// <summary>
        /// GET /api/tournaments/{tournamentId}/matches
        /// Lấy toàn bộ lịch thi đấu của giải (công khai)
        /// </summary>
        [HttpGet("tournaments/{tournamentId}/matches")]
        public async Task<IActionResult> GetByTournament(int tournamentId)
        {
            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId)
                .OrderBy(m => m.Round)
                .ToListAsync();

            return Ok(new { success = true, data = matches });
        }

        /// <summary>
        /// POST /api/tournaments/{tournamentId}/matches/random
        /// Tạo lịch thi đấu tự động theo thuật toán vòng tròn (Round-Robin) (CHỈ ADMIN)
        /// </summary>
        [HttpPost("tournaments/{tournamentId}/matches/random")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GenerateSchedule(int tournamentId, [FromBody] GenerateScheduleDto? dto)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            if (teams.Count < 2)
                return BadRequest(new { success = false, message = "Cần ít nhất 2 đội bóng để tạo lịch." });

            // Xóa lịch cũ
            var oldMatches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId)
                .ToListAsync();
            _context.Matches.RemoveRange(oldMatches);
            await _context.SaveChangesAsync();

            // Thuật toán vòng tròn (Circle method)
            var list = teams.Select(t => (int?)t.TeamId).ToList();
            if (list.Count % 2 != 0) list.Add(null); // thêm "bye" nếu lẻ

            int n = list.Count;
            int rounds = n - 1;
            int half = n / 2;
            var newMatches = new List<Match>();

            for (int r = 0; r < rounds; r++)
            {
                for (int i = 0; i < half; i++)
                {
                    var home = list[i];
                    var away = list[n - 1 - i];

                    if (home != null && away != null)
                    {
                        newMatches.Add(new Match
                        {
                            TournamentId = tournamentId,
                            HomeTeamId = home.Value,
                            AwayTeamId = away.Value,
                            Round = r + 1,
                            Status = "Scheduled",
                            HomeScore = null,
                            AwayScore = null
                        });
                    }
                }

                // Xoay vòng (giữ phần tử đầu cố định)
                var last = list[n - 1];
                for (int i = n - 1; i > 1; i--)
                    list[i] = list[i - 1];
                list[1] = last;
            }

            // Nếu type = double thì tạo thêm lượt về
            string type = dto?.Type ?? "single";
            if (type == "double")
            {
                int baseRounds = rounds;
                var secondLeg = newMatches.Select(m => new Match
                {
                    TournamentId = m.TournamentId,
                    HomeTeamId = m.AwayTeamId,
                    AwayTeamId = m.HomeTeamId,
                    Round = m.Round + baseRounds,
                    Status = "Scheduled"
                }).ToList();
                newMatches.AddRange(secondLeg);
            }

            _context.Matches.AddRange(newMatches);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã tạo {newMatches.Count} trận đấu thành công!",
                data = newMatches
            });
        }

        /// <summary>
        /// PUT /api/matches/{id}/score
        /// Cập nhật nhanh tỉ số trận đấu (CHỈ ADMIN)
        /// </summary>
        [HttpPut("matches/{id}/score")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateScore(int id, [FromBody] UpdateScoreDto dto)
        {
            try
            {
                if (dto.HomeScore < 0 || dto.AwayScore < 0)
                    return BadRequest(new { success = false, message = "Tỷ số không được nhỏ hơn 0." });

                var match = await _context.Matches.FindAsync(id);
                if (match == null)
                    return NotFound(new { success = false, message = $"Không tìm thấy trận đấu ID = {id}." });

                match.HomeScore = dto.HomeScore;
                match.AwayScore = dto.AwayScore;
                match.Status = "Completed";

                await _context.SaveChangesAsync();

                var updated = await _context.Matches
                    .Include(m => m.HomeTeam)
                    .Include(m => m.AwayTeam)
                    .FirstOrDefaultAsync(m => m.MatchId == id);

                return Ok(new { success = true, message = "Cập nhật tỉ số thành công!", data = updated });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi hệ thống khi cập nhật tỉ số.", error = ex.Message });
            }
        }

        /// <summary>
        /// PUT /api/matches/{id} — Cập nhật tỉ số (alias, khớp frontend dùng PUT /matches/{id}) (CHỈ ADMIN)
        /// </summary>
        [HttpPut("matches/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateScoreDto dto)
        {
            return await UpdateScore(id, dto);
        }

        /// <summary>
        /// DELETE /api/matches/{id} — Xóa 1 trận đấu (CHỈ ADMIN)
        /// </summary>
        [HttpDelete("matches/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var match = await _context.Matches.FindAsync(id);
            if (match == null)
                return NotFound(new { success = false, message = $"Không tìm thấy trận đấu ID = {id}." });

            _context.Matches.Remove(match);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa trận đấu thành công!" });
        }

        /// <summary>
        /// DELETE /api/tournaments/{tournamentId}/matches — Xóa toàn bộ lịch (CHỈ ADMIN)
        /// </summary>
        [HttpDelete("tournaments/{tournamentId}/matches")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ClearSchedule(int tournamentId)
        {
            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId)
                .ToListAsync();
            _context.Matches.RemoveRange(matches);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã xóa toàn bộ lịch thi đấu." });
        }
    }
}