using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using Appwebbongda.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TournamentsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ISubscriptionService _subscription;

        public TournamentsController(AppDbContext context, ISubscriptionService subscription)
        {
            _context = context;
            _subscription = subscription;
        }

        /// <summary>
        /// Kiem tra han muc so giai cua goi hien tai.
        /// Tra ve null neu duoc phep tao; nguoc lai tra ve thong bao loi.
        /// Admin khong bi gioi han.
        /// </summary>
        private async Task<object?> CheckTournamentQuotaAsync()
        {
            if (IsAdmin()) return null;                    // Admin: khong gioi han

            var uid = GetCurrentUserId();
            if (uid == null)
            {
                // TU CHOI thay vi cho qua: neu khong biet la ai thi khong the tinh han muc,
                // de lot se thanh lo hong tao giai khong gioi han.
                return new
                {
                    success = false,
                    code = "AUTH_NO_USER_ID",
                    message = "Không xác định được tài khoản. Vui lòng đăng xuất rồi đăng nhập lại."
                };
            }

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null)
            {
                return new
                {
                    success = false,
                    code = "AUTH_USER_NOT_FOUND",
                    message = "Không tìm thấy tài khoản. Vui lòng đăng nhập lại."
                };
            }

            // Goi het han -> tu ha ve free truoc khi tinh han muc
            if (_subscription.ApplyExpiryIfNeeded(user))
                await _context.SaveChangesAsync();

            var plan = _subscription.GetPlan(user.Plan);
            if (plan.MaxTournaments < 0) return null;      // -1 = khong gioi han

            var isFree = string.Equals(user.Plan, "free", StringComparison.OrdinalIgnoreCase);

            // ── CACH DEM ──
            // FREE (dung thu): dem TRON DOI -> xoa giai cu roi tao lai KHONG lay lai luot.
            //                  Chong viec tao/xoa lien tuc de dung mai mien phi.
            // TRA PHI: dem so giai DANG CO -> da tra tien thi xoa bot duoc tao lai.
            int used = isFree
                ? user.TournamentsCreated
                : await _context.Tournaments.CountAsync(t => t.CreatedByUserId == uid.Value);

            if (used < plan.MaxTournaments) return null;   // con han muc
            return new
            {
                success = false,
                code = "PLAN_LIMIT_TOURNAMENTS",
                plan = user.Plan,
                used,
                limit = plan.MaxTournaments,
                message = isFree
                    ? $"Bạn đã dùng hết {plan.MaxTournaments} lượt tạo giải của bản dùng thử. Lượt đã dùng không lấy lại được kể cả khi xóa giải. Vui lòng đăng ký gói để tạo thêm."
                    : $"Gói {plan.Name} chỉ cho phép {plan.MaxTournaments} giải đấu. Vui lòng nâng cấp gói."
            };
        }

        // Lay id user tu token
        private int? GetCurrentUserId()
        {
            // Doc nhieu dang ten claim khac nhau de chac chan lay duoc id:
            // ASP.NET co the anh xa "sub" -> NameIdentifier, hoac giu nguyen ten goc.
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub")
                   ?? User.FindFirstValue("nameid")
                   ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        // Lay role tu token (Admin / BTC / User)
        private string GetCurrentRole() =>
            User.FindFirstValue(ClaimTypes.Role) ?? "User";

        private bool IsAdmin() =>
            string.Equals(GetCurrentRole(), "Admin", StringComparison.OrdinalIgnoreCase);

        // QUYEN SUA/XOA GIAI:
        //   - Admin        : lam duoc tren MOI giai cua BAT KY ai
        //   - Nguoi khac   : CHI tren giai do CHINH MINH tao
        //   - Giai khong co chu (CreatedByUserId = null): chi Admin xu ly duoc
        private bool CanEditTournament(Tournament t)
        {
            if (IsAdmin()) return true;
            var uid = GetCurrentUserId();
            return uid != null && t.CreatedByUserId == uid.Value;
        }

        public class UpdateStatusDto
        {
            public string Status { get; set; } = string.Empty;
        }

        // DTO tạo / cập nhật giải đấu
        public class TournamentDto
        {
            public string Name { get; set; } = string.Empty;
            public string Format { get; set; } = "League";
            public string? Status { get; set; }
            public string? Description { get; set; }
            public int? MaxTeams { get; set; }
            public DateTime? StartDate { get; set; }
            // Cho phep dang ky tham du hay khong (admin bat/tat)
            public bool? AllowRegistration { get; set; }
            // Logo giai dau
            public string? LogoUrl { get; set; }
            public string? Season { get; set; }
            public bool? ChatEnabled { get; set; }
        }

        /// <summary>
        /// GET /api/tournaments — Lấy danh sách tất cả giải đấu (công khai)
        /// </summary>
        /// <summary>
        /// GET /api/tournaments — Danh sach giai dau.
        /// Tham so:
        ///   status = loc theo trang thai
        ///   mine=true = chi lay giai do CHINH MINH tao (trang "Giai dau cua toi")
        /// Luon kem ten nguoi tao de trang "Giai dau cong dong" phan biet duoc.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] bool mine = false)
        {
            var query = _context.Tournaments.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status == status);

            // Chi lay giai cua chinh minh
            if (mine)
            {
                var uid = GetCurrentUserId();
                if (uid == null)
                    return Ok(new { success = true, data = new List<object>() });
                query = query.Where(t => t.CreatedByUserId == uid.Value);
            }

            // Join sang Users de lay ten nguoi tao (left join: giai cu co the khong co nguoi tao)
            var tournaments = await query
                .GroupJoin(_context.Users,
                    t => t.CreatedByUserId,
                    u => u.Id,
                    (t, us) => new { t, us })
                .SelectMany(x => x.us.DefaultIfEmpty(),
                    (x, u) => new
                    {
                        x.t.TournamentId,
                        x.t.Name,
                        x.t.Format,
                        x.t.Status,
                        x.t.Description,
                        x.t.MaxTeams,
                        x.t.NumberOfGroups,
                        x.t.TeamsAdvancingPerGroup,
                        x.t.StartDate,
                        x.t.LogoUrl,
                        x.t.Season,
                        x.t.AllowRegistration,
                        x.t.ChatEnabled,
                        x.t.RatingSum,
                        x.t.RatingCount,
                        x.t.CreatedByUserId,
                        // Ten nguoi tao — hien o trang cong dong
                        CreatedByName = u != null ? u.FullName : null,
                        CreatedByAvatar = u != null ? u.AvatarUrl : null,
                        // So doi cua giai (de the giai hien dung so luong)
                        TeamCount = _context.Teams.Count(te => te.TournamentId == x.t.TournamentId)
                    })
                .ToListAsync();

            return Ok(new { success = true, data = tournaments });
        }

        /// <summary>
        /// GET /api/tournaments/{id} — Chi tiết giải đấu (công khai)
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var tournament = await _context.Tournaments
                .Include(t => t.Teams)
                .FirstOrDefaultAsync(t => t.TournamentId == id);

            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            return Ok(new { success = true, data = tournament });
        }

        /// <summary>
        /// POST /api/tournaments — Tạo giải đấu mới (CHỈ ADMIN)
        /// </summary>
        [HttpPost]
        // Moi tai khoan da dang nhap deu tao duoc giai.
        // So luong bi gioi han theo goi (CheckTournamentQuotaAsync).
        [Authorize]
        public async Task<IActionResult> Create([FromBody] TournamentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Tên giải đấu không được để trống." });

            // Kiem tra han muc goi truoc khi tao
            var quotaError = await CheckTournamentQuotaAsync();
            if (quotaError != null) return StatusCode(403, quotaError);

            // Bat buoc xac dinh duoc nguoi tao: giai khong co chu se khong hien o
            // "Giai cua toi" va khong ai sua duoc -> chan tu dau.
            var creatorId = GetCurrentUserId();
            if (creatorId == null)
                return StatusCode(403, new
                {
                    success = false,
                    code = "AUTH_NO_USER_ID",
                    message = "Không xác định được tài khoản. Vui lòng đăng xuất rồi đăng nhập lại."
                });

            var tournament = new Tournament
            {
                Name = dto.Name,
                Format = dto.Format ?? "League",
                Status = dto.Status ?? "Sắp khởi tranh",
                Description = dto.Description,
                MaxTeams = dto.MaxTeams ?? 16,
                StartDate = dto.StartDate ?? DateTime.Now,
                // Luu nguoi tao giai (de BTC chi sua duoc giai cua minh)
                CreatedByUserId = creatorId,
                // Cho phep dang ky hay khong (mac dinh false neu khong gui)
                AllowRegistration = dto.AllowRegistration ?? false,
                LogoUrl = dto.LogoUrl,
                Season = dto.Season,
                ChatEnabled = dto.ChatEnabled ?? false
            };

            _context.Tournaments.Add(tournament);

            // Tang bo dem TRON DOI cua nguoi tao (Admin khong bi tinh luot)
            if (!IsAdmin())
            {
                var creator = await _context.Users.FindAsync(creatorId.Value);
                if (creator != null)
                {
                    creator.TournamentsCreated += 1;
                    Console.WriteLine($"[Quota] {creator.Email} da dung {creator.TournamentsCreated} luot tao giai.");
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Tạo giải đấu thành công!", data = tournament });
        }

        /// <summary>
        /// PUT /api/tournaments/{id} — Cập nhật giải (ADMIN sửa mọi giải, BTC sửa giải mình tạo)
        /// </summary>
        [HttpPut("{id}")]
        // Chi Admin hoac NGUOI TAO giai moi sua duoc (CanEditTournament kiem tra)
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromBody] TournamentDto dto)
        {
            var tournament = await _context.Tournaments.FindAsync(id);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // BTC chi duoc sua giai do chinh minh tao
            if (!CanEditTournament(tournament))
                return StatusCode(403, new { success = false, code = "NOT_OWNER", message = "Bạn chỉ sửa được giải do chính mình tạo. Giải này thuộc về người khác." });

            if (!string.IsNullOrWhiteSpace(dto.Name)) tournament.Name = dto.Name;
            if (!string.IsNullOrWhiteSpace(dto.Format)) tournament.Format = dto.Format;
            if (!string.IsNullOrWhiteSpace(dto.Status)) tournament.Status = dto.Status;
            if (dto.Description != null) tournament.Description = dto.Description;
            if (dto.MaxTeams.HasValue) tournament.MaxTeams = dto.MaxTeams.Value;
            if (dto.StartDate.HasValue) tournament.StartDate = dto.StartDate.Value;
            if (dto.AllowRegistration.HasValue) tournament.AllowRegistration = dto.AllowRegistration.Value;
            if (dto.LogoUrl != null) tournament.LogoUrl = dto.LogoUrl;
            if (dto.Season != null) tournament.Season = dto.Season;
            if (dto.ChatEnabled.HasValue) tournament.ChatEnabled = dto.ChatEnabled.Value;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật giải đấu thành công!", data = tournament });
        }

        /// <summary>
        /// DELETE /api/tournaments/{id} — Xóa giải (ADMIN mọi giải, BTC giải mình tạo)
        /// </summary>
        [HttpDelete("{id}")]
        // Chi Admin hoac NGUOI TAO giai moi sua duoc (CanEditTournament kiem tra)
        [Authorize]
        public async Task<IActionResult> Delete(int id)
        {
            var tournament = await _context.Tournaments.FindAsync(id);
            if (tournament == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // BTC chi duoc xoa giai do chinh minh tao
            if (!CanEditTournament(tournament))
                return StatusCode(403, new { success = false, code = "NOT_OWNER", message = "Bạn chỉ xóa được giải do chính mình tạo. Giải này thuộc về người khác." });

            // ── XOA DU LIEU CON TRUOC ──
            // SQL Server chan xoa "giai cha" khi cac bang con con tro toi no
            // (loi: DELETE statement conflicted with the REFERENCE constraint).
            // Phai xoa dung THU TU PHU THUOC:
            //   Matches      -> tro toi Teams va Tournament
            //   ChatMessages -> tro toi Tournament
            //   Registrations-> tro toi Tournament va Teams
            //   Teams, Groups-> tro toi Tournament
            //   Tournament   -> xoa sau cung
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var matches = await _context.Matches.Where(m => m.TournamentId == id).ToListAsync();
                if (matches.Count > 0) _context.Matches.RemoveRange(matches);

                var chats = await _context.ChatMessages.Where(c => c.TournamentId == id).ToListAsync();
                if (chats.Count > 0) _context.ChatMessages.RemoveRange(chats);

                var regs = await _context.Registrations.Where(r => r.TournamentId == id).ToListAsync();
                if (regs.Count > 0) _context.Registrations.RemoveRange(regs);

                await _context.SaveChangesAsync();   // xoa xong nhom phu thuoc Teams

                var teams = await _context.Teams.Where(t => t.TournamentId == id).ToListAsync();
                if (teams.Count > 0) _context.Teams.RemoveRange(teams);

                var groups = await _context.Groups.Where(g => g.TournamentId == id).ToListAsync();
                if (groups.Count > 0) _context.Groups.RemoveRange(groups);

                await _context.SaveChangesAsync();

                _context.Tournaments.Remove(tournament);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                Console.WriteLine($"[Delete] Da xoa giai #{id}: {matches.Count} tran, {teams.Count} doi, " +
                                  $"{groups.Count} bang, {regs.Count} dang ky, {chats.Count} tin nhan.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();   // loi giua chung -> tra ve nguyen trang, khong mat du lieu
                return StatusCode(500, new
                {
                    success = false,
                    message = "Không xóa được giải đấu. Dữ liệu đã được giữ nguyên.",
                    detail = ex.InnerException?.Message ?? ex.Message
                });
            }

            return Ok(new { success = true, message = "Xóa giải đấu thành công!" });
        }

        /// <summary>
        /// PUT /api/tournaments/{id}/status — Cập nhật trạng thái (ADMIN mọi giải, BTC giải mình tạo)
        /// </summary>
        [HttpPut("{id}/status")]
        // Chi Admin hoac NGUOI TAO giai moi sua duoc (CanEditTournament kiem tra)
        [Authorize]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Status))
                    return BadRequest(new { success = false, message = "Trạng thái không được để trống." });

                var tournament = await _context.Tournaments.FindAsync(id);
                if (tournament == null)
                    return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

                // BTC chi duoc doi trang thai giai do chinh minh tao
                if (!CanEditTournament(tournament))
                    return StatusCode(403, new { success = false, code = "NOT_OWNER", message = "Bạn chỉ đổi được trạng thái giải do chính mình tạo." });

                tournament.Status = dto.Status;
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Cập nhật trạng thái thành công!", data = tournament });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi hệ thống.", error = ex.Message });
            }
        }

        // ===================================================================
        // #72: DANH GIA SAO cho giai dau (ai cung danh gia duoc, khong can dang nhap)
        // POST /api/Tournaments/{id}/rate   body: { stars: 1-5 }
        // ===================================================================
        public class RateDto { public int Stars { get; set; } }

        [HttpPost("{id}/rate")]
        public async Task<IActionResult> Rate(int id, [FromBody] RateDto dto)
        {
            if (dto.Stars < 1 || dto.Stars > 5)
                return BadRequest(new { success = false, message = "So sao phai tu 1 den 5." });

            var t = await _context.Tournaments.FindAsync(id);
            if (t == null) return NotFound(new { success = false, message = "Khong tim thay giai dau." });

            t.RatingSum += dto.Stars;
            t.RatingCount += 1;
            await _context.SaveChangesAsync();

            double avg = t.RatingCount > 0 ? (double)t.RatingSum / t.RatingCount : 0;
            return Ok(new { success = true, message = "Cam on ban da danh gia!", average = Math.Round(avg, 1), count = t.RatingCount });
        }

        // GET /api/Tournaments/{id}/rating - lay diem trung binh + so luot
        [HttpGet("{id}/rating")]
        public async Task<IActionResult> GetRating(int id)
        {
            var t = await _context.Tournaments.FindAsync(id);
            if (t == null) return NotFound(new { success = false, message = "Khong tim thay." });
            double avg = t.RatingCount > 0 ? (double)t.RatingSum / t.RatingCount : 0;
            return Ok(new { success = true, average = Math.Round(avg, 1), count = t.RatingCount });
        }
    }
}