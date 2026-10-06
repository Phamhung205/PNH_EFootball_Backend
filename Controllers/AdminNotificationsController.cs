using System.Security.Claims;
using Appwebbongda.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Appwebbongda.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminNotificationsController(AppDbContext context) : ControllerBase
{
    private async Task<int?> CurrentAdminAsync()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(subject, out var id)) return null;
        // Recheck the current database role, including after an admin has been demoted.
        return await context.Users.AnyAsync(user => user.Id == id && user.Role == "Admin") ? id : null;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var adminId = await CurrentAdminAsync();
        if (adminId == null) return Forbid();
        var lastRead = await context.AdminActivityReads.AsNoTracking()
            .Where(read => read.AdminUserId == adminId).Select(read => read.LastReadEventId).FirstOrDefaultAsync();
        var unreadCount = await context.AdminActivityEvents.CountAsync(item => item.Id > lastRead);
        var items = await context.AdminActivityEvents.AsNoTracking().OrderByDescending(item => item.Id).Take(50)
            .Select(item => new { item.Id, item.UserName, item.CreatedAt, isRead = item.Id <= lastRead }).ToListAsync();
        return Ok(new { data = new { unreadCount, items = items.Select(item => new { item.Id, item.UserName, createdAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc), item.isRead }) } });
    }

    public record MarkReadRequest(long ThroughId);

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadRequest request)
    {
        var adminId = await CurrentAdminAsync();
        if (adminId == null) return Forbid();
        if (request.ThroughId <= 0) return BadRequest(new { message = "Mã thông báo không hợp lệ." });
        var throughId = await context.AdminActivityEvents.Where(item => item.Id <= request.ThroughId)
            .Select(item => (long?)item.Id).MaxAsync() ?? 0;
        // Serializable upsert with a monotonic watermark, safe across multiple admin tabs.
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.AdminActivityReads WITH (UPDLOCK, HOLDLOCK) WHERE AdminUserId = {adminId.Value})
                UPDATE dbo.AdminActivityReads SET LastReadEventId = CASE WHEN LastReadEventId < {throughId} THEN {throughId} ELSE LastReadEventId END WHERE AdminUserId = {adminId.Value};
            ELSE
                INSERT INTO dbo.AdminActivityReads (AdminUserId, LastReadEventId) VALUES ({adminId.Value}, {throughId});
            COMMIT TRANSACTION;");
        return Ok(new { success = true });
    }
}
