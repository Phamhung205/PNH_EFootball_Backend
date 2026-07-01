using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// Chi ro Match la cua Models (tranh trung voi System.Text.RegularExpressions.Match
// khi Visual Studio tu them using Regex)
using Match = Appwebbongda.Models.Match;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KnockoutController : ControllerBase
    {
        private readonly AppDbContext _context;

        // Cac tran knockout co Round >= KNOCKOUT_BASE de phan biet voi vong bang.
        private const int KNOCKOUT_BASE = 100;

        public KnockoutController(AppDbContext context)
        {
            _context = context;
        }

        // DTO nhan ti so khi luu 1 tran
        public class ScoreDto
        {
            public int? HomeScore { get; set; }
            public int? AwayScore { get; set; }
            public int? HomePenalty { get; set; }
            public int? AwayPenalty { get; set; }
        }

        // DTO tuy chon khi generate (hien chua dung, de mo rong sau)
        public class GenerateDto
        {
            public List<int>? ManualTeamIds { get; set; }
        }

        // ===================================================================
        // Ham phu: dinh dang 1 tran knockout ra JSON cho frontend
        // ===================================================================
        private object ToKnockoutDto(Match m)
        {
            return new
            {
                matchId = m.MatchId,
                round = m.Round,
                homeId = m.HomeTeamId,
                awayId = m.AwayTeamId,
                homeName = m.HomeTeam != null ? m.HomeTeam.Name : null,
                homeLogo = m.HomeTeam != null ? m.HomeTeam.LogoUrl : null,
                awayName = m.AwayTeam != null ? m.AwayTeam.Name : null,
                awayLogo = m.AwayTeam != null ? m.AwayTeam.LogoUrl : null,
                homeScore = m.HomeScore,
                awayScore = m.AwayScore,
                homePenalty = m.HomePenalty,
                awayPenalty = m.AwayPenalty,
                isThirdPlace = m.IsThirdPlace,
                status = m.Status
            };
        }

        // Lay tat ca tran knockout cua giai (Round >= 100), kem thong tin doi
        private async Task<List<object>> GetKnockoutMatches(int tournamentId)
        {
            var list = await _context.Matches
                .Include(m => m.HomeTeam)
                .Include(m => m.AwayTeam)
                .Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE)
                .OrderBy(m => m.Round).ThenBy(m => m.MatchId)
                .ToListAsync();

            return list.Select(ToKnockoutDto).ToList();
        }

        // ===================================================================
        // 1. GET /api/knockout/{tournamentId} - lay so do hien co
        // ===================================================================
        [HttpGet("{tournamentId}")]
        public async Task<IActionResult> Get(int tournamentId)
        {
            var data = await GetKnockoutMatches(tournamentId);
            return Ok(new { success = true, data });
        }

        // ===================================================================
        // 2. POST /api/knockout/{tournamentId}/generate - tao so do
        //    Lay 2 doi dung dau moi bang -> tao vong dau knockout
        // ===================================================================
        [HttpPost("{tournamentId}/generate")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Generate(int tournamentId, [FromBody] GenerateDto? dto)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            // Xoa cac tran knockout cu (neu co) truoc khi tao moi
            var oldKnockout = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE)
                .ToListAsync();
            if (oldKnockout.Count > 0)
                _context.Matches.RemoveRange(oldKnockout);

            // Lay danh sach doi tham gia vong knockout
            List<int> advancingTeamIds;

            if (dto?.ManualTeamIds != null && dto.ManualTeamIds.Count >= 2)
            {
                // Truong hop admin chon tay
                advancingTeamIds = dto.ManualTeamIds;
            }
            else
            {
                // Tu dong: lay so doi di tiep moi bang theo CAU HINH giai (TeamsAdvancingPerGroup).
                // Neu giai khong cau hinh -> mac dinh 2 doi/bang.
                int perGroup = (tournament.TeamsAdvancingPerGroup.HasValue && tournament.TeamsAdvancingPerGroup.Value > 0)
                    ? tournament.TeamsAdvancingPerGroup.Value
                    : 2;
                advancingTeamIds = await GetTopTeamsPerGroup(tournamentId, perGroup);
            }

            if (advancingTeamIds.Count < 2)
                return BadRequest(new { success = false, message = "Chua du doi de tao so do (can it nhat 2 doi co ket qua vong bang)." });

            // Tao cap dau vong 1 knockout: doi 1 gap doi cuoi, doi 2 gap doi ke cuoi... (kieu seed)
            // Vi don gian: ghep lien tiep 1-2, 3-4,... (co the cai tien sau)
            var pairs = new List<(int home, int away)>();
            for (int i = 0; i + 1 < advancingTeamIds.Count; i += 2)
            {
                pairs.Add((advancingTeamIds[i], advancingTeamIds[i + 1]));
            }

            // Tao cac tran vong dau (Round = KNOCKOUT_BASE)
            foreach (var p in pairs)
            {
                _context.Matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    HomeTeamId = p.home,
                    AwayTeamId = p.away,
                    Round = KNOCKOUT_BASE,
                    Status = "Scheduled",
                    IsThirdPlace = false
                });
            }

            await _context.SaveChangesAsync();

            var data = await GetKnockoutMatches(tournamentId);
            return Ok(new { success = true, message = "Da tao so do knockout!", data });
        }

        // ===================================================================
        // Ham phu: lay topN doi dung dau moi bang (theo diem)
        // ===================================================================
        private async Task<List<int>> GetTopTeamsPerGroup(int tournamentId, int topN)
        {
            // Lay tat ca doi cua giai (co GroupName)
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            // Lay cac tran vong bang da xong (Round < KNOCKOUT_BASE, da co ti so)
            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId
                            && m.Round < KNOCKOUT_BASE
                            && m.HomeScore != null && m.AwayScore != null)
                .ToListAsync();

            // Nhom doi theo bang
            var byGroup = teams
                .Where(t => !string.IsNullOrEmpty(t.GroupName))
                .GroupBy(t => t.GroupName)
                .OrderBy(g => g.Key);

            var result = new List<int>();

            foreach (var group in byGroup)
            {
                // Tinh diem cho tung doi trong bang
                var standings = new List<(int teamId, int points, int gd, int gf)>();

                foreach (var team in group)
                {
                    int won = 0, drawn = 0, gf = 0, ga = 0;

                    var homeGames = matches.Where(m => m.HomeTeamId == team.TeamId);
                    foreach (var m in homeGames)
                    {
                        gf += m.HomeScore!.Value; ga += m.AwayScore!.Value;
                        if (m.HomeScore > m.AwayScore) won++;
                        else if (m.HomeScore == m.AwayScore) drawn++;
                    }

                    var awayGames = matches.Where(m => m.AwayTeamId == team.TeamId);
                    foreach (var m in awayGames)
                    {
                        gf += m.AwayScore!.Value; ga += m.HomeScore!.Value;
                        if (m.AwayScore > m.HomeScore) won++;
                        else if (m.AwayScore == m.HomeScore) drawn++;
                    }

                    int points = won * 3 + drawn;
                    standings.Add((team.TeamId, points, gf - ga, gf));
                }

                // Sap xep: diem -> hieu so -> ban thang. Lay topN
                var topTeams = standings
                    .OrderByDescending(s => s.points)
                    .ThenByDescending(s => s.gd)
                    .ThenByDescending(s => s.gf)
                    .Take(topN)
                    .Select(s => s.teamId);

                result.AddRange(topTeams);
            }

            return result;
        }

        // ===================================================================
        // 3. DELETE /api/knockout/{tournamentId} - xoa toan bo so do
        // ===================================================================
        [HttpDelete("{tournamentId}")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Clear(int tournamentId)
        {
            var knockout = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE)
                .ToListAsync();

            if (knockout.Count > 0)
            {
                _context.Matches.RemoveRange(knockout);
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = "Da xoa so do knockout." });
        }

        // ===================================================================
        // 4. PUT /api/knockout/match/{matchId} - luu ti so 1 tran
        //    Sau khi luu, tu dong day doi thang len vong sau
        // ===================================================================
        [HttpPut("match/{matchId}")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> SaveScore(int matchId, [FromBody] ScoreDto dto)
        {
            var match = await _context.Matches.FindAsync(matchId);
            if (match == null)
                return NotFound(new { success = false, message = "Khong tim thay tran dau." });

            int tournamentId = match.TournamentId;

            // Cap nhat ti so
            match.HomeScore = dto.HomeScore;
            match.AwayScore = dto.AwayScore;
            match.HomePenalty = dto.HomePenalty;
            match.AwayPenalty = dto.AwayPenalty;
            match.Status = (dto.HomeScore != null && dto.AwayScore != null) ? "Finished" : "Scheduled";

            await _context.SaveChangesAsync();

            // Xac dinh doi thang tran nay
            int? winnerId = GetWinner(match);

            // Neu co doi thang va day khong phai tran tranh hang 3 -> day len vong sau
            if (winnerId != null && !match.IsThirdPlace)
            {
                await AdvanceWinner(tournamentId, match, winnerId.Value);
            }

            var data = await GetKnockoutMatches(tournamentId);
            return Ok(new { success = true, message = "Da luu ti so.", data });
        }

        // Xac dinh doi thang 1 tran (theo ti so chinh, roi luan luu neu hoa)
        private int? GetWinner(Match m)
        {
            if (m.HomeScore == null || m.AwayScore == null) return null;
            if (m.HomeScore > m.AwayScore) return m.HomeTeamId;
            if (m.AwayScore > m.HomeScore) return m.AwayTeamId;
            // Hoa -> xet luan luu
            if (m.HomePenalty != null && m.AwayPenalty != null)
            {
                if (m.HomePenalty > m.AwayPenalty) return m.HomeTeamId;
                if (m.AwayPenalty > m.HomePenalty) return m.AwayTeamId;
            }
            return null; // chua phan thang bai
        }

        // Day doi thang len vong sau. Tao tran vong sau neu chua co.
        private async Task AdvanceWinner(int tournamentId, Match currentMatch, int winnerId)
        {
            int currentRound = currentMatch.Round;
            int nextRound = currentRound + 1;

            // Lay tat ca tran vong hien tai (sap theo MatchId de biet thu tu cap dau)
            var currentRoundMatches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round == currentRound && !m.IsThirdPlace)
                .OrderBy(m => m.MatchId)
                .ToListAsync();

            // Neu vong hien tai chi co 1 tran -> day la chung ket, khong tao them
            if (currentRoundMatches.Count <= 1) return;

            // Tim vi tri (index) cua tran hien tai trong vong
            int idx = currentRoundMatches.FindIndex(m => m.MatchId == currentMatch.MatchId);
            if (idx < 0) return;

            // Tran vong sau ma doi thang se vao: idx / 2
            int nextIndex = idx / 2;
            bool isHomeSlot = (idx % 2 == 0); // tran chan -> home cua vong sau, le -> away

            // Lay cac tran vong sau da tao
            var nextRoundMatches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round == nextRound && !m.IsThirdPlace)
                .OrderBy(m => m.MatchId)
                .ToListAsync();

            Match? nextMatch = nextIndex < nextRoundMatches.Count ? nextRoundMatches[nextIndex] : null;

            if (nextMatch == null)
            {
                // Chua co tran vong sau -> tao moi
                // De tranh loi khoa ngoai, tam dat doi con lai = winnerId (se cap nhat khi tran kia xong)
                nextMatch = new Match
                {
                    TournamentId = tournamentId,
                    Round = nextRound,
                    HomeTeamId = isHomeSlot ? winnerId : 0,
                    AwayTeamId = isHomeSlot ? 0 : winnerId,
                    Status = "Scheduled",
                    IsThirdPlace = false
                };

                // Neu 1 trong 2 slot con = 0 (chua co doi) -> tam dat = winnerId de thoa man [Required]
                if (nextMatch.HomeTeamId == 0) nextMatch.HomeTeamId = winnerId;
                if (nextMatch.AwayTeamId == 0) nextMatch.AwayTeamId = winnerId;

                _context.Matches.Add(nextMatch);
            }
            else
            {
                // Da co tran vong sau -> cap nhat dung slot (home/away)
                if (isHomeSlot) nextMatch.HomeTeamId = winnerId;
                else nextMatch.AwayTeamId = winnerId;
            }

            await _context.SaveChangesAsync();
        }
    }
}