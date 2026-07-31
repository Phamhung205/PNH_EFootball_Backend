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
        private readonly IConfiguration _config;

        // ── PHI KICH HOAT GIAI ──
        // So giai MIEN PHI tron doi cho moi tai khoan.
        public const int FREE_TOURNAMENT_SLOTS = 2;
        // Muc phi theo quy mo giai (dong).
        // DUOI 32 doi  -> 15.000d
        // TU 32 doi tro len -> 25.000d (C1 36 doi, World Cup 48 doi...)
        public const int FEE_SMALL = 15000;
        public const int FEE_LARGE = 25000;
        public const int LARGE_THRESHOLD = 32;   // tu moc nay tro len tinh gia cao

        /// <summary>
        /// Tinh phi kich hoat theo so doi cua giai.
        /// He thong TU NHAN DIEN so doi user nhap, khong can chon goi.
        ///   duoi 32 doi     -> 15.000d
        ///   tu 32 doi tro len -> 25.000d
        /// </summary>
        public static int TinhPhiKichHoat(int maxTeams)
            => maxTeams >= LARGE_THRESHOLD ? FEE_LARGE : FEE_SMALL;

        public TournamentsController(AppDbContext context, ISubscriptionService subscription, IConfiguration config)
        {
            _context = context;
            _subscription = subscription;
            _config = config;
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

            // ── KHONG CON CHAN SO LUONG GIAI ──
            // Truoc day: het 2 giai free la KHONG tao them duoc.
            // Bay gio: ai cung tao duoc bao nhieu giai tuy thich, nhung tu giai
            // thu 3 tro di phai TRA PHI moi mo khoa duoc (chia bang, xep lich...).
            // Viec tinh phi nam o ham Create, khong chan o day nua.
            return null;
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
                        // KHONG tra AvatarUrl o day: anh la chuoi base64 dai,
                        // 20 giai = ~120KB tai them, trong khi the giai chi hien
                        // anh o 4x4 pixel. Frontend se hien chu cai dau ten thay the.
                        CreatedByAvatar = (string?)null,
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

            // ── SUAT MIEN PHI TRON DOI ──
            // 2 giai dau tien cua moi tai khoan la mien phi.
            // Dem theo TournamentsCreated (tron doi) nen xoa giai KHONG lay lai suat,
            // tranh viec tao/xoa lien tuc de dung mien phi mai.
            // Admin luon mien phi.
            int soGiaiDaTao = 0;
            if (!IsAdmin())
            {
                var creatorForCount = await _context.Users.FindAsync(creatorId.Value);
                soGiaiDaTao = creatorForCount?.TournamentsCreated ?? 0;
            }
            bool duocMienPhi = IsAdmin() || soGiaiDaTao < FREE_TOURNAMENT_SLOTS;

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
                ChatEnabled = dto.ChatEnabled ?? false,

                // ── PHI KICH HOAT ──
                IsFree = duocMienPhi,
                IsPaid = duocMienPhi,                       // mien phi = coi nhu da mo khoa
                ActivationFee = duocMienPhi ? 0 : TinhPhiKichHoat(dto.MaxTeams ?? 16),
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
            if (dto.MaxTeams.HasValue)
            {
                tournament.MaxTeams = dto.MaxTeams.Value;
                // TU TINH LAI PHI khi doi so doi.
                // Vd: tao giai 16 doi (15k) roi sua thanh 36 doi -> phai thanh 25k.
                // Chi tinh lai khi giai CHUA tra tien va KHONG phai suat mien phi,
                // tranh doi phi cua giai da thanh toan xong.
                if (!tournament.IsPaid && !tournament.IsFree)
                    tournament.ActivationFee = TinhPhiKichHoat(tournament.MaxTeams);
            }
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
                // Dung SQL truc tiep thay vi tai het vao bo nho roi RemoveRange.
                // Ly do: EF chi xoa duoc nhung ban ghi no DA THEO DOI, neu co ban ghi
                // "mo coi" (vd tran tro toi doi da bi xoa) thi RemoveRange bo sot,
                // va rang buoc khoa ngoai van chan -> loi khong ro nguyen nhan.
                var sql = _context.Database;

                // 1. Matches — tro toi Teams va Tournament
                var soTran = await sql.ExecuteSqlRawAsync(
                    "DELETE FROM Matches WHERE TournamentId = {0}", id);

                // 2. ChatMessages — tro toi Tournament
                var soChat = await sql.ExecuteSqlRawAsync(
                    "DELETE FROM ChatMessages WHERE TournamentId = {0}", id);

                // 3. Registrations — tro toi Tournament va Teams
                var soDangKy = await sql.ExecuteSqlRawAsync(
                    "DELETE FROM Registrations WHERE TournamentId = {0}", id);

                // 4. Teams — phai xoa SAU Matches va Registrations
                var soDoi = await sql.ExecuteSqlRawAsync(
                    "DELETE FROM Teams WHERE TournamentId = {0}", id);

                // 5. Groups
                var soBang = await sql.ExecuteSqlRawAsync(
                    "DELETE FROM Groups WHERE TournamentId = {0}", id);

                // 6. Cuoi cung moi xoa chinh giai
                await sql.ExecuteSqlRawAsync(
                    "DELETE FROM Tournaments WHERE TournamentId = {0}", id);

                await tx.CommitAsync();

                Console.WriteLine($"[Delete] Da xoa giai #{id}: {soTran} tran, {soDoi} doi, " +
                                  $"{soBang} bang, {soDangKy} dang ky, {soChat} tin nhan.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();   // loi giua chung -> tra ve nguyen trang, khong mat du lieu

                // Ghi ro nguyen nhan ra log de con biet duong sua
                var goc = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"[Delete] LOI xoa giai #{id}: {goc}");

                // Tra ca nguyen nhan goc ve cho frontend, khong giau nua.
                // Truoc day chi bao "Khong xoa duoc" nen khong biet vuong bang nao.
                return StatusCode(500, new
                {
                    success = false,
                    code = "DELETE_FAILED",
                    message = "Không xóa được giải đấu. Dữ liệu đã được giữ nguyên. Chi tiết: " + goc,
                    detail = goc
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

                // CHUA TRA PHI -> khong duoc chuyen sang "Dang dien ra" hay "Hoan thanh".
                // Giai phai duoc kich hoat thi moi bat dau thi dau duoc.
                // Van cho de "Sap khoi tranh" (trang thai mac dinh khi moi tao).
                if (!tournament.IsPaid && !tournament.IsFree && dto.Status != "Sắp khởi tranh")
                {
                    var phi = tournament.ActivationFee > 0 ? tournament.ActivationFee
                            : TinhPhiKichHoat(tournament.MaxTeams);
                    return StatusCode(402, new
                    {
                        success = false,
                        code = "TOURNAMENT_NOT_ACTIVATED",
                        tournamentId = tournament.TournamentId,
                        fee = phi,
                        message = $"Giải chưa được kích hoạt nên không thể bắt đầu. "
                                + $"Vui lòng thanh toán {phi:N0}đ để mở khóa giải."
                    });
                }

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
        // ===================================================================
        // PHI KICH HOAT GIAI
        // ===================================================================

        /// <summary>
        /// Bo dau tieng Viet va ky tu dac biet.
        /// Napas/VietQR chi chap nhan chu khong dau; de dau se lam nhieu app
        /// ngan hang bao loi hoac cat mat chuoi.
        /// </summary>
        private static string BoDau(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var norm = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in norm)
            {
                // Bo cac dau thanh/dau mu
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (c == 'đ') { sb.Append('d'); continue; }
                if (c == 'Đ') { sb.Append('D'); continue; }
                // Chi giu chu, so va khoang trang
                if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
                else sb.Append(' ');
            }
            // Gop nhieu khoang trang lien tiep thanh mot
            var result = System.Text.RegularExpressions.Regex
                .Replace(sb.ToString(), @"\s+", " ").Trim();
            return result;
        }

        /// <summary>
        /// Tao noi dung chuyen khoan: "PNH{id} {ten giai} {nguoi tao}".
        ///
        /// Ma PNH{id} LUON dung dau va khong bao gio bi cat — day la thu Admin
        /// dung de biet tien tra cho giai nao. Ten giai va ten nguoi chi la
        /// thong tin phu, se bi cat neu vuot 50 ky tu (gioi han cua Napas).
        /// </summary>
        private static string TaoNoiDungCK(int tournamentId, string? tenGiai, string? nguoiTao)
        {
            const int MAX = 50;
            var ma = $"PNH{tournamentId}";
            var phan = BoDau(tenGiai);
            var nguoi = BoDau(nguoiTao);

            var noiDung = ma;
            // Them ten giai neu con cho
            if (phan.Length > 0 && noiDung.Length + 1 + phan.Length <= MAX)
                noiDung += " " + phan;
            else if (phan.Length > 0 && noiDung.Length + 2 < MAX)
                noiDung += " " + phan.Substring(0, MAX - noiDung.Length - 1);

            // Them nguoi tao neu van con cho
            if (nguoi.Length > 0 && noiDung.Length + 1 + nguoi.Length <= MAX)
                noiDung += " " + nguoi;

            return noiDung.Length > MAX ? noiDung.Substring(0, MAX).Trim() : noiDung;
        }

        /// <summary>
        /// GET /api/tournaments/{id}/activation
        /// Frontend goi de biet giai da mo khoa chua, con thieu bao nhieu tien,
        /// va lay thong tin chuyen khoan + ma QR.
        /// </summary>
        [HttpGet("{id}/activation")]
        public async Task<IActionResult> GetActivation(int id)
        {
            var t = await _context.Tournaments.FindAsync(id);
            if (t == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // Lay ten nguoi tao de ghep vao noi dung chuyen khoan
            string? tenNguoiTao = null;
            if (t.CreatedByUserId != null)
            {
                var owner = await _context.Users.FindAsync(t.CreatedByUserId.Value);
                tenNguoiTao = owner?.FullName ?? owner?.Email;
            }

            // Noi dung day du: "PNH8 World Cup 2026 Pham Ngoc Hung"
            var maDoiSoat = TaoNoiDungCK(t.TournamentId, t.Name, tenNguoiTao);

            // ── DEM SO DOI THAT DA NHAP ──
            // Phi tinh theo so doi THUC TE trong giai, khong phai con so MaxTeams
            // khai bao luc tao. Chua nhap doi nao thi chua tinh duoc phi.
            var soDoi = await _context.Teams.CountAsync(x => x.TournamentId == t.TournamentId);
            var coDoi = soDoi > 0;

            // Chua co doi -> phi = 0 va bao frontend biet de hien loi nhac nhap doi
            var phi = coDoi ? TinhPhiKichHoat(soDoi) : 0;

            // Dong bo lai vao DB neu lech (giai chua tra tien)
            if (coDoi && !t.IsPaid && !t.IsFree && t.ActivationFee != phi)
            {
                t.ActivationFee = phi;
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    tournamentId = t.TournamentId,
                    isPaid = t.IsPaid,
                    isFree = t.IsFree,
                    fee = t.IsPaid ? 0 : phi,
                    maxTeams = t.MaxTeams,
                    // So doi THUC TE da nhap — frontend dung de biet co hien QR hay khong
                    teamCount = soDoi,
                    hasTeams = coDoi,
                    paidAt = t.PaidAt,
                    // BTC da bam "Toi da chuyen khoan" chua (de an/hien nut)
                    claimed = t.PaymentClaimedAt != null,
                    claimedAt = t.PaymentClaimedAt,
                    paymentNote = maDoiSoat,
                    // Thong tin chuyen khoan — doc tu cau hinh, khong hard-code
                    bank = _config["Payment:BankCode"] ?? "",
                    bankName = _config["Payment:BankName"] ?? "",
                    accountNumber = _config["Payment:AccountNumber"] ?? "",
                    accountName = _config["Payment:AccountName"] ?? "",
                    // Chua co doi -> KHONG sinh QR (so tien = 0, quet vao se sai)
                    qrUrl = coDoi ? BuildVietQrUrl(phi, maDoiSoat) : "",
                    // Lien he Zalo de duyet nhanh
                    zaloPhone = _config["Payment:ZaloPhone"] ?? "",
                    zaloName = _config["Payment:ZaloName"] ?? "",
                }
            });
        }

        /// <summary>
        /// Tao link anh QR VietQR (mien phi, khong can dang ky).
        /// Nguoi dung quet la app ngan hang tu dien so tien + noi dung -> giam sai sot.
        /// </summary>
        private string BuildVietQrUrl(int amount, string note)
        {
            var bank = _config["Payment:BankCode"];
            var acc = _config["Payment:AccountNumber"];
            var name = _config["Payment:AccountName"] ?? "";
            if (string.IsNullOrWhiteSpace(bank) || string.IsNullOrWhiteSpace(acc))
            {
                // Ghi log RO RANG de biet ngay nguyen nhan khi QR khong hien —
                // truoc day loi nay am tham, phai doan mo hinh moi tim ra.
                Console.WriteLine("[BuildVietQrUrl] THIEU CAU HINH: Payment:BankCode="
                    + $"'{bank}', Payment:AccountNumber='{acc}'. Kiem tra appsettings.json "
                    + "(hoac bien moi truong tren Northflank) co muc 'Payment' day du khong.");
                return "";
            }
            return $"https://img.vietqr.io/image/{bank}-{acc}-compact2.png"
                 + $"?amount={amount}&addInfo={Uri.EscapeDataString(note)}"
                 + $"&accountName={Uri.EscapeDataString(name)}";
        }

        /// <summary>
        /// POST /api/tournaments/{id}/claim-payment
        /// BTC bam "Tôi đã chuyển khoản" -> danh dau de Admin biet ma kiem tra truoc.
        /// KHONG mo khoa giai — chi Admin duyet moi mo khoa duoc.
        /// </summary>
        [HttpPost("{id}/claim-payment")]
        [Authorize]
        public async Task<IActionResult> ClaimPayment(int id)
        {
            var t = await _context.Tournaments.FindAsync(id);
            if (t == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // Chi nguoi tao giai (hoac Admin) moi bao duoc
            if (!CanEditTournament(t))
                return StatusCode(403, new { success = false, message = "Bạn chỉ báo được cho giải do chính mình tạo." });

            if (t.IsPaid || t.IsFree)
                return Ok(new { success = true, message = "Giải này đã được mở khóa rồi." });

            t.PaymentClaimedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã ghi nhận. Quản trị viên sẽ kiểm tra và mở khóa giải sớm nhất."
            });
        }

        /// <summary>
        /// POST /api/tournaments/{id}/confirm-payment — CHI ADMIN
        /// Admin doi chieu sao ke ngan hang roi bam xac nhan de mo khoa giai.
        /// </summary>
        [HttpPost("{id}/confirm-payment")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ConfirmPayment(int id)
        {
            var t = await _context.Tournaments.FindAsync(id);
            if (t == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            if (t.IsPaid)
                return Ok(new { success = true, message = "Giải này đã được mở khóa từ trước." });

            t.IsPaid = true;
            t.PaidAt = DateTime.UtcNow;
            t.PaymentNote = $"PNH{t.TournamentId}";
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã mở khóa giải đấu.", data = t });
        }

        /// <summary>
        /// POST /api/tournaments/{id}/revoke-payment — CHI ADMIN
        /// Thu hoi khi xac nhan nham (vd chuyen khoan bi hoan).
        /// </summary>
        [HttpPost("{id}/revoke-payment")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RevokePayment(int id)
        {
            var t = await _context.Tournaments.FindAsync(id);
            if (t == null)
                return NotFound(new { success = false, message = $"Không tìm thấy giải đấu ID = {id}." });

            // Giai nam trong suat mien phi thi khong the thu hoi
            if (t.IsFree)
                return BadRequest(new { success = false, message = "Giải này thuộc suất miễn phí, không thể thu hồi." });

            t.IsPaid = false;
            t.PaidAt = null;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Đã thu hồi trạng thái thanh toán." });
        }

        /// <summary>
        /// GET /api/tournaments/pending-payments?q=&amp;status= — CHI ADMIN
        /// Danh sach giai cho duyet thanh toan.
        /// q      : tim theo Gmail HOAC ten nguoi dang ky HOAC ten giai HOAC ma doi soat
        /// status : "pending" (mac dinh) | "approved" | "all"
        /// </summary>
        [HttpGet("pending-payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PendingPayments([FromQuery] string? q, [FromQuery] string? status)
        {
            // Chi xet cac giai CO TINH PHI (bo qua giai mien phi tron doi)
            var query = _context.Tournaments.Where(t => !t.IsFree);

            // Loc theo trang thai duyet
            var st = (status ?? "pending").ToLowerInvariant();
            if (st == "pending") query = query.Where(t => !t.IsPaid);
            else if (st == "approved") query = query.Where(t => t.IsPaid);
            // "all" -> khong loc

            // Ghep voi bang Users de tim duoc theo Gmail / ten nguoi dang ky
            var joined = from t in query
                         join u in _context.Users on t.CreatedByUserId equals u.Id into gj
                         from u in gj.DefaultIfEmpty()
                         select new
                         {
                             t.TournamentId,
                             t.Name,
                             t.MaxTeams,
                             t.ActivationFee,
                             t.IsPaid,
                             t.PaidAt,
                             t.PaymentClaimedAt,
                             t.Status,
                             t.Format,
                             ownerEmail = u != null ? u.Email : null,
                             ownerName = u != null ? (u.FullName ?? u.Email) : null,
                             ownerId = u != null ? (int?)u.Id : null,
                         };

            // Tim kiem: khop Gmail, ten nguoi, ten giai, hoac ma doi soat (PNH12 / 12)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim().ToLower();
                // Cho phep go "PNH12" hoac "12" de tim thang theo ma doi soat
                var keyId = key.StartsWith("pnh") ? key.Substring(3) : key;
                int.TryParse(keyId, out int idFromKey);

                joined = joined.Where(x =>
                    (x.ownerEmail != null && x.ownerEmail.ToLower().Contains(key)) ||
                    (x.ownerName != null && x.ownerName.ToLower().Contains(key)) ||
                    x.Name.ToLower().Contains(key) ||
                    (idFromKey > 0 && x.TournamentId == idFromKey));
            }

            var list = await joined
                // Giai DA BAO chuyen khoan len dau de Admin kiem tra truoc
                .OrderByDescending(x => x.PaymentClaimedAt != null)
                .ThenByDescending(x => x.PaymentClaimedAt)
                .ThenByDescending(x => x.TournamentId)
                .Take(200)
                .ToListAsync();

            // Them ma doi soat vao ket qua (tinh ngoai DB cho don gian)
            var result = list.Select(x => new
            {
                x.TournamentId,
                x.Name,
                x.MaxTeams,
                x.ActivationFee,
                x.IsPaid,
                x.PaidAt,
                x.PaymentClaimedAt,
                // BTC da bao chuyen khoan -> Admin uu tien kiem tra truoc
                claimed = x.PaymentClaimedAt != null,
                x.Status,
                x.Format,
                x.ownerEmail,
                x.ownerName,
                x.ownerId,
                // Noi dung CK day du, GIONG HET cai BTC nhin thay khi chuyen khoan
                paymentNote = TaoNoiDungCK(x.TournamentId, x.Name, x.ownerName),
                // Ma ngan de tim nhanh trong sao ke
                shortCode = "PNH" + x.TournamentId,
            });

            return Ok(new { success = true, data = result });
        }
    }
}