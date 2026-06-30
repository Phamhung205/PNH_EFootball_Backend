using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Appwebbongda.Data;
using Appwebbongda.Models;
using Appwebbongda.DTOs;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;

        // Email admin goc - khong cho phep ha quyen tai khoan nay (tranh mat admin)
        private const string AdminEmail = "aadmin588@gmail.com";

        public UsersController(AppDbContext context)
        {
            _context = context;
        }

        // Lay id user tu token (giong AuthController)
        private int? GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : (int?)null;
        }

        // Kiem tra user hien tai co phai admin khong (dua vao Role trong token)
        private bool IsAdmin() =>
            string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);

        // Chuyen 1 User sang DTO an toan (KHONG tra PasswordHash ra ngoai)
        private static UserListItemDto ToDto(User u) => new UserListItemDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            Role = u.Role,
            AvatarUrl = u.AvatarUrl,
            Provider = u.Provider,
            CreatedAt = u.CreatedAt
        };

        // ===================================================================
        // 1. ADMIN: Lay danh sach tat ca nguoi dung
        // GET /api/Users?search=abc
        // ===================================================================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetUsers([FromQuery] string? search)
        {
            if (!IsAdmin())
                return StatusCode(403, new { success = false, message = "Chi admin moi xem duoc danh sach nguoi dung." });

            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u =>
                    u.FullName.ToLower().Contains(s) ||
                    u.Email.ToLower().Contains(s));
            }

            var list = await query
                .OrderByDescending(u => u.Role == "Admin") // admin len dau
                .ThenBy(u => u.FullName)
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = list.Select(ToDto).ToList()
            });
        }

        // ===================================================================
        // 1b. ADMIN: Tim 1 nguoi dung theo Gmail (de phan quyen)
        // GET /api/Users/find?email=abc@gmail.com
        // ===================================================================
        [HttpGet("find")]
        [Authorize]
        public async Task<IActionResult> FindByEmail([FromQuery] string email)
        {
            if (!IsAdmin())
                return StatusCode(403, new { success = false, message = "Chi admin moi duoc tim nguoi dung." });

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { success = false, message = "Vui long nhap email." });

            var key = email.Trim().ToLower();
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == key);

            // Khong tim thay -> bao loi ro de frontend hien "Chua co tai khoan nay"
            if (user == null)
                return NotFound(new { success = false, message = "Chua co tai khoan nay." });

            return Ok(new { success = true, data = ToDto(user) });
        }

        // ===================================================================
        // 2. ADMIN: Doi quyen 1 nguoi dung (Admin / BTC / User)
        // PUT /api/Users/{id}/role
        // ===================================================================
        [HttpPut("{id}/role")]
        [Authorize]
        public async Task<IActionResult> ChangeRole(int id, [FromBody] ChangeRoleDto dto)
        {
            var myId = GetCurrentUserId();
            if (myId == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            if (!IsAdmin())
                return StatusCode(403, new { success = false, message = "Chi admin moi duoc doi quyen." });

            // Chi chap nhan 3 gia tri: "Admin", "BTC" hoac "User"
            var newRole = (dto.Role ?? "").Trim();
            var allowed = new[] { "Admin", "BTC", "User" };
            var matched = allowed.FirstOrDefault(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase));
            if (matched == null)
                return BadRequest(new { success = false, message = "Quyen khong hop le. Chi nhan 'Admin', 'BTC' hoac 'User'." });

            // Chuan hoa dung chu (Admin / BTC / User)
            newRole = matched;

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            // Khong cho ha quyen tai khoan admin goc
            if (string.Equals(user.Email, AdminEmail, StringComparison.OrdinalIgnoreCase) && newRole != "Admin")
                return BadRequest(new { success = false, message = "Khong the ha quyen tai khoan admin goc." });

            // Khong cho tu ha quyen chinh minh (tranh tu khoa minh ra ngoai)
            if (user.Id == myId.Value && newRole != "Admin")
                return BadRequest(new { success = false, message = "Ban khong the tu ha quyen chinh minh." });

            user.Role = newRole;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Da cap nhat quyen thanh cong.",
                data = ToDto(user)
            });
        }

        // ===================================================================
        // 3. ADMIN: Xoa 1 nguoi dung
        // DELETE /api/Users/{id}
        // ===================================================================
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var myId = GetCurrentUserId();
            if (myId == null) return Unauthorized(new { success = false, message = "Token khong hop le." });

            if (!IsAdmin())
                return StatusCode(403, new { success = false, message = "Chi admin moi duoc xoa nguoi dung." });

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { success = false, message = "Khong tim thay nguoi dung." });

            // Khong cho xoa admin goc
            if (string.Equals(user.Email, AdminEmail, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Khong the xoa tai khoan admin goc." });

            // Khong cho tu xoa chinh minh
            if (user.Id == myId.Value)
                return BadRequest(new { success = false, message = "Ban khong the tu xoa chinh minh." });

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Da xoa nguoi dung." });
        }
    }
}