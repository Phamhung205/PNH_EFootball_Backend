// ============================================================
// BACKEND - FILE THAY TOÀN BỘ
// Data/AppDbContext.cs
// ============================================================

using Microsoft.EntityFrameworkCore;
using Appwebbongda.Models;

using Match =
    Appwebbongda.Models.Match;

using Group =
    Appwebbongda.Models.Group;

namespace Appwebbongda.Data
{
    public class AppDbContext
        : DbContext
    {
        public AppDbContext(
            DbContextOptions<AppDbContext> options
        )
            : base(options)
        {
        }

        public DbSet<Tournament>
            Tournaments
        {
            get;
            set;
        }

        public DbSet<Group>
            Groups
        {
            get;
            set;
        }

        public DbSet<Team>
            Teams
        {
            get;
            set;
        }

        public DbSet<Match>
            Matches
        {
            get;
            set;
        }

        public DbSet<User>
            Users
        {
            get;
            set;
        }

        public DbSet<Registration>
            Registrations
        {
            get;
            set;
        }

        public DbSet<ChatMessage>
            ChatMessages
        {
            get;
            set;
        }

        public DbSet<TeamLibrary>
            TeamLibraries
        {
            get;
            set;
        }

        // Bảng mới.
        public DbSet<ChampionHonor>
            ChampionHonors
        {
            get;
            set;
        }

        public DbSet<AdminActivityEvent> AdminActivityEvents { get; set; }
        public DbSet<AdminActivityRead> AdminActivityReads { get; set; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(
                modelBuilder
            );

            // ================================================
            // USER
            // ================================================

            modelBuilder
                .Entity<User>()
                .HasIndex(
                    u =>
                        u.Email
                )
                .IsUnique();

            // ================================================
            // MATCH
            // ================================================

            modelBuilder
                .Entity<Match>()
                .HasOne(
                    m =>
                        m.HomeTeam
                )
                .WithMany()
                .HasForeignKey(
                    m =>
                        m.HomeTeamId
                )
                .OnDelete(
                    DeleteBehavior.Restrict
                );

            modelBuilder
                .Entity<Match>()
                .HasOne(
                    m =>
                        m.AwayTeam
                )
                .WithMany()
                .HasForeignKey(
                    m =>
                        m.AwayTeamId
                )
                .OnDelete(
                    DeleteBehavior.Restrict
                );

            modelBuilder
                .Entity<Match>()
                .HasOne(
                    m =>
                        m.Tournament
                )
                .WithMany()
                .HasForeignKey(
                    m =>
                        m.TournamentId
                )
                .OnDelete(
                    DeleteBehavior.Cascade
                );

            // ================================================
            // REGISTRATION
            // ================================================

            modelBuilder
                .Entity<Registration>()
                .HasOne(
                    r =>
                        r.Tournament
                )
                .WithMany()
                .HasForeignKey(
                    r =>
                        r.TournamentId
                )
                .OnDelete(
                    DeleteBehavior.Cascade
                );

            modelBuilder
                .Entity<Registration>()
                .HasOne(
                    r =>
                        r.User
                )
                .WithMany()
                .HasForeignKey(
                    r =>
                        r.UserId
                )
                .OnDelete(
                    DeleteBehavior.Restrict
                );

            modelBuilder
                .Entity<Registration>()
                .HasOne(
                    r =>
                        r.Team
                )
                .WithMany()
                .HasForeignKey(
                    r =>
                        r.TeamId
                )
                .OnDelete(
                    DeleteBehavior.Restrict
                );

            // ================================================
            // CHAMPION HONOR
            // ================================================

            // Nếu xóa giải thì KHÔNG xóa lịch sử vinh danh.
            // TournamentId sẽ trở thành null.
            modelBuilder
                .Entity<ChampionHonor>()
                .HasOne(
                    h =>
                        h.Tournament
                )
                .WithMany()
                .HasForeignKey(
                    h =>
                        h.TournamentId
                )
                .OnDelete(
                    DeleteBehavior.SetNull
                );

            // Nếu user bị xóa thì vẫn giữ lịch sử.
            modelBuilder
                .Entity<ChampionHonor>()
                .HasOne(
                    h =>
                        h.User
                )
                .WithMany()
                .HasForeignKey(
                    h =>
                        h.UserId
                )
                .OnDelete(
                    DeleteBehavior.SetNull
                );

            // Mỗi giải tự động chỉ có:
            // 1 Top 1
            // 1 Top 2
            // 1 Top 3
            //
            // Bản ghi Admin nhập thủ công có TournamentId = null,
            // nên không bị giới hạn này.
            modelBuilder
                .Entity<ChampionHonor>()
                .HasIndex(
                    h =>
                        new
                        {
                            h.TournamentId,
                            h.Placement
                        }
                )
                .IsUnique()
                .HasFilter(
                    "[TournamentId] IS NOT NULL"
                );

            modelBuilder
                .Entity<ChampionHonor>()
                .HasIndex(
                    h =>
                        h.UserId
                );
        }
    }
}
