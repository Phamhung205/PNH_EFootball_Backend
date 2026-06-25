using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api")]
    public class StandingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public class StandingDto
        {
            public int Rank { get; set; }
            public int TeamId { get; set; }
            public string TeamName { get; set; } = string.Empty;
            public string? LogoUrl { get; set; }
            public int Played { get; set; } // P
            public int Won { get; set; }    // W
            public int Drawn { get; set; }  // D
            public int Lost { get; set; }   // L
            public int GoalsFor { get; set; } // GF
            public int GoalsAgainst { get; set; } // GA
            public int GoalDiff { get; set; } // GD
            public int Points { get; set; } // Pts
        }

        public StandingsController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Endpoint: GET /api/tournaments/{tournamentId}/standings
        /// Tính toán bảng xếp hạng động cho giải đấu dựa trên lịch thi đấu đã kết thúc
        /// </summary>
        [HttpGet("tournaments/{tournamentId}/standings")]
        public async Task<IActionResult> GetStandings(int tournamentId)
        {
            // 1. Xác minh giải đấu tồn tại
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"Không tìm thấy giải đấu với ID = {tournamentId}."
                });
            }

            // 2. Lấy toàn bộ danh sách các đội trong giải đấu
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            // 3. Lấy toàn bộ các trận đấu đã kết thúc (Completed) của giải đấu này
            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Status == "Completed")
                .ToListAsync();

            // 4. Tính toán bảng xếp hạng động bằng LINQ in-memory (tối ưu hóa tài nguyên DB)
            var standingsList = teams.Select(team =>
            {
                // Lọc các trận đội này đá trên sân nhà và sân khách
                var homeGames = matches.Where(m => m.HomeTeamId == team.TeamId).ToList();
                var awayGames = matches.Where(m => m.AwayTeamId == team.TeamId).ToList();

                // Số trận đã đá (P)
                int played = homeGames.Count + awayGames.Count;

                // Thắng (W), Hòa (D), Thua (L)
                int won = homeGames.Count(m => m.HomeScore > m.AwayScore) +
                          awayGames.Count(m => m.AwayScore > m.HomeScore);

                int lost = homeGames.Count(m => m.HomeScore < m.AwayScore) +
                           awayGames.Count(m => m.AwayScore < m.HomeScore);

                int drawn = homeGames.Count(m => m.HomeScore == m.AwayScore) +
                            awayGames.Count(m => m.AwayScore == m.HomeScore);

                // Bàn thắng ghi được (GF), Bàn thua (GA)
                int goalsFor = homeGames.Sum(m => m.HomeScore ?? 0) +
                               awayGames.Sum(m => m.AwayScore ?? 0);

                int goalsAgainst = homeGames.Sum(m => m.AwayScore ?? 0) +
                                  awayGames.Sum(m => m.HomeScore ?? 0);

                // Hiệu số bàn thắng (GD)
                int goalDiff = goalsFor - goalsAgainst;

                // Điểm số (Pts): Thắng = 3đ, Hòa = 1đ, Thua = 0đ
                int points = won * 3 + drawn * 1;

                return new StandingDto
                {
                    TeamId = team.TeamId,
                    TeamName = team.Name,
                    LogoUrl = team.LogoUrl,
                    Played = played,
                    Won = won,
                    Drawn = drawn,
                    Lost = lost,
                    GoalsFor = goalsFor,
                    GoalsAgainst = goalsAgainst,
                    GoalDiff = goalDiff,
                    Points = points
                };
            }).ToList();

            // 5. Sắp xếp theo: Điểm -> Hiệu số -> Tổng bàn thắng -> ĐỐI ĐẦU trực tiếp
            // Sắp 3 mức đầu trước, sau đó dùng đối đầu để phân định khi vẫn bằng nhau.
            var sorted = standingsList
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.GoalDiff)
                .ThenByDescending(s => s.GoalsFor)
                .ToList();

            // Ham tinh diem doi dau giua 2 doi (chi xet cac tran giua chinh 2 doi do)
            // Tra ve: >0 neu teamA tren teamB, <0 neu duoi, 0 neu van hoa
            int HeadToHead(int teamAId, int teamBId)
            {
                var h2h = matches.Where(m =>
                    (m.HomeTeamId == teamAId && m.AwayTeamId == teamBId) ||
                    (m.HomeTeamId == teamBId && m.AwayTeamId == teamAId)).ToList();
                if (h2h.Count == 0) return 0;

                int aPoints = 0, bPoints = 0, aGoals = 0, bGoals = 0;
                foreach (var m in h2h)
                {
                    int hs = m.HomeScore ?? 0, as_ = m.AwayScore ?? 0;
                    // Quy ve goc nhin teamA
                    int aScore = m.HomeTeamId == teamAId ? hs : as_;
                    int bScore = m.HomeTeamId == teamAId ? as_ : hs;
                    aGoals += aScore; bGoals += bScore;
                    if (aScore > bScore) aPoints += 3;
                    else if (aScore < bScore) bPoints += 3;
                    else { aPoints += 1; bPoints += 1; }
                }
                if (aPoints != bPoints) return aPoints - bPoints;     // diem doi dau
                if (aGoals != bGoals) return aGoals - bGoals;         // hieu so doi dau
                return 0;
            }

            // Sap xep lai cac nhom bang diem+hieu so+ban thang bang doi dau (bubble sort don gian)
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                for (int j = 0; j < sorted.Count - 1 - i; j++)
                {
                    var x = sorted[j]; var y = sorted[j + 1];
                    // Chi so sanh doi dau khi 2 doi bang het 3 chi so chinh
                    bool tied = x.Points == y.Points && x.GoalDiff == y.GoalDiff && x.GoalsFor == y.GoalsFor;
                    if (tied)
                    {
                        int cmp = HeadToHead(x.TeamId, y.TeamId);
                        if (cmp < 0) { sorted[j] = y; sorted[j + 1] = x; } // y tren x
                    }
                }
            }
            var standingsListSorted = sorted;

            // 6. Gán thứ hạng (Rank) sau khi đã sắp xếp xong
            for (int i = 0; i < standingsListSorted.Count; i++)
            {
                standingsListSorted[i].Rank = i + 1;
            }

            return Ok(new
            {
                success = true,
                message = $"Lấy bảng xếp hạng giải đấu \"{tournament.Name}\" thành công.",
                data = standingsListSorted
            });
        }
    }
}