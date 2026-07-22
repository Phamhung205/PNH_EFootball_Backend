using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Appwebbongda.Controllers
{
    /// <summary>
    /// Kho doi CA NHAN — moi user co danh sach doi rieng de dung lai.
    /// Tu dong luu khi them doi vao giai, chon lai khi tao giai khac.
    /// </summary>
    [ApiController]
    [Route("api/team-library")]
    [Authorize]
    public class TeamLibraryController : ControllerBase
    {
        private readonly AppDbContext _context;
        public TeamLibraryController(AppDbContext context) => _context = context;

        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        /// <summary>
        /// GET /api/team-library — Lay toan bo kho doi cua user hien tai.
        /// Sap xep: doi dung gan day nhat len dau.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyLibrary([FromQuery] string? q)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

            var query = _context.TeamLibraries.Where(t => t.UserId == uid.Value);

            // Tim theo ten (o o chon doi)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim().ToLower();
                query = query.Where(t => t.Name.ToLower().Contains(key));
            }

            var list = await query
                .OrderByDescending(t => t.LastUsedAt)
                .Select(t => new { t.Id, t.Name, t.LogoUrl, t.LastUsedAt })
                .ToListAsync();

            return Ok(new { success = true, data = list });
        }

        /// <summary>
        /// POST /api/team-library — Them 1 doi vao kho (hoac cap nhat neu trung ten).
        /// Goi khi user tu them doi, hoac tu dong khi them doi vao giai.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddToLibrary([FromBody] TeamLibraryDto dto)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
            if (string.IsNullOrWhiteSpace(dto?.Name))
                return BadRequest(new { success = false, message = "Thiếu tên đội." });

            var ten = dto.Name.Trim();

            // Trung ten (khong phan biet hoa thuong) -> cap nhat logo + thoi gian dung
            var existing = await _context.TeamLibraries
                .FirstOrDefaultAsync(t => t.UserId == uid.Value && t.Name.ToLower() == ten.ToLower());

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.LogoUrl)) existing.LogoUrl = dto.LogoUrl;
                existing.LastUsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return Ok(new { success = true, data = new { existing.Id, existing.Name, existing.LogoUrl }, updated = true });
            }

            var item = new TeamLibrary
            {
                UserId = uid.Value,
                Name = ten,
                LogoUrl = dto.LogoUrl,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };
            _context.TeamLibraries.Add(item);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, data = new { item.Id, item.Name, item.LogoUrl }, updated = false });
        }

        /// <summary>
        /// POST /api/team-library/bulk — Them nhieu doi cung luc vao kho.
        /// Dung khi them hang loat doi vao giai -> luu het vao kho mot lan.
        /// Bo qua doi trung ten (chi cap nhat thoi gian dung).
        /// </summary>
        [HttpPost("bulk")]
        public async Task<IActionResult> AddBulk([FromBody] BulkTeamLibraryDto dto)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
            if (dto?.Teams == null || dto.Teams.Count == 0)
                return Ok(new { success = true, added = 0 });

            // Lay san danh sach ten da co de khoi query tung cai
            var daCo = await _context.TeamLibraries
                .Where(t => t.UserId == uid.Value)
                .ToDictionaryAsync(t => t.Name.ToLower(), t => t);

            int them = 0;
            var now = DateTime.UtcNow;
            foreach (var d in dto.Teams)
            {
                var ten = (d?.Name ?? "").Trim();
                if (ten.Length == 0) continue;
                var key = ten.ToLower();

                if (daCo.TryGetValue(key, out var cu))
                {
                    // Da co -> cap nhat logo neu doi moi co, va thoi gian dung
                    if (!string.IsNullOrWhiteSpace(d.LogoUrl)) cu.LogoUrl = d.LogoUrl;
                    cu.LastUsedAt = now;
                }
                else
                {
                    var item = new TeamLibrary
                    {
                        UserId = uid.Value,
                        Name = ten,
                        LogoUrl = d.LogoUrl,
                        CreatedAt = now,
                        LastUsedAt = now
                    };
                    _context.TeamLibraries.Add(item);
                    daCo[key] = item;   // tranh trung trong cung lo
                    them++;
                }
            }
            await _context.SaveChangesAsync();
            return Ok(new { success = true, added = them });
        }

        /// <summary>
        /// DELETE /api/team-library/{id} — Xoa 1 doi khoi kho.
        /// Chi xoa duoc doi trong kho CUA MINH.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Chưa đăng nhập." });

            var item = await _context.TeamLibraries.FindAsync(id);
            if (item == null) return NotFound(new { success = false, message = "Không tìm thấy đội trong kho." });
            if (item.UserId != uid.Value)
                return StatusCode(403, new { success = false, message = "Đây không phải kho đội của bạn." });

            _context.TeamLibraries.Remove(item);
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        public class TeamLibraryDto
        {
            public string Name { get; set; } = string.Empty;
            public string? LogoUrl { get; set; }
        }

        public class BulkTeamLibraryDto
        {
            public List<TeamLibraryDto> Teams { get; set; } = new();
        }
    }
}