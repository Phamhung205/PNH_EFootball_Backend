using Microsoft.EntityFrameworkCore;
using Appwebbongda.Models;

namespace Appwebbongda.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // 1. Khai báo các bảng sẽ được tạo trong SQL Server
        public DbSet<Tournament> Tournaments { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<Match> Matches { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Registration> Registrations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 2. Cấu hình bảng User (Đảm bảo Email không bị trùng lặp)
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // 3. Cấu hình bảng Match: Tránh lỗi Multiple Cascade Paths của SQL Server
            modelBuilder.Entity<Match>()
                .HasOne(m => m.HomeTeam)
                .WithMany()
                .HasForeignKey(m => m.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Match>()
                .HasOne(m => m.AwayTeam)
                .WithMany()
                .HasForeignKey(m => m.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Tournament)
                .WithMany()
                .HasForeignKey(m => m.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            // 4. Cau hinh bang Registration (dang ky tham du giai).
            // Xoa giai -> xoa luon cac dang ky cua giai do.
            modelBuilder.Entity<Registration>()
                .HasOne(r => r.Tournament)
                .WithMany()
                .HasForeignKey(r => r.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Lien ket User: KHONG cascade (tranh multiple cascade paths cua SQL Server)
            modelBuilder.Entity<Registration>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Lien ket Team: KHONG cascade, cho phep null (chua chia doi)
            modelBuilder.Entity<Registration>()
                .HasOne(r => r.Team)
                .WithMany()
                .HasForeignKey(r => r.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}