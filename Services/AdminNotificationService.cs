using Appwebbongda.Data;
using Appwebbongda.Models;
using Microsoft.EntityFrameworkCore;

namespace Appwebbongda.Services;

public class AdminNotificationService(AppDbContext context, ILogger<AdminNotificationService> logger)
{
    public async Task RecordLoginAsync(User user)
    {
        if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            context.AdminActivityEvents.Add(new AdminActivityEvent { UserId = user.Id, UserName = user.FullName, CreatedAt = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // An unavailable notification store must not block successful authentication.
            foreach (var entry in context.ChangeTracker.Entries<AdminActivityEvent>().Where(entry => entry.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            logger.LogError(ex, "Could not persist an admin login notification for user {UserId}", user.Id);
        }
    }
}
