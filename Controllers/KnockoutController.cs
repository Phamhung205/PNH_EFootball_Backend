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

        /// <summary>
        /// Chan neu giai CHUA KICH HOAT (chua tra phi).
        /// Tra null neu duoc phep.
        /// </summary>
        private async Task<ObjectResult?> BlockIfNotActivatedAsync(int? tournamentId)
        {
            if (tournamentId == null) return null;
            var t = await _context.Tournaments.FindAsync(tournamentId.Value);
            if (t == null) return null;
            if (t.IsPaid || t.IsFree) return null;
            // Goi chung TinhPhiKichHoat de sua gia mot cho la ap dung moi noi
            var fee = TournamentsController.TinhPhiKichHoat(t.MaxTeams);
            return new ObjectResult(new
            {
                success = false,
                code = "TOURNAMENT_NOT_ACTIVATED",
                tournamentId = t.TournamentId,
                fee,
                message = $"Giải này chưa được kích hoạt. Vui lòng thanh toán {fee:N0}đ để mở khóa "
                        + "chia bảng, xếp lịch, nhập tỉ số. Bạn vẫn thêm/xóa đội được trong lúc chờ."
            })
            { StatusCode = 402 };
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
                // Them ten 'homeTeamId'/'awayTeamId' de khop voi frontend (KnockoutBracket, QualifiedTeams)
                homeTeamId = m.HomeTeamId,
                awayTeamId = m.AwayTeamId,
                homeName = m.HomeTeam != null ? m.HomeTeam.Name : null,
                homeLogo = m.HomeTeam != null ? m.HomeTeam.LogoUrl : null,
                awayName = m.AwayTeam != null ? m.AwayTeam.Name : null,
                awayLogo = m.AwayTeam != null ? m.AwayTeam.LogoUrl : null,
                homeScore = m.HomeScore,
                awayScore = m.AwayScore,
                homePenalty = m.HomePenalty,
                awayPenalty = m.AwayPenalty,
                isThirdPlace = m.IsThirdPlace,
                bracketSlot = m.BracketSlot,
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
        // GET /api/knockout/{tournamentId}/qualified
        // Danh sach doi SE vao vong knockout — xem TRUOC khi tao so do.
        // Tinh tu bang xep hang cac bang, khong can da tao so do.
        // ===================================================================
        [HttpGet("{tournamentId}/qualified")]
        public async Task<IActionResult> GetQualified(int tournamentId, [FromQuery] int? perGroup)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            int take = perGroup ?? tournament.TeamsAdvancingPerGroup ?? 2;
            var ids = await GetTopTeamsPerGroup(tournamentId, take);

            // Lay thong tin doi theo dung THU TU da xep hang
            var teams = await _context.Teams
                .Where(t => ids.Contains(t.TeamId))
                .ToListAsync();

            var ordered = ids
                .Select(id => teams.FirstOrDefault(t => t.TeamId == id))
                .Where(t => t != null)
                .Select((t, i) => new
                {
                    teamId = t!.TeamId,
                    name = t.Name,
                    logo = t.LogoUrl,
                    groupName = t.GroupName,
                    seed = i + 1                 // thu tu hat giong
                })
                .ToList();

            // Ghep cap du kien (1 gap 2, 3 gap 4, ...) — giong luc tao so do
            var pairs = new List<object>();
            for (int i = 0; i + 1 < ordered.Count; i += 2)
                pairs.Add(new { home = ordered[i], away = ordered[i + 1] });

            // Da tao so do chua
            bool daTaoSoDo = await _context.Matches
                .AnyAsync(m => m.TournamentId == tournamentId && m.Round >= KNOCKOUT_BASE);

            return Ok(new
            {
                success = true,
                data = new
                {
                    teams = ordered,
                    pairs,
                    total = ordered.Count,
                    perGroup = take,
                    hasBracket = daTaoSoDo,
                    enough = ordered.Count >= 2
                }
            });
        }

        // ===================================================================
        // 2. POST /api/knockout/{tournamentId}/generate - tao so do
        //    Lay 2 doi dung dau moi bang -> tao vong dau knockout
        // ===================================================================
        [HttpPost("{tournamentId}/generate")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> Generate(int tournamentId, [FromBody] GenerateDto? dto)
        {
            // Giai chua kich hoat (chua tra phi) -> chan
            var notPaid = await BlockIfNotActivatedAsync(tournamentId);
            if (notPaid != null) return notPaid;

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
            // BracketSlot = vi tri cap dau (0,1,2...) -> biet doi thang di vao tran nao vong sau
            for (int i = 0; i < pairs.Count; i++)
            {
                _context.Matches.Add(new Match
                {
                    TournamentId = tournamentId,
                    HomeTeamId = pairs[i].home,
                    AwayTeamId = pairs[i].away,
                    Round = KNOCKOUT_BASE,
                    Status = "Scheduled",
                    IsThirdPlace = false,
                    BracketSlot = i
                });
            }

            await _context.SaveChangesAsync();

            var data = await GetKnockoutMatches(tournamentId);
            return Ok(new { success = true, message = "Da tao so do knockout!", data });
        }

        // ===================================================================
        // Ham phu: lay doi vao knockout theo THE THUC WC.
        // - Lay top 2 moi bang (nhat + nhi)
        // - Neu can du so doi la luy thua 2 (vd 32), lay them cac doi HANG 3 tot nhat.
        // topN = so doi lay moi bang cho vi tri chinh (thuong 2).
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
                .OrderBy(g => g.Key)
                .ToList();

            int numGroups = byGroup.Count;

            var result = new List<int>();
            // Danh sach cac doi xep thu (topN+1) moi bang (vd hang 3) de xet "tot nhat"
            var rankNextTeams = new List<(int teamId, int points, int gd, int gf)>();

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

                // Sap xep bang: diem -> hieu so -> ban thang
                var ordered = standings
                    .OrderByDescending(s => s.points)
                    .ThenByDescending(s => s.gd)
                    .ThenByDescending(s => s.gf)
                    .ToList();

                // Lay topN doi dau bang (nhat, nhi...)
                result.AddRange(ordered.Take(topN).Select(s => s.teamId));

                // Doi xep thu (topN+1) - vd hang 3 - de xet lay them sau
                if (ordered.Count > topN)
                    rankNextTeams.Add(ordered[topN]);
            }

            // Tinh so doi can co de so do knockout la luy thua 2 (2,4,8,16,32,64...)
            int baseCount = result.Count;                 // vd 12 bang x 2 = 24
            int target = LargestPowerOfTwoLE(baseCount + rankNextTeams.Count);
            // target = luy thua 2 lon nhat <= (24 + 12) = 32

            int extraNeeded = target - baseCount;         // vd 32 - 24 = 8 doi hang 3
            if (extraNeeded > 0 && rankNextTeams.Count > 0)
            {
                // Lay 'extraNeeded' doi hang 3 TOT NHAT (theo diem -> hieu so -> ban thang)
                var bestThirds = rankNextTeams
                    .OrderByDescending(s => s.points)
                    .ThenByDescending(s => s.gd)
                    .ThenByDescending(s => s.gf)
                    .Take(extraNeeded)
                    .Select(s => s.teamId);
                result.AddRange(bestThirds);
            }

            return result;
        }

        // Tim luy thua 2 lon nhat <= n (vd 24 -> 16, 32 -> 32, 36 -> 32)
        private int LargestPowerOfTwoLE(int n)
        {
            int p = 1;
            while (p * 2 <= n) p *= 2;
            return p;
        }

        // (giu ham cu de tuong thich, khong dung nua)
        private async Task<List<int>> GetTopTeamsPerGroupOld(int tournamentId, int topN)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId
                            && m.Round < KNOCKOUT_BASE
                            && m.HomeScore != null && m.AwayScore != null)
                .ToListAsync();

            var byGroup = teams
                .Where(t => !string.IsNullOrEmpty(t.GroupName))
                .GroupBy(t => t.GroupName)
                .OrderBy(g => g.Key);

            var result = new List<int>();

            foreach (var group in byGroup)
            {
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
            // Giai chua kich hoat (chua tra phi) -> chan
            var notPaid = await BlockIfNotActivatedAsync(tournamentId);
            if (notPaid != null) return notPaid;

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

            // Giai chua kich hoat -> khong nhap ti so duoc
            var notPaidScore = await BlockIfNotActivatedAsync(tournamentId);
            if (notPaidScore != null) return notPaidScore;

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
                // Neu day la tran BAN KET (vong ap chot) -> day doi THUA vao tran tranh hang 3
                await HandleThirdPlace(tournamentId, match, winnerId.Value);
            }

            var data = await GetKnockoutMatches(tournamentId);
            return Ok(new { success = true, message = "Da luu ti so.", data });
        }

        // Khi tran ban ket xong: dua doi THUA vao tran tranh hang 3.
        private async Task HandleThirdPlace(int tournamentId, Match semiMatch, int winnerId)
        {
            int currentRound = semiMatch.Round;

            // Lay cac tran cung vong (khong tinh tran hang 3)
            var roundMatches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round == currentRound && !m.IsThirdPlace)
                .OrderBy(m => m.MatchId)
                .ToListAsync();

            // Chi xu ly khi vong nay co DUNG 2 tran (ban ket). Vong khac bo qua.
            if (roundMatches.Count != 2) return;

            // Doi thua tran nay = doi con lai (khong phai winnerId)
            int loserId = (semiMatch.HomeTeamId == winnerId) ? semiMatch.AwayTeamId : semiMatch.HomeTeamId;

            // Tim tran ban ket "anh em" (tran con lai cung vong)
            var otherSemi = roundMatches.FirstOrDefault(m => m.MatchId != semiMatch.MatchId);
            int? otherLoser = null;
            if (otherSemi != null)
            {
                int? otherWinner = GetWinner(otherSemi);
                if (otherWinner != null)
                    otherLoser = (otherSemi.HomeTeamId == otherWinner.Value) ? otherSemi.AwayTeamId : otherSemi.HomeTeamId;
            }

            // Tim tran tranh hang 3 da co
            var thirdMatch = await _context.Matches
                .FirstOrDefaultAsync(m => m.TournamentId == tournamentId
                                          && m.Round == currentRound && m.IsThirdPlace);

            if (thirdMatch == null)
            {
                // CHI tao khi CA 2 ban ket da xong (du 2 doi thua), tranh tao "X vs X"
                if (otherLoser == null) return;

                thirdMatch = new Match
                {
                    TournamentId = tournamentId,
                    Round = currentRound,
                    HomeTeamId = loserId,
                    AwayTeamId = otherLoser.Value,
                    Status = "Scheduled",
                    IsThirdPlace = true
                };
                _context.Matches.Add(thirdMatch);
                await _context.SaveChangesAsync();
            }
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

            // Tu va du lieu cu: tran tao truoc khi co BracketSlot deu = 0.
            // Neu thay trung nhau thi gan lai theo thu tu MatchId.
            if (currentRoundMatches.GroupBy(m => m.BracketSlot).Any(g => g.Count() > 1))
            {
                for (int i = 0; i < currentRoundMatches.Count; i++)
                    currentRoundMatches[i].BracketSlot = i;
                await _context.SaveChangesAsync();
            }

            var thisMatch = currentRoundMatches.FirstOrDefault(m => m.MatchId == currentMatch.MatchId);
            if (thisMatch == null) return;
            int idx = thisMatch.BracketSlot;   // vi tri CO DINH, khong doi theo thu tu tao

            // Tran vong sau ma doi thang se vao: idx / 2
            int nextIndex = idx / 2;
            bool isHomeSlot = (idx % 2 == 0); // tran chan -> home cua vong sau, le -> away

            // Lay cac tran vong sau da tao
            var nextRoundMatches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId && m.Round == nextRound && !m.IsThirdPlace)
                .OrderBy(m => m.MatchId)
                .ToListAsync();

            // Tim theo BracketSlot chu KHONG theo vi tri trong danh sach da tao.
            // Truoc day dung nextRoundMatches[nextIndex] -> neu cap sau xong truoc cap truoc
            // thi ca hai deu tro vao [0] va GHI DE len nhau -> trung doi.
            Match? nextMatch = nextRoundMatches.FirstOrDefault(m => m.BracketSlot == nextIndex);

            // 2 tran vong hien tai gop lai thanh 1 tran vong sau (idx chan + idx le)
            // Vi tri cua tran "anh em" (cung cap voi tran hien tai)
            int siblingIdx = isHomeSlot ? idx + 1 : idx - 1;
            Match? sibling = currentRoundMatches.FirstOrDefault(m => m.BracketSlot == siblingIdx);

            // Lay doi thang cua tran anh em (neu tran do da xong)
            int? siblingWinner = (sibling != null) ? GetWinner(sibling) : null;

            if (nextMatch == null)
            {
                // Chua co tran vong sau.
                // CHI tao khi da biet CA 2 doi (winner + siblingWinner), tranh tao "X vs X".
                if (siblingWinner == null)
                {
                    // Chua du 2 doi -> KHONG tao voi, cho tran anh em xong da.
                    return;
                }

                // Da du 2 doi -> tao tran vong sau dung thu tu home/away
                int homeId = isHomeSlot ? winnerId : siblingWinner.Value;
                int awayId = isHomeSlot ? siblingWinner.Value : winnerId;

                nextMatch = new Match
                {
                    TournamentId = tournamentId,
                    Round = nextRound,
                    HomeTeamId = homeId,
                    AwayTeamId = awayId,
                    Status = "Scheduled",
                    IsThirdPlace = false,
                    BracketSlot = nextIndex    // BAT BUOC: de vong sau nua tim dung tran
                };
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