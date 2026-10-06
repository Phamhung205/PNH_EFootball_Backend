// ============================================================
// BACKEND - FILE THAY TOÀN BỘ
// Program.cs
// ============================================================

using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Appwebbongda.Data;
using Appwebbongda.Services;
using Appwebbongda.Models;

var builder =
    WebApplication.CreateBuilder(
        args
    );

// ============================================================
// 1. DATABASE
// ============================================================

var connectionString =
    builder.Configuration
        .GetConnectionString(
            "DefaultConnection"
        )
    ??
    throw new InvalidOperationException(
        "Thiếu ConnectionStrings__DefaultConnection."
    );

{
    var moiTruong =
        builder.Environment
            .EnvironmentName;

    var server =
        "?";

    try
    {
        server =
            new Microsoft.Data.SqlClient
                .SqlConnectionStringBuilder(
                    connectionString
                )
                .DataSource;
    }
    catch
    {
    }

    Console.WriteLine(
        "========================================"
    );

    Console.WriteLine(
        $"  MOI TRUONG : {moiTruong}"
    );

    Console.WriteLine(
        $"  DATABASE   : {server}"
    );

    Console.WriteLine(
        "========================================"
    );
}

builder.Services
    .AddDbContext<AppDbContext>(
        options =>
            options.UseSqlServer(
                connectionString,

                sql =>
                {
                    sql.EnableRetryOnFailure(
                        maxRetryCount:
                            3,

                        maxRetryDelay:
                            TimeSpan.FromSeconds(
                                3
                            ),

                        errorNumbersToAdd:
                            null
                    );

                    sql.CommandTimeout(
                        30
                    );
                }
            )
    );

// ============================================================
// 2. SERVICES
// ============================================================

builder.Services
    .AddScoped<
        IJwtService,
        JwtService
    >();

builder.Services
    .AddScoped<
        IOtpService,
        OtpService
    >();

builder.Services
    .AddScoped<
        IEmailSender,
        EmailSender
    >();

builder.Services
    .AddScoped<
        ISmsSender,
        SmsSender
    >();

builder.Services
    .AddScoped<
        ISubscriptionService,
        SubscriptionService
    >();

builder.Services
    .AddHttpClient();

// ============================================================
// 3. JWT
// ============================================================

builder.Services.AddScoped<AdminNotificationService>();

var jwtKey =
    builder.Configuration[
        "Jwt:Key"
    ]
    ??
    throw new InvalidOperationException(
        "Thiếu Jwt__Key."
    );

var jwtIssuer =
    builder.Configuration[
        "Jwt:Issuer"
    ]
    ??
    "PNHFootball";

var jwtAudience =
    builder.Configuration[
        "Jwt:Audience"
    ]
    ??
    "PNHFootballUsers";

builder.Services
    .AddAuthentication(
        JwtBearerDefaults
            .AuthenticationScheme
    )
    .AddJwtBearer(
        options =>
        {
            options
                .TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer =
                        true,

                    ValidateAudience =
                        true,

                    ValidateLifetime =
                        true,

                    ValidateIssuerSigningKey =
                        true,

                    ValidIssuer =
                        jwtIssuer,

                    ValidAudience =
                        jwtAudience,

                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding
                                .UTF8
                                .GetBytes(
                                    jwtKey
                                )
                        )
                };
        }
    );

builder.Services
    .AddAuthorization();

// ============================================================
// 4. RATE LIMIT
// ============================================================

builder.Services
    .AddRateLimiter(
        options =>
        {
            options
                .RejectionStatusCode =
                429;

            options
                .AddFixedWindowLimiter(
                    "auth",

                    opt =>
                    {
                        opt.PermitLimit =
                            5;

                        opt.Window =
                            TimeSpan
                                .FromMinutes(
                                    1
                                );

                        opt.QueueLimit =
                            0;
                    }
                );

            options
                .AddFixedWindowLimiter(
                    "chat",

                    opt =>
                    {
                        opt.PermitLimit =
                            20;

                        opt.Window =
                            TimeSpan
                                .FromMinutes(
                                    1
                                );

                        opt.QueueLimit =
                            0;
                    }
                );
        }
    );

// ============================================================
// 5. CONTROLLERS
// ============================================================

builder.Services
    .AddControllers()
    .AddJsonOptions(
        options =>
        {
            options
                .JsonSerializerOptions
                .ReferenceHandler =
                System.Text.Json
                    .Serialization
                    .ReferenceHandler
                    .IgnoreCycles;

            options
                .JsonSerializerOptions
                .MaxDepth =
                64;
        }
    );

builder.Services
    .AddEndpointsApiExplorer();

builder.Services
    .AddSwaggerGen();

// ============================================================
// 6. CORS
// ============================================================

const string CorsPolicy =
    "FrontendCors";

var allowedOrigins =
    builder.Configuration[
        "AllowedOrigins"
    ]
    ?.Split(
        ',',

        StringSplitOptions
            .RemoveEmptyEntries
        |
        StringSplitOptions
            .TrimEntries
    )
    ??
    new[]
    {
        "http://localhost:5173"
    };

builder.Services
    .AddCors(
        options =>
        {
            options.AddPolicy(
                CorsPolicy,

                policy =>
                    policy
                        .WithOrigins(
                            allowedOrigins
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod()
            );
        }
    );

var app =
    builder.Build();

// ============================================================
// 7. DATABASE STARTUP
// ============================================================

using (
    var scope =
        app.Services
            .CreateScope()
)
{
    try
    {
        var db =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

        db.Database
            .CanConnect();

        if (
            app.Environment
                .IsDevelopment()
        )
        {
            db.Database
                .EnsureCreated();
        }

        var syncColumns =
            new[]
            {
                @"IF COL_LENGTH('dbo.Tournaments','SystemCompetition') IS NULL
                ALTER TABLE dbo.Tournaments ADD SystemCompetition NVARCHAR(100) NULL;",

                // =================================================
                // USERS
                // =================================================

                @"IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.Users')
                    AND name = N'Plan'
                )
                ALTER TABLE dbo.Users
                ADD [Plan] NVARCHAR(20) NOT NULL DEFAULT N'free';",

                @"IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.Users')
                    AND name = N'PlanExpiry'
                )
                ALTER TABLE dbo.Users
                ADD PlanExpiry DATETIME2 NULL;",

                @"IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.Users')
                    AND name = N'TournamentsCreated'
                )
                ALTER TABLE dbo.Users
                ADD TournamentsCreated INT NOT NULL DEFAULT 0;",

                // =================================================
                // TOURNAMENTS
                // =================================================

                @"IF COL_LENGTH('dbo.Tournaments','Prize1') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD Prize1 INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','Prize2') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD Prize2 INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','Prize3') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD Prize3 INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Matches','BracketSlot') IS NULL
                ALTER TABLE dbo.Matches
                ADD BracketSlot INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','IsPaid') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD IsPaid BIT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','IsFree') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD IsFree BIT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','ActivationFee') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD ActivationFee INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','PaidAt') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD PaidAt DATETIME2 NULL;",

                @"IF COL_LENGTH('dbo.Tournaments','PaymentNote') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD PaymentNote NVARCHAR(50) NULL;",

                @"IF COL_LENGTH('dbo.Tournaments','PaymentClaimedAt') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD PaymentClaimedAt DATETIME2 NULL;",

                @"IF COL_LENGTH('dbo.Tournaments','PaymentRejectedAt') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD PaymentRejectedAt DATETIME2 NULL;",

                @"IF COL_LENGTH('dbo.Registrations','HasPaid') IS NULL
                ALTER TABLE dbo.Registrations
                ADD HasPaid BIT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Registrations','PaidAt') IS NULL
                ALTER TABLE dbo.Registrations
                ADD PaidAt DATETIME2 NULL;",

                @"IF COL_LENGTH('dbo.Teams','ShortName') IS NULL
                ALTER TABLE dbo.Teams
                ADD ShortName NVARCHAR(10) NULL;",

                @"IF COL_LENGTH('dbo.Tournaments','BestThirdPlaceCount') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD BestThirdPlaceCount INT NULL;",

                @"IF COL_LENGTH('dbo.Tournaments','ManualQualifiedIds') IS NULL
                ALTER TABLE dbo.Tournaments
                ADD ManualQualifiedIds NVARCHAR(MAX) NULL;",

                // =================================================
                // TEAM LIBRARY
                // =================================================

                @"IF OBJECT_ID('dbo.TeamLibraries', 'U') IS NULL
                CREATE TABLE dbo.TeamLibraries (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    UserId INT NOT NULL,
                    Name NVARCHAR(200) NOT NULL,
                    LogoUrl NVARCHAR(MAX) NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    LastUsedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );",

                @"IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_TeamLibraries_UserId'
                )
                AND OBJECT_ID('dbo.TeamLibraries', 'U') IS NOT NULL

                CREATE INDEX IX_TeamLibraries_UserId
                ON dbo.TeamLibraries(UserId);",

                // =================================================
                // CHAMPION HONORS
                // =================================================
                //
                // Bảng này lưu từng thành tích:
                //
                // Placement = 1 => Vô địch
                // Placement = 2 => Á quân
                // Placement = 3 => Hạng ba
                //
                // Source = Auto   => hệ thống tự ghi
                // Source = Manual => Admin nhập
                //
                // =================================================

                @"IF OBJECT_ID('dbo.ChampionHonors', 'U') IS NULL
                BEGIN

                    CREATE TABLE dbo.ChampionHonors (

                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,

                        TournamentId INT NULL,

                        UserId INT NULL,

                        CompetitionName NVARCHAR(255) NOT NULL,

                        Season NVARCHAR(100) NULL,

                        ChampionName NVARCHAR(255) NOT NULL,

                        TeamName NVARCHAR(255) NULL,

                        TeamLogoUrl NVARCHAR(MAX) NULL,

                        ChampionAvatarUrl NVARCHAR(MAX) NULL,

                        StartDate DATETIME2 NULL,

                        EndDate DATETIME2 NOT NULL,

                        Placement INT NOT NULL DEFAULT 1,

                        Source NVARCHAR(20) NOT NULL DEFAULT N'Manual',

                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

                        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

                        CONSTRAINT FK_ChampionHonors_Tournaments_TournamentId

                            FOREIGN KEY (TournamentId)

                            REFERENCES dbo.Tournaments(TournamentId)

                            ON DELETE SET NULL,

                        CONSTRAINT FK_ChampionHonors_Users_UserId

                            FOREIGN KEY (UserId)

                            REFERENCES dbo.Users(Id)

                            ON DELETE SET NULL
                    );

                END;",

                // Index tìm nhanh theo UserId.
                @"IF OBJECT_ID('dbo.ChampionHonors', 'U') IS NOT NULL
                AND NOT EXISTS (

                    SELECT 1

                    FROM sys.indexes

                    WHERE name = 'IX_ChampionHonors_UserId'

                    AND object_id = OBJECT_ID('dbo.ChampionHonors')

                )

                CREATE INDEX IX_ChampionHonors_UserId

                ON dbo.ChampionHonors(UserId);",

                // 1 giải chỉ có:
                // 1 Top 1
                // 1 Top 2
                // 1 Top 3
                //
                // Bản ghi Admin nhập tay TournamentId = NULL
                // nên Admin vẫn có thể nhập lịch sử mùa cũ bình thường.
                @"IF OBJECT_ID('dbo.ChampionHonors', 'U') IS NOT NULL
                AND NOT EXISTS (

                    SELECT 1

                    FROM sys.indexes

                    WHERE name = 'IX_ChampionHonors_TournamentId_Placement'

                    AND object_id = OBJECT_ID('dbo.ChampionHonors')

                )

                CREATE UNIQUE INDEX IX_ChampionHonors_TournamentId_Placement

                ON dbo.ChampionHonors(TournamentId, Placement)

                WHERE TournamentId IS NOT NULL;",

                // =================================================
                // MỞ KHÓA GIẢI CŨ
                // =================================================

                @"IF OBJECT_ID('dbo.AdminActivityEvents', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AdminActivityEvents (
                        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        UserId int NOT NULL,
                        UserName nvarchar(255) NOT NULL,
                        CreatedAt datetime2 NOT NULL
                    );
                END;
                IF OBJECT_ID('dbo.AdminActivityReads', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AdminActivityReads (
                        AdminUserId int NOT NULL PRIMARY KEY,
                        LastReadEventId bigint NOT NULL DEFAULT 0
                    );
                END;",
                @"UPDATE dbo.Tournaments

                SET

                    IsFree = 1,

                    IsPaid = 1

                WHERE

                    IsPaid = 0

                    AND IsFree = 0

                    AND ActivationFee = 0

                    AND PaidAt IS NULL;",
            };

        foreach (
            var sql in syncColumns
        )
        {
            try
            {
                db.Database
                    .ExecuteSqlRaw(
                        sql
                    );
            }
            catch (
                Exception ex
            )
            {
                Console.WriteLine(
                    $"[Startup] Bo qua dong bo cot: {ex.Message}"
                );
            }
        }

        Console.WriteLine(
            $"[Startup] Da dong bo cot ({app.Environment.EnvironmentName})."
        );

        // ========================================================
        // ADMIN SEED
        // ========================================================

        var adminEmail =
            builder.Configuration[
                "Admin:Email"
            ];

        var adminPassword =
            builder.Configuration[
                "Admin:Password"
            ];

        if (
            string.IsNullOrWhiteSpace(
                adminEmail
            )
            ||
            string.IsNullOrWhiteSpace(
                adminPassword
            )
        )
        {
            Console.WriteLine(
                "[Startup] Chua cau hinh Admin:Email / Admin:Password -> BO QUA tao admin. " +
                "Hay dat bien moi truong Admin__Email va Admin__Password de tao tai khoan admin an toan."
            );
        }
        else if (
            !db.Users.Any(
                u =>
                    u.Email ==
                    adminEmail
            )
        )
        {
            db.Users.Add(
                new User
                {
                    FullName =
                        "Administrator",

                    Email =
                        adminEmail,

                    PasswordHash =
                        BCrypt.Net.BCrypt
                            .HashPassword(
                                adminPassword
                            ),

                    Role =
                        "Admin",

                    CreatedAt =
                        DateTime.UtcNow
                }
            );

            db.SaveChanges();

            Console.WriteLine(
                $"[Startup] Da tao tai khoan admin: {adminEmail}"
            );
        }
    }
    catch (
        Exception ex
    )
    {
        Console.WriteLine(
            $"[Startup Warning] Khong the seed admin luc khoi dong: {ex.Message}"
        );
    }
}

// ============================================================
// 8. GLOBAL ERROR HANDLER
// ============================================================

app.UseExceptionHandler(
    errApp =>
    {
        errApp.Run(
            async context =>
            {
                var feature =
                    context
                        .Features
                        .Get<
                            IExceptionHandlerFeature
                        >();

                var ex =
                    feature?.Error;

                var logger =
                    context
                        .RequestServices
                        .GetRequiredService<
                            ILoggerFactory
                        >()
                        .CreateLogger(
                            "GlobalError"
                        );

                logger.LogError(
                    ex,

                    "Loi chua xu ly tai {Path}",

                    context
                        .Request
                        .Path
                );

                var isDev =
                    context
                        .RequestServices
                        .GetRequiredService<
                            IHostEnvironment
                        >()
                        .IsDevelopment();

                context
                    .Response
                    .StatusCode =
                    500;

                context
                    .Response
                    .ContentType =
                    "application/json";

                await context
                    .Response
                    .WriteAsJsonAsync(
                        new
                        {
                            success =
                                false,

                            message =
                                "Đã có lỗi xảy ra trên máy chủ. Vui lòng thử lại sau.",

                            detail =
                                isDev
                                    ? ex?.Message
                                    : null
                        }
                    );
            }
        );
    }
);

// ============================================================
// 9. SWAGGER
// ============================================================

if (
    app.Environment
        .IsDevelopment()
)
{
    app.UseSwagger();

    app.UseSwaggerUI();
}

// ============================================================
// 10. PIPELINE
// ============================================================

app.UseCors(
    CorsPolicy
);

app.UseAuthentication();

app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.MapGet(
    "/",

    () =>
        Results.Ok(
            new
            {
                status =
                    "PNH Football API is running"
            }
        )
);

// ============================================================
// 11. HEALTH
// ============================================================

app.MapGet(
    "/health",

    async (
        AppDbContext db
    ) =>
    {
        try
        {
            await db
                .Database
                .ExecuteSqlRawAsync(
                    "SELECT 1"
                );

            return Results.Ok(
                new
                {
                    status =
                        "healthy",

                    db =
                        "awake"
                }
            );
        }
        catch (
            Exception ex
        )
        {
            return Results.Ok(
                new
                {
                    status =
                        "degraded",

                    db =
                        "sleeping",

                    note =
                        ex.Message
                }
            );
        }
    }
);

app.Run();
