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
        public async Task<IActionResult> GetQualified(int tournamentId,
            [FromQuery] int? perGroup, [FromQuery] int? thirdPlace)
        {
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            int take = perGroup ?? tournament.TeamsAdvancingPerGroup ?? 2;
            // So doi hang ba: uu tien tham so URL, sau do den cai dat da luu cua giai
            int? soHangBa = thirdPlace ?? tournament.BestThirdPlaceCount;

            var ids = await GetTopTeamsPerGroup(
                tournamentId, take, soHangBa, tournament.ManualQualifiedIds);

            // Lay chi tiet bang xep hang de danh dau ai la doi hang ba
            var (theoBang, hangBaXepHang) = await TinhBangXepHangAsync(tournamentId);
            var idsHangBa = ids.Intersect(hangBaXepHang.Select(h => h.TeamId)).ToHashSet();

            bool dangChonTay = !string.IsNullOrWhiteSpace(tournament.ManualQualifiedIds);

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
                    seed = i + 1,                // thu tu hat giong
                    isThirdPlace = idsHangBa.Contains(t.TeamId)
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
                    enough = ordered.Count >= 2,

                    // ── SO DOI HANG BA ──
                    thirdPlaceCount = soHangBa,          // cai dat hien tai (null = tu tinh)
                    thirdPlaceTaken = idsHangBa.Count,   // thuc te lay bao nhieu
                    isManual = dangChonTay,              // dang dung danh sach chon tay?

                    // Kiem tra so doi co hop le de tao so do khong
                    isPowerOfTwo = ordered.Count >= 2 && (ordered.Count & (ordered.Count - 1)) == 0,
                    nextPowerOfTwo = NextPowerOfTwo(ordered.Count),

                    // Bang xep hang DAY DU cac doi hang ba (de hien bang rieng)
                    thirdPlaceRanking = hangBaXepHang.Select((h, idx) => new
                    {
                        rank = idx + 1,
                        teamId = h.TeamId,
                        groupName = h.GroupName,
                        played = h.Played,
                        won = h.Won,
                        drawn = h.Drawn,
                        lost = h.Lost,
                        goalsFor = h.GoalsFor,
                        goalsAgainst = h.GoalsAgainst,
                        goalDiff = h.GoalDiff,
                        points = h.Points,
                        qualified = idsHangBa.Contains(h.TeamId)
                    }).ToList(),

                    // Toan bo bang xep hang tung bang (de frontend hien neu can)
                    groupStandings = theoBang.Select(b => new
                    {
                        groupName = b.FirstOrDefault()?.GroupName ?? "",
                        teams = b.Select(x => new
                        {
                            rank = x.RankInGroup,
                            teamId = x.TeamId,
                            played = x.Played,
                            won = x.Won,
                            drawn = x.Drawn,
                            lost = x.Lost,
                            goalsFor = x.GoalsFor,
                            goalsAgainst = x.GoalsAgainst,
                            goalDiff = x.GoalDiff,
                            points = x.Points
                        }).ToList()
                    }).ToList()
                }
            });
        }

        // ===================================================================
        // 1b. PUT /api/knockout/{tournamentId}/qualify-config
        //     Luu cai dat chon doi vao vong trong.
        //     - thirdPlaceCount: so doi hang ba lay them (null = tu tinh)
        //     - manualTeamIds  : danh sach chon tay (rong = dung tu dong)
        // ===================================================================
        [HttpPut("{tournamentId}/qualify-config")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> SaveQualifyConfig(int tournamentId, [FromBody] QualifyConfigDto dto)
        {
            var notPaid = await BlockIfNotActivatedAsync(tournamentId);
            if (notPaid != null) return notPaid;

            var t = await _context.Tournaments.FindAsync(tournamentId);
            if (t == null)
                return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            // So doi hang ba (am -> coi nhu khong dat)
            t.BestThirdPlaceCount = (dto.ThirdPlaceCount.HasValue && dto.ThirdPlaceCount.Value >= 0)
                ? dto.ThirdPlaceCount
                : null;

            // Danh sach chon tay: loc so hop le, bo trung
            if (dto.ManualTeamIds != null && dto.ManualTeamIds.Count > 0)
            {
                var ids = dto.ManualTeamIds.Where(x => x > 0).Distinct().ToList();
                t.ManualQualifiedIds = ids.Count > 0 ? string.Join(",", ids) : null;
            }
            else
            {
                t.ManualQualifiedIds = null;   // ve lai che do tu dong
            }

            await _context.SaveChangesAsync();
            return Ok(new
            {
                success = true,
                message = "Da luu cai dat chon doi.",
                data = new
                {
                    thirdPlaceCount = t.BestThirdPlaceCount,
                    manualTeamIds = t.ManualQualifiedIds,
                    isManual = !string.IsNullOrWhiteSpace(t.ManualQualifiedIds)
                }
            });
        }

        public class QualifyConfigDto
        {
            public int? ThirdPlaceCount { get; set; }
            public List<int>? ManualTeamIds { get; set; }
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
                advancingTeamIds = await GetTopTeamsPerGroup(
                    tournamentId, perGroup,
                    tournament.BestThirdPlaceCount,
                    tournament.ManualQualifiedIds);
            }

            if (advancingTeamIds.Count < 2)
                return BadRequest(new { success = false, message = "Chua du doi de tao so do (can it nhat 2 doi co ket qua vong bang)." });

            // ── BAT BUOC SO DOI PHAI LA LUY THUA 2 ──
            // So do loai truc tiep chi ghep duoc khi so doi la 2, 4, 8, 16, 32...
            // Vd 15 doi -> co 1 doi khong co doi thu. Chan lai va bao ro con thieu may doi.
            int soDoi = advancingTeamIds.Count;
            bool laLuyThua2 = (soDoi & (soDoi - 1)) == 0;
            if (!laLuyThua2)
            {
                int can = NextPowerOfTwo(soDoi);
                return BadRequest(new
                {
                    success = false,
                    code = "NOT_POWER_OF_TWO",
                    current = soDoi,
                    required = can,
                    missing = can - soDoi,
                    message = $"Dang co {soDoi} doi — khong tao duoc so do. "
                            + $"Can dung {can} doi (thieu {can - soDoi} doi). "
                            + "Hay tang so doi hang ba hoac chon them doi bang tay."
                });
            }

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
        /// <summary>
        /// So lieu xep hang cua MOT doi trong bang.
        /// </summary>
        private class TeamStanding
        {
            public int TeamId { get; set; }
            public string GroupName { get; set; } = "";
            public int Played { get; set; }
            public int Won { get; set; }
            public int Drawn { get; set; }
            public int Lost { get; set; }
            public int GoalsFor { get; set; }
            public int GoalsAgainst { get; set; }
            public int GoalDiff => GoalsFor - GoalsAgainst;
            public int Points => Won * 3 + Drawn;
            public int RankInGroup { get; set; }   // 1 = nhat bang
        }

        /// <summary>
        /// Xep hang MOT bang theo dung thu tu uu tien cua UEFA.
        ///
        /// Khi cac doi BANG DIEM, thu tu xet la:
        ///   1. Diem doi dau giua rieng cac doi bang nhau
        ///   2. Hieu so doi dau
        ///   3. Ban thang doi dau
        ///   4. Hieu so TOAN BANG
        ///   5. Ban thang TOAN BANG
        ///   6. So tran thang
        ///   7. TeamId (thay cho boc tham — de ket qua on dinh, khong doi moi lan goi)
        ///
        /// Khac cach cu: cach cu nhay thang sang hieu so toan bang, BO QUA doi dau
        /// -> sai thu tu khi hai doi bang diem ma doi thua doi dau lai co hieu so tot hon.
        /// </summary>
        private List<TeamStanding> XepHangBang(List<Team> teamsInGroup, List<Match> matches)
        {
            // Tinh so lieu toan bang cho tung doi
            var bang = new List<TeamStanding>();
            foreach (var team in teamsInGroup)
            {
                var st = new TeamStanding { TeamId = team.TeamId, GroupName = team.GroupName ?? "" };

                foreach (var m in matches.Where(x => x.HomeTeamId == team.TeamId))
                {
                    st.Played++;
                    st.GoalsFor += m.HomeScore!.Value;
                    st.GoalsAgainst += m.AwayScore!.Value;
                    if (m.HomeScore > m.AwayScore) st.Won++;
                    else if (m.HomeScore == m.AwayScore) st.Drawn++;
                    else st.Lost++;
                }
                foreach (var m in matches.Where(x => x.AwayTeamId == team.TeamId))
                {
                    st.Played++;
                    st.GoalsFor += m.AwayScore!.Value;
                    st.GoalsAgainst += m.HomeScore!.Value;
                    if (m.AwayScore > m.HomeScore) st.Won++;
                    else if (m.AwayScore == m.HomeScore) st.Drawn++;
                    else st.Lost++;
                }
                bang.Add(st);
            }

            // Gom cac doi BANG DIEM thanh nhom, trong moi nhom xet doi dau truoc
            var ketQua = new List<TeamStanding>();
            foreach (var nhom in bang.GroupBy(x => x.Points).OrderByDescending(g => g.Key))
            {
                var ds = nhom.ToList();
                if (ds.Count == 1) ketQua.Add(ds[0]);
                else ketQua.AddRange(XetDoiDau(ds, matches));
            }

            for (int k = 0; k < ketQua.Count; k++)
                ketQua[k].RankInGroup = k + 1;

            return ketQua;
        }

        /// <summary>
        /// Xet doi dau giua cac doi BANG DIEM.
        /// Chi tinh nhung tran ma CA HAI doi deu nam trong nhom dang xet.
        /// </summary>
        private List<TeamStanding> XetDoiDau(List<TeamStanding> nhom, List<Match> allMatches)
        {
            var ids = nhom.Select(x => x.TeamId).ToHashSet();

            var tranNoiBo = allMatches
                .Where(m => ids.Contains(m.HomeTeamId) && ids.Contains(m.AwayTeamId))
                .ToList();

            // Tinh diem / hieu so / ban thang RIENG trong nhom
            var chiSo = new Dictionary<int, (int diem, int hieuSo, int banThang)>();
            foreach (var id in ids)
            {
                int diem = 0, bt = 0, bb = 0;
                foreach (var m in tranNoiBo.Where(x => x.HomeTeamId == id))
                {
                    bt += m.HomeScore!.Value; bb += m.AwayScore!.Value;
                    if (m.HomeScore > m.AwayScore) diem += 3;
                    else if (m.HomeScore == m.AwayScore) diem += 1;
                }
                foreach (var m in tranNoiBo.Where(x => x.AwayTeamId == id))
                {
                    bt += m.AwayScore!.Value; bb += m.HomeScore!.Value;
                    if (m.AwayScore > m.HomeScore) diem += 3;
                    else if (m.AwayScore == m.HomeScore) diem += 1;
                }
                chiSo[id] = (diem, bt - bb, bt);
            }

            return nhom
                .OrderByDescending(x => chiSo[x.TeamId].diem)        // 1. diem doi dau
                .ThenByDescending(x => chiSo[x.TeamId].hieuSo)       // 2. hieu so doi dau
                .ThenByDescending(x => chiSo[x.TeamId].banThang)     // 3. ban thang doi dau
                .ThenByDescending(x => x.GoalDiff)                   // 4. hieu so toan bang
                .ThenByDescending(x => x.GoalsFor)                   // 5. ban thang toan bang
                .ThenByDescending(x => x.Won)                        // 6. so tran thang
                .ThenBy(x => x.TeamId)                               // 7. thay boc tham
                .ToList();
        }

        /// <summary>
        /// Xep hang cac doi HANG BA cua moi bang.
        /// Cac doi nay KHONG da voi nhau nen khong xet doi dau, chi xet:
        ///   diem -> hieu so -> ban thang -> so tran thang -> TeamId
        /// </summary>
        private List<TeamStanding> XepHangDoiHangBa(List<TeamStanding> cacDoi)
            => cacDoi
                .OrderByDescending(x => x.Points)
                .ThenByDescending(x => x.GoalDiff)
                .ThenByDescending(x => x.GoalsFor)
                .ThenByDescending(x => x.Won)
                .ThenBy(x => x.TeamId)
                .ToList();

        /// <summary>
        /// Tinh bang xep hang tat ca cac bang + danh sach doi hang ba.
        /// Dung chung cho ca GetQualified va Generate de hai noi luon khop nhau.
        /// </summary>
        private async Task<(List<List<TeamStanding>> theoBang, List<TeamStanding> hangBa)>
            TinhBangXepHangAsync(int tournamentId)
        {
            var teams = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .ToListAsync();

            var matches = await _context.Matches
                .Where(m => m.TournamentId == tournamentId
                            && m.Round < KNOCKOUT_BASE
                            && m.HomeScore != null && m.AwayScore != null)
                .ToListAsync();

            var theoBang = teams
                .Where(t => !string.IsNullOrEmpty(t.GroupName))
                .GroupBy(t => t.GroupName!)
                .OrderBy(g => g.Key)
                .Select(g => XepHangBang(g.ToList(), matches))
                .ToList();

            // Doi xep thu 3 cua moi bang (bang phai co it nhat 3 doi)
            var hangBa = theoBang.Where(b => b.Count >= 3).Select(b => b[2]).ToList();

            return (theoBang, XepHangDoiHangBa(hangBa));
        }

        /// <summary>
        /// Lay danh sach doi vao vong trong.
        ///
        /// Uu tien 1: neu BTC da chon TAY (ManualQualifiedIds) -> dung danh sach do.
        /// Uu tien 2: tu dong — lay topN doi dau moi bang + so doi hang ba chi dinh.
        ///
        /// soDoiHangBa:
        ///   null  -> tu tinh cho du luy thua 2
        ///   >= 0  -> lay dung so do (0 = khong lay hang ba nao)
        /// </summary>
        private async Task<List<int>> GetTopTeamsPerGroup(
            int tournamentId, int topN, int? soDoiHangBa = null, string? manualIds = null)
        {
            // ── Uu tien danh sach chon tay ──
            if (!string.IsNullOrWhiteSpace(manualIds))
            {
                var chonTay = manualIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : 0)
                    .Where(v => v > 0)
                    .Distinct()
                    .ToList();
                if (chonTay.Count > 0) return chonTay;
            }

            var (theoBang, hangBa) = await TinhBangXepHangAsync(tournamentId);

            // Lay topN doi dau moi bang
            var result = new List<int>();
            foreach (var bang in theoBang)
                result.AddRange(bang.Take(topN).Select(x => x.TeamId));

            // So doi hang ba lay them
            int extraNeeded;
            if (soDoiHangBa.HasValue && soDoiHangBa.Value >= 0)
            {
                extraNeeded = Math.Min(soDoiHangBa.Value, hangBa.Count);
            }
            else
            {
                // Tu tinh: bu cho du luy thua 2
                int target = LargestPowerOfTwoLE(result.Count + hangBa.Count);
                extraNeeded = Math.Max(0, target - result.Count);
            }

            if (extraNeeded > 0)
                result.AddRange(hangBa.Take(extraNeeded).Select(x => x.TeamId));

            return result;
        }

        /// <summary>Luy thua 2 nho nhat >= n (vd 15 -> 16, 16 -> 16, 17 -> 32).</summary>
        private static int NextPowerOfTwo(int n)
        {
            if (n <= 2) return 2;
            int p = 2;
            while (p < n) p *= 2;
            return p;
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