using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using Appwebbongda.Services;
using System.Security.Claims;
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
        private readonly ISubscriptionService _subscription;

        public TeamsController(AppDbContext context, ISubscriptionService subscription)
        {
            _context = context;
            _subscription = subscription;
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

        // ── QUYEN: Admin lam moi thu; nguoi khac chi thao tac tren giai CUA CHINH MINH ──
        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        private bool IsAdmin() =>
            string.Equals(User.FindFirstValue(ClaimTypes.Role) ?? "User", "Admin", StringComparison.OrdinalIgnoreCase);

        /// <summary>Co duoc quan ly (them/sua/xoa doi) trong giai nay khong.</summary>
        private async Task<bool> CanManageTournamentAsync(int tournamentId)
        {
            if (IsAdmin()) return true;
            var uid = GetCurrentUserId();
            if (uid == null) return false;
            var t = await _context.Tournaments.FindAsync(tournamentId);
            return t != null && t.CreatedByUserId == uid.Value;
        }

        /// <summary>Kiem tra quyen theo 1 doi (tra cuu giai cua doi do).</summary>
        private async Task<bool> CanManageTeamAsync(int teamId)
        {
            if (IsAdmin()) return true;
            var team = await _context.Teams.FindAsync(teamId);
            if (team?.TournamentId == null) return false;
            return await CanManageTournamentAsync(team.TournamentId.Value);
        }

        /// <summary>
        /// Kiem tra so doi truoc khi them.
        ///
        /// MaxTeams chi la con so nguoi tao UOC LUONG luc tao giai, KHONG phai
        /// gioi han cung. Neu ho them nhieu hon thi TU NOI RONG, khong chan —
        /// vi phi tinh theo SO DOI THUC TE nen them cang nhieu tra cang dung.
        ///
        /// Tra ve null neu duoc phep; nguoc lai tra ve thong bao loi.
        /// </summary>
        private async Task<object?> CheckTeamQuotaAsync(Tournament tournament, int adding)
        {
            if (tournament == null) return null;

            var current = await _context.Teams.CountAsync(t => t.TournamentId == tournament.TournamentId);
            var total = current + adding;

            // Chan cung o 256 doi de tranh nhap nham hoac pha hoai
            const int HARD_LIMIT = 256;
            if (total > HARD_LIMIT)
            {
                return new
                {
                    success = false,
                    code = "TEAM_HARD_LIMIT",
                    used = current,
                    limit = HARD_LIMIT,
                    message = $"Một giải chỉ nhận tối đa {HARD_LIMIT} đội. "
                            + $"Bạn đang có {current} đội và muốn thêm {adding} đội nữa."
                };
            }

            // Vuot con so da khai bao -> TU NOI RONG cho khop thuc te
            if (total > tournament.MaxTeams)
            {
                tournament.MaxTeams = total;
                await _context.SaveChangesAsync();
            }

            return null;   // luon cho them
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
        /// POST /api/tournaments/{tournamentId}/teams
        /// Them doi - LUON gan TournamentId tu URL. Tu choi neu giai khong ton tai.
        /// </summary>
        [HttpPost("tournaments/{tournamentId}/teams")]
        [Authorize]
        public async Task<IActionResult> Create(int tournamentId, [FromBody] TeamDto dto)
        {
            // Bao ve: id giai phai hop le (> 0) va ton tai
            if (tournamentId <= 0)
                return BadRequest(new { success = false, message = "Thiếu hoặc sai ID giải đấu." });

            // Chi Admin hoac nguoi tao giai moi duoc them doi
            if (!await CanManageTournamentAsync(tournamentId))
                return StatusCode(403, new { success = false, message = "Bạn chỉ thao tác được trên giải do chính mình tạo." });

            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            if (!await CanManageTournamentAsync(tournamentId))
                return StatusCode(403, new { success = false, message = "Bạn chỉ thao tác được trên giải do chính mình tạo." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Tên đội bóng không được để trống." });

            // Kiem tra han muc so doi cua goi
            var teamQuotaError = await CheckTeamQuotaAsync(tournament, 1);
            if (teamQuotaError != null) return StatusCode(403, teamQuotaError);

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

        // 1 doi trong danh sach nhap hang loat (co the kem logo)
        public class BulkTeamItemDto
        {
            public string Name { get; set; } = string.Empty;
            public string? LogoUrl { get; set; }   // base64 hoac URL, co the null
        }

        // DTO cho nhap nhieu doi cung luc.
        // Ho tro 2 kieu gui:
        //   - Names: ["Doi A","Doi B"]                       (chi ten)
        //   - Teams: [{ name:"Doi A", logoUrl:"data:..." }]  (ten + logo, tu file nen/anh)
        public class BulkTeamsDto
        {
            public List<string>? Names { get; set; }
            public List<BulkTeamItemDto>? Teams { get; set; }
        }

        /// <summary>
        /// Nhap NHIEU doi cung luc (1 request).
        /// Tu bo ten trong, tu loai trung (ca trong danh sach gui len lan da co trong giai).
        /// </summary>
        [HttpPost("tournaments/{tournamentId}/teams/bulk")]
        [Authorize]
        public async Task<IActionResult> CreateBulk(int tournamentId, [FromBody] BulkTeamsDto dto)
        {
            if (tournamentId <= 0)
                return BadRequest(new { success = false, message = "Thiếu hoặc sai ID giải đấu." });

            if (!await CanManageTournamentAsync(tournamentId))
                return StatusCode(403, new { success = false, message = "Bạn chỉ thao tác được trên giải do chính mình tạo." });

            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            // Gop 2 kieu gui (Names hoac Teams) thanh 1 danh sach chung
            var incoming = new List<BulkTeamItemDto>();
            if (dto?.Teams != null && dto.Teams.Count > 0)
                incoming.AddRange(dto.Teams);
            if (dto?.Names != null && dto.Names.Count > 0)
                incoming.AddRange(dto.Names.Select(n => new BulkTeamItemDto { Name = n }));

            if (incoming.Count == 0)
                return BadRequest(new { success = false, message = "Danh sách đội trống." });

            // Gioi han so luong 1 lan gui de tranh qua tai
            const int MaxPerRequest = 200;
            if (incoming.Count > MaxPerRequest)
                return BadRequest(new { success = false, message = $"Tối đa {MaxPerRequest} đội mỗi lần nhập." });

            // Ten da co san trong giai (so sanh khong phan biet hoa thuong)
            var existing = await _context.Teams
                .Where(t => t.TournamentId == tournamentId)
                .Select(t => t.Name)
                .ToListAsync();
            var existingSet = new HashSet<string>(
                existing.Select(n => (n ?? string.Empty).Trim().ToLowerInvariant()));

            var toAdd = new List<Team>();
            var skipped = new List<string>();
            var seen = new HashSet<string>();   // chong trung ngay trong danh sach gui len

            foreach (var item in incoming)
            {
                var name = (item?.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;      // bo dong trong
                if (name.Length > 100) name = name.Substring(0, 100);

                var key = name.ToLowerInvariant();
                if (existingSet.Contains(key) || !seen.Add(key))
                {
                    skipped.Add(name);                              // trung -> bo qua
                    continue;
                }

                toAdd.Add(new Team
                {
                    Name = name,
                    LogoUrl = string.IsNullOrWhiteSpace(item?.LogoUrl) ? null : item!.LogoUrl,
                    TournamentId = tournamentId,
                    Status = "Đã duyệt"
                });
            }

            // Kiem tra han muc so doi truoc khi luu ca loat
            var bulkQuotaError = await CheckTeamQuotaAsync(tournament, toAdd.Count);
            if (bulkQuotaError != null) return StatusCode(403, bulkQuotaError);

            if (toAdd.Count == 0)
                return Ok(new
                {
                    success = true,
                    message = "Không có đội mới nào được thêm (tất cả đã tồn tại hoặc trống).",
                    added = 0,
                    skipped = skipped.Count,
                    skippedNames = skipped
                });

            _context.Teams.AddRange(toAdd);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã thêm {toAdd.Count} đội." + (skipped.Count > 0 ? $" Bỏ qua {skipped.Count} đội trùng." : ""),
                added = toAdd.Count,
                skipped = skipped.Count,
                skippedNames = skipped,
                data = toAdd.Select(t => new { t.TeamId, t.Name, t.LogoUrl, t.TournamentId })
            });
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
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromBody] TeamDto dto)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            if (!await CanManageTeamAsync(id))
                return StatusCode(403, new { success = false, message = "Bạn chỉ thao tác được trên giải do chính mình tạo." });

            if (!string.IsNullOrWhiteSpace(dto.Name)) team.Name = dto.Name.Trim();
            if (dto.LogoUrl != null) team.LogoUrl = dto.LogoUrl;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật đội thành công!", data = team });
        }

        [HttpDelete("teams/{id}")]
        [Authorize]
        public async Task<IActionResult> Delete(int id)
        {
            var team = await _context.Teams.FindAsync(id);
            if (team == null)
                return NotFound(new { success = false, message = $"Không tìm thấy đội ID = {id}." });

            if (!await CanManageTeamAsync(id))
                return StatusCode(403, new { success = false, message = "Bạn chỉ thao tác được trên giải do chính mình tạo." });

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
        [Authorize]
        public async Task<IActionResult> SaveGroups(int tournamentId, [FromBody] SaveGroupsDto dto)
        {
            // Giai chua kich hoat (chua tra phi) -> chan
            var notPaid = await BlockIfNotActivatedAsync(tournamentId);
            if (notPaid != null) return notPaid;

            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {tournamentId}." });

            // Admin: moi giai. Nguoi khac: chi giai do chinh minh tao.
            if (!await CanManageTournamentAsync(tournamentId))
                return StatusCode(403, new
                {
                    success = false,
                    code = "NOT_OWNER",
                    message = "Bạn chỉ chia bảng được cho giải do chính mình tạo."
                });

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