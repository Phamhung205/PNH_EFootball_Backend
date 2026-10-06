using System.ComponentModel.DataAnnotations;

namespace Appwebbongda.Models;

public class AdminActivityEvent
{
    public long Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(255)] public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AdminActivityRead
{
    [Key] public int AdminUserId { get; set; }
    public long LastReadEventId { get; set; }
}
