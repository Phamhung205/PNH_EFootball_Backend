using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Appwebbongda.Data;
using Appwebbongda.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ChatController(AppDbContext context) { _context = context; }

        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        private string GetCurrentRole() => User.FindFirstValue(ClaimTypes.Role) ?? "User";

        // Kiem tra user co quyen vao chat giai nay khong:
        // - Admin: luon duoc
        // - BTC (nguoi tao giai): duoc
        // - User: phai da DANG KY giai nay
        private async Task<bool> CanAccessChat(int tournamentId, int userId)
        {
            var role = GetCurrentRole();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            // Nguoi tao giai (BTC) duoc vao
            var tournament = await _context.Tournaments.FindAsync(tournamentId);
            if (tournament != null && tournament.CreatedByUserId == userId)
                return true;

            // User thuong: phai da dang ky giai nay
            bool registered = await _context.Registrations
                .AnyAsync(r => r.TournamentId == tournamentId && r.UserId == userId);
            return registered;
        }

        // ===================================================================
        // 1. Kiem tra quyen vao chat (frontend goi de biet co cho vao khong)
        // GET /api/Chat/{tournamentId}/access
        // ===================================================================
        [HttpGet("{tournamentId}/access")]
        [Authorize]
        public async Task<IActionResult> CheckAccess(int tournamentId)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            bool canAccess = await CanAccessChat(tournamentId, uid.Value);
            return Ok(new { success = true, canAccess });
        }

        // ===================================================================
        // 2. Lay tin nhan cua giai (co the lay tu 1 id tro di - cho polling)
        // GET /api/Chat/{tournamentId}/messages?afterId=0
        // ===================================================================
        [HttpGet("{tournamentId}/messages")]
        [Authorize]
        public async Task<IActionResult> GetMessages(int tournamentId, [FromQuery] int afterId = 0)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            if (!await CanAccessChat(tournamentId, uid.Value))
                return StatusCode(403, new { success = false, message = "Ban chua dang ky giai nay nen khong the vao chat." });

            // Lay tin nhan (chi lay tu afterId tro di neu co - de polling nhe hon)
            var query = _context.ChatMessages
                .Include(m => m.User)
                .Where(m => m.TournamentId == tournamentId);

            if (afterId > 0)
                query = query.Where(m => m.Id > afterId);

            var messages = await query
                .OrderBy(m => m.Id)
                .Take(200) // gioi han 200 tin gan nhat moi lan
                .Select(m => new
                {
                    id = m.Id,
                    userId = m.UserId,
                    userName = m.User != null ? m.User.FullName : "Nguoi dung",
                    avatarUrl = m.User != null ? m.User.AvatarUrl : null,
                    content = m.Content,
                    createdAt = m.CreatedAt,
                    isMine = m.UserId == uid.Value
                })
                .ToListAsync();

            return Ok(new { success = true, data = messages });
        }

        // ===================================================================
        // 3. Gui tin nhan
        // POST /api/Chat/{tournamentId}/messages   body: { content }
        // ===================================================================
        public class SendMessageDto { public string? Content { get; set; } }

        [HttpPost("{tournamentId}/messages")]
        [Authorize]
        public async Task<IActionResult> SendMessage(int tournamentId, [FromBody] SendMessageDto dto)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            if (!await CanAccessChat(tournamentId, uid.Value))
                return StatusCode(403, new { success = false, message = "Ban chua dang ky giai nay nen khong the chat." });

            if (string.IsNullOrWhiteSpace(dto.Content))
                return BadRequest(new { success = false, message = "Noi dung tin nhan trong." });

            // Gioi han do dai tin nhan
            var content = dto.Content.Trim();
            if (content.Length > 1000) content = content.Substring(0, 1000);

            var msg = new ChatMessage
            {
                TournamentId = tournamentId,
                UserId = uid.Value,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(msg);
            await _context.SaveChangesAsync();

            // Tra ve tin vua gui (kem ten nguoi gui)
            var user = await _context.Users.FindAsync(uid.Value);
            return Ok(new
            {
                success = true,
                data = new
                {
                    id = msg.Id,
                    userId = msg.UserId,
                    userName = user?.FullName ?? "Nguoi dung",
                    avatarUrl = user?.AvatarUrl,
                    content = msg.Content,
                    createdAt = msg.CreatedAt,
                    isMine = true
                }
            });
        }

        // ===================================================================
        // 4. ADMIN/BTC: Xoa 1 tin nhan
        // DELETE /api/Chat/messages/{messageId}
        // ===================================================================
        [HttpDelete("messages/{messageId}")]
        [Authorize(Roles = "Admin,BTC")]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var msg = await _context.ChatMessages.FindAsync(messageId);
            if (msg == null) return NotFound(new { success = false, message = "Khong tim thay tin nhan." });

            _context.ChatMessages.Remove(msg);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Da xoa tin nhan." });
        }
    }
}