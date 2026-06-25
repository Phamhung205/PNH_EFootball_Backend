using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    /// <summary>
    /// Quan ly VONG KNOCKOUT (loai truc tiep).
    /// Quy uoc: tran knockout dung Round >= 100 de phan biet voi vong bang (Round nho).
    ///   Round 100 = vong dau knockout, 101 = vong ke, 102 = ...
    /// Khong can them cot DB / migration — tan dung cot Round san co cua Match.
    /// </summary>
    [ApiController]
    [Route("api")]
    public class KnockoutController : ControllerBase
    {
        private readonly AppDbContext _context;
        private const int KNOCKOUT_BASE = 100; // Round >= 100 => tran knockout

        public KnockoutController(AppDbContext context)
        {
            _context = context;
        }

        public class KnockoutMatchDto
        {
            public int matchId { get; set; }
            public int round { get; set; }          // 100,101,...
            public int slot { get; set; }           // vi tri tran trong vong (0,1,2..)
            public int? homeTeamId { get; set; }
            public int? awayTeamId { get; set; }
            public string? homeName { get; set; }
            public string? awayName { get; set; }
            public string? homeLogo { get; set; }
            public string? awayLogo { get; set; }
            public int? homeScore { get; set; }
            public int? awayScore { get; set; }
            public string status { get; set; } = "Scheduled";
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/knockout/{tournamentId}  -> lay so do hien co
        // ─────────────────────────────────────────────────────────────
        [HttpGet("knockout/{tournamentId}")]
        public async Task<IActionResult> GetBracket(int tournamentId)
        {
            // Don sach tran loi cu (team=0 tu lan loi truoc) neu con sot trong DB
            var broken = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE
                            && (m.HomeTeamId == 0 || m.AwayTeamId == 0))
                .ToListAsync();
            if (broken.Count > 0)
            {
                _context.Matches.RemoveRange(broken);
                await _context.SaveChangesAsync();
            }

            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE)
                .OrderBy(m => m.Round).ThenBy(m => m.MatchId)
                .ToListAsync();

            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            var dto = BuildDto(matches, teams);
            return Ok(new { success = true, data = dto });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /api/knockout/{tournamentId}/generate
        //   body: { teamsPerGroup = 2, manualTeamIds = null }
        //   - Neu manualTeamIds co: dung dung danh sach do (chinh tay).
        //   - Neu khong: tu dong lay Top N moi bang tu BXH.
        //   Xoa het tran knockout cu roi tao moi vong dau.
        // ─────────────────────────────────────────────────────────────
        public class GenerateDto
        {
            public int teamsPerGroup { get; set; } = 2;
            public List<int>? manualTeamIds { get; set; } = null;
            // Che do World Cup: 2 doi dau moi bang + N doi hang ba tot nhat (vd 8)
            // Neu bestThirdCount > 0 -> dung logic World Cup.
            public int bestThirdCount { get; set; } = 0;
        }

        [HttpPost("knockout/{tournamentId}/generate")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Generate(int tournamentId, [FromBody] GenerateDto dto)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            // 1. Xac dinh danh sach doi vao knockout
            List<int> qualifiedIds;
            if (dto.manualTeamIds != null && dto.manualTeamIds.Count >= 2)
            {
                qualifiedIds = dto.manualTeamIds;
            }
            else
            {
                // TU DONG quyet dinh: lay 2 doi dau moi bang truoc.
                var top2 = await GetTopTeamsPerGroup(tournamentId, 2);
                int groupCount = await CountGroups(tournamentId);

                // Neu so doi (2/bang) la luy thua cua 2 (16, 32...) -> dung luon, KHONG can hang ba.
                // Vi du 8 bang x 2 = 16 (dep) -> knock-out 16 doi.
                // Neu KHONG dep (vd 12 bang x 2 = 24) -> lay them hang ba cho du luy thua 2 gan nhat (32).
                bool isPowerOfTwo = top2.Count >= 2 && (top2.Count & (top2.Count - 1)) == 0;

                if (isPowerOfTwo || groupCount < 5)
                {
                    // Du dep, hoac it bang (≤4 bang) -> dung 2 doi/bang nhu thuong
                    qualifiedIds = top2;
                }
                else
                {
                    // Tinh so hang ba can lay de dat luy thua 2 gan nhat (vd 24 -> can them 8 = 32)
                    int target = 1;
                    while (target * 2 <= top2.Count + groupCount) target *= 2; // luy thua 2 lon nhat <= tong co the
                    int needThirds = target - top2.Count;
                    if (needThirds < 0) needThirds = 0;
                    if (needThirds > groupCount) needThirds = groupCount;

                    if (needThirds > 0)
                        qualifiedIds = await GetWorldCupQualified(tournamentId, needThirds);
                    else
                        qualifiedIds = top2;
                }
            }

            if (qualifiedIds.Count < 2)
                return BadRequest(new { success = false, message = "Can it nhat 2 doi de tao so do knockout." });

            // 2. Lam tron xuong luy thua cua 2 (2,4,8,16...)
            int size = 1;
            while (size * 2 <= qualifiedIds.Count) size *= 2;
            qualifiedIds = qualifiedIds.Take(size).ToList();

            // 3. Xoa het tran knockout cu cua giai
            var oldKo = _context.Matches.Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE);
            _context.Matches.RemoveRange(oldKo);
            await _context.SaveChangesAsync();

            // 4. Tao cac tran vong dau (Round = 100), ghep cap 0-1, 2-3,...
            //    Seed kieu doi dau: 1 vs cuoi (qualifiedIds da theo thu hang)
            var firstRound = new List<Match>();
            for (int i = 0; i < size; i += 2)
            {
                firstRound.Add(new Match
                {
                    TournamentId = tournamentId,
                    HomeTeamId = qualifiedIds[i],
                    AwayTeamId = qualifiedIds[i + 1],
                    Round = KNOCKOUT_BASE,
                    Status = "Scheduled",
                    MatchDate = null
                });
            }
            _context.Matches.AddRange(firstRound);
            await _context.SaveChangesAsync();

            // 5. Tra ve so do moi
            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE)
                .OrderBy(m => m.Round).ThenBy(m => m.MatchId).ToListAsync();
            var teams = await _context.Teams.Where(t => t.TournamentId == tournamentId).ToListAsync();

            return Ok(new { success = true, message = $"Da tao so do knockout voi {size} doi.", data = BuildDto(matches, teams) });
        }

        // ─────────────────────────────────────────────────────────────
        // PUT /api/knockout/match/{matchId}  body: { homeScore, awayScore }
        //   Luu ti so. Neu co ket qua -> tu tao/cap nhat tran vong sau (day doi thang len).
        // ─────────────────────────────────────────────────────────────
        public class ScoreDto { public int? homeScore { get; set; } public int? awayScore { get; set; } }

        [HttpPut("knockout/match/{matchId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateScore(int matchId, [FromBody] ScoreDto dto)
        {
            try
            {
                var match = await _context.Matches.FindAsync(matchId);
                if (match == null || match.Round < KNOCKOUT_BASE)
                    return NotFound(new { success = false, message = "Khong tim thay tran knockout." });

                match.HomeScore = dto.homeScore;
                match.AwayScore = dto.awayScore;
                bool decided = dto.homeScore != null && dto.awayScore != null && dto.homeScore != dto.awayScore;
                match.Status = decided ? "Finished" : "Scheduled";
                await _context.SaveChangesAsync();

                // Day doi thang len vong sau (co bao ve ben trong)
                if (decided)
                {
                    await AdvanceWinner(match);
                }

                // Tra ve so do moi nhat
                var matches = await _context.Matches
                    .Where(m => m.TournamentId == match.TournamentId && m.Round >= KNOCKOUT_BASE)
                    .OrderBy(m => m.Round).ThenBy(m => m.MatchId).ToListAsync();
                var teams = await _context.Teams.Where(t => t.TournamentId == match.TournamentId).ToListAsync();

                return Ok(new { success = true, data = BuildDto(matches, teams) });
            }
            catch (Exception ex)
            {
                // Khong crash: bao loi mem cho frontend
                return StatusCode(500, new { success = false, message = "Loi khi luu ti so: " + (ex.InnerException?.Message ?? ex.Message) });
            }
        }

        // ════════════════════════ HELPERS ════════════════════════

        /// Day doi thang cua 1 tran len vong ke tiep (tao tran vong sau neu chua co)
        /// Day doi thang len vong sau. CHI tao tran vong sau khi DU CA 2 DOI
        /// (tranh luu TeamId = 0 gay loi khoa ngoai). Tinh lai winner cua 2 tran "anh em".
        private async Task AdvanceWinner(Match match)
        {
            // Lay tat ca tran cung vong, sap xep on dinh de tinh chi so (slot)
            var sameRound = await _context.Matches
                .Where(m => m.TournamentId == match.TournamentId && m.Round == match.Round)
                .OrderBy(m => m.MatchId).ToListAsync();

            // Neu vong nay chi con 1 tran -> day la chung ket, khong tao vong sau
            if (sameRound.Count <= 1) return;

            int idx = sameRound.FindIndex(m => m.MatchId == match.MatchId);
            int pairStart = (idx % 2 == 0) ? idx : idx - 1; // 2 tran ghep cap: (0,1),(2,3)..
            var matchA = sameRound[pairStart];
            var matchB = (pairStart + 1 < sameRound.Count) ? sameRound[pairStart + 1] : null;

            int nextRound = match.Round + 1;
            int nextSlot = pairStart / 2;

            // Xac dinh doi thang cua 2 tran trong cap (null neu chua xong)
            int? winA = WinnerId(matchA);
            int? winB = matchB != null ? WinnerId(matchB) : (int?)null;

            // BAO VE: doi thang phai ton tai trong DB (tranh loi khoa ngoai)
            var validTeamIds = await _context.Teams
                .Where(t => t.TournamentId == match.TournamentId)
                .Select(t => t.TeamId).ToListAsync();
            if (winA != null && !validTeamIds.Contains(winA.Value)) winA = null;
            if (winB != null && !validTeamIds.Contains(winB.Value)) winB = null;

            // Tim tran vong sau o slot nay (neu da co)
            var nextMatches = await _context.Matches
                .Where(m => m.TournamentId == match.TournamentId && m.Round == nextRound)
                .OrderBy(m => m.MatchId).ToListAsync();
            Match? target = nextSlot < nextMatches.Count ? nextMatches[nextSlot] : null;

            // CHI tao/cap nhat tran vong sau khi CA HAI doi da xac dinh
            if (winA != null && winB != null)
            {
                if (target == null)
                {
                    target = new Match
                    {
                        TournamentId = match.TournamentId,
                        HomeTeamId = winA.Value,
                        AwayTeamId = winB.Value,
                        Round = nextRound,
                        Status = "Scheduled"
                    };
                    _context.Matches.Add(target);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    // Neu doi thay doi -> cap nhat lai + xoa ket qua cu (tranh sai lech)
                    bool changed = target.HomeTeamId != winA.Value || target.AwayTeamId != winB.Value;
                    if (changed)
                    {
                        target.HomeTeamId = winA.Value;
                        target.AwayTeamId = winB.Value;
                        target.HomeScore = null; target.AwayScore = null; target.Status = "Scheduled";
                        await _context.SaveChangesAsync();
                    }
                }
            }
            else if (target != null)
            {
                // 1 trong 2 tran bi sua thanh chua xong -> xoa tran vong sau (khong con hop le)
                _context.Matches.Remove(target);
                await _context.SaveChangesAsync();
            }
        }

        /// Doi thang 1 tran (null neu hoa/chua co ket qua)
        private static int? WinnerId(Match m)
        {
            if (m.HomeScore == null || m.AwayScore == null || m.HomeScore == m.AwayScore) return null;
            return m.HomeScore > m.AwayScore ? m.HomeTeamId : m.AwayTeamId;
        }

        /// Lay Top N doi moi bang dua tren BXH (diem -> hieu so -> ban thang)
        // Dem so bang (group) co doi trong giai
        private async Task<int> CountGroups(int tournamentId)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId && t.GroupName != null && t.GroupName != "")
                .ToListAsync();
            return teams.Select(t => t.GroupName).Distinct().Count();
        }

        // Logic World Cup: lay 2 doi dau MOI bang + N doi hang ba tot nhat.
        // Vi du 12 bang: 12 nhat + 12 nhi + 8 hang ba tot nhat = 32 doi.
        private async Task<List<int>> GetWorldCupQualified(int tournamentId, int bestThirdCount)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round < KNOCKOUT_BASE
                            && m.HomeScore != null && m.AwayScore != null)
                .ToListAsync();

            // Tinh chi so [Pts, GD, GF] cho moi doi
            var mut = teams.ToDictionary(t => t.TeamId, t => new int[] { 0, 0, 0 });
            foreach (var m in matches)
            {
                int hs = m.HomeScore!.Value, aws = m.AwayScore!.Value;
                if (mut.ContainsKey(m.HomeTeamId))
                {
                    mut[m.HomeTeamId][1] += (hs - aws);
                    mut[m.HomeTeamId][2] += hs;
                    mut[m.HomeTeamId][0] += hs > aws ? 3 : (hs == aws ? 1 : 0);
                }
                if (mut.ContainsKey(m.AwayTeamId))
                {
                    mut[m.AwayTeamId][1] += (aws - hs);
                    mut[m.AwayTeamId][2] += aws;
                    mut[m.AwayTeamId][0] += aws > hs ? 3 : (hs == aws ? 1 : 0);
                }
            }

            var byGroup = teams.GroupBy(t => t.GroupName ?? "");

            var firsts = new List<int>();   // doi nhat moi bang
            var seconds = new List<int>();  // doi nhi moi bang
            var thirds = new List<Team>();  // doi hang ba moi bang (de so sanh chon tot nhat)

            foreach (var g in byGroup.OrderBy(g => g.Key))
            {
                var ranked = g
                    .OrderByDescending(t => mut[t.TeamId][0]) // Pts
                    .ThenByDescending(t => mut[t.TeamId][1])  // GD
                    .ThenByDescending(t => mut[t.TeamId][2])  // GF
                    .ToList();
                if (ranked.Count >= 1) firsts.Add(ranked[0].TeamId);
                if (ranked.Count >= 2) seconds.Add(ranked[1].TeamId);
                if (ranked.Count >= 3) thirds.Add(ranked[2]);
            }

            // Chon N doi hang ba tot nhat (so sanh giua cac doi hang ba)
            var bestThirds = thirds
                .OrderByDescending(t => mut[t.TeamId][0]) // Pts
                .ThenByDescending(t => mut[t.TeamId][1])  // GD
                .ThenByDescending(t => mut[t.TeamId][2])  // GF
                .Take(bestThirdCount)
                .Select(t => t.TeamId)
                .ToList();

            // Gop: nhat -> nhi -> hang ba tot nhat
            var result = new List<int>();
            result.AddRange(firsts);
            result.AddRange(seconds);
            result.AddRange(bestThirds);
            return result;
        }

        private async Task<List<int>> GetTopTeamsPerGroup(int tournamentId, int topN)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            // Chi tinh tran vong bang (Round < KNOCKOUT_BASE) da ket thuc
            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round < KNOCKOUT_BASE
                            && m.HomeScore != null && m.AwayScore != null)
                .ToListAsync();

            // Tinh chi so cho moi doi
            var stats = teams.ToDictionary(t => t.TeamId, t => new { Pts = 0, GD = 0, GF = 0 });
            var mut = teams.ToDictionary(t => t.TeamId, t => new int[] { 0, 0, 0 }); // [Pts, GD, GF]

            foreach (var m in matches)
            {
                int hs = m.HomeScore!.Value, aws = m.AwayScore!.Value;
                if (mut.ContainsKey(m.HomeTeamId))
                {
                    mut[m.HomeTeamId][1] += (hs - aws);
                    mut[m.HomeTeamId][2] += hs;
                    mut[m.HomeTeamId][0] += hs > aws ? 3 : (hs == aws ? 1 : 0);
                }
                if (mut.ContainsKey(m.AwayTeamId))
                {
                    mut[m.AwayTeamId][1] += (aws - hs);
                    mut[m.AwayTeamId][2] += aws;
                    mut[m.AwayTeamId][0] += aws > hs ? 3 : (hs == aws ? 1 : 0);
                }
            }

            // Gom theo bang (GroupName). Doi khong co bang -> gom vao "" (1 bang chung)
            var byGroup = teams.GroupBy(t => t.GroupName ?? "");

            var result = new List<int>();
            foreach (var g in byGroup.OrderBy(g => g.Key))
            {
                var ranked = g
                    .OrderByDescending(t => mut[t.TeamId][0]) // Pts
                    .ThenByDescending(t => mut[t.TeamId][1])  // GD
                    .ThenByDescending(t => mut[t.TeamId][2])  // GF
                    .Take(topN)
                    .Select(t => t.TeamId);
                result.AddRange(ranked);
            }
            return result;
        }

        /// Chuyen list Match -> DTO kem ten/logo doi
        private List<KnockoutMatchDto> BuildDto(List<Match> matches, List<Team> teams)
        {
            var tmap = teams.ToDictionary(t => t.TeamId, t => t);
            // Gan slot theo thu tu trong tung vong
            var grouped = matches.GroupBy(m => m.Round).OrderBy(g => g.Key);
            var list = new List<KnockoutMatchDto>();
            foreach (var g in grouped)
            {
                int slot = 0;
                foreach (var m in g.OrderBy(x => x.MatchId))
                {
                    tmap.TryGetValue(m.HomeTeamId, out var home);
                    tmap.TryGetValue(m.AwayTeamId, out var away);
                    list.Add(new KnockoutMatchDto
                    {
                        matchId = m.MatchId,
                        round = m.Round,
                        slot = slot++,
                        homeTeamId = m.HomeTeamId == 0 ? (int?)null : m.HomeTeamId,
                        awayTeamId = m.AwayTeamId == 0 ? (int?)null : m.AwayTeamId,
                        homeName = home?.Name,
                        awayName = away?.Name,
                        homeLogo = home?.LogoUrl,
                        awayLogo = away?.LogoUrl,
                        homeScore = m.HomeScore,
                        awayScore = m.AwayScore,
                        status = m.Status
                    });
                }
            }
            return list;
        }
    }
}