using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Appwebbongda.Data;
using Appwebbongda.Services;
using Appwebbongda.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Thiếu ConnectionStrings__DefaultConnection.");

// IN RO moi truong + database dang ket noi (giup phan biet local vs that).
// Neu chay local ma van thay "db54322.public.databaseasp.net" nghia la KHONG
// chay o che do Development -> kiem tra profile dang chay trong Visual Studio.
{
    var moiTruong = builder.Environment.EnvironmentName;
    var server = "?";
    try { server = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).DataSource; } catch { }
    Console.WriteLine("========================================");
    Console.WriteLine($"  MOI TRUONG : {moiTruong}");
    Console.WriteLine($"  DATABASE   : {server}");
    Console.WriteLine("========================================");
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        // DB MonsterASP (goi mien phi) hay ngu/rot ket noi -> thu lai khi loi.
        // maxRetryCount 3, delay 3s: du de vuot qua luc DB "tinh giac",
        // khong keo dai qua lau khi that su co van de.
        sql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(3),
            errorNumbersToAdd: null);
        // 30 giay/cau lenh. Neu treo thi bao loi trong 30s thay vi 60s -> de sua hon.
        // Cau lenh binh thuong chi mat vai chuc mili-giay, 30s la qua du.
        sql.CommandTimeout(30);
    }));

// 2. Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<ISmsSender, SmsSender>();

// Goi dang ky: toan bo luat goi/het han/nang quyen BTC nam trong service nay
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// HttpClient de goi API AI (Groq) tu AssistantController
builder.Services.AddHttpClient();

// 3. JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Thiếu Jwt__Key.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PNHFootball";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PNHFootballUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// 3b. Rate limiting (chan thu nhieu lan: login, OTP, quen mat khau...)
// Chinh sach "auth": moi IP toi da 5 request / 1 phut cho cac endpoint dang nhap/OTP.
// Neu vuot -> tra ve ma 429 (Too Many Requests).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    // Chinh sach "chat": moi IP toi da 20 tin nhan/phut (tranh xai chua het quota AI free)
    options.AddFixedWindowLimiter("chat", opt =>
    {
        opt.PermitLimit = 20;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// 4. Controllers + JSON
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.MaxDepth = 64;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 5. CORS
const string CorsPolicy = "FrontendCors";
var allowedOrigins = builder.Configuration["AllowedOrigins"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// 6. Seed admin (KHONG dung Migrate() de tranh crash PendingModelChangesWarning).
// Database da duoc cap nhat cot bang SQL truc tiep nen KHONG can EF tu migrate.
// Boc try-catch de neu DB tam thoi ngu/loi thi app VAN KHOI DONG (khong crash).
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Chi dam bao ket noi duoc DB (khong goi Migrate de tranh warning lam crash)
        db.Database.CanConnect();

        // ===== TAO DATABASE MOI (CHI LOCAL) =====
        // EnsureCreated tao toan bo bang tu model neu database CHUA co.
        // Chi lam o local: Production da co database that, khong dung cach nay.
        if (app.Environment.IsDevelopment())
        {
            db.Database.EnsureCreated();
        }

        // ===== DONG BO COT (CHAY CA LOCAL LAN PRODUCTION) =====
        // Truoc day khoi nay CHI chay o local -> Production thieu cot moi (vd
        // PaymentClaimedAt) -> loi "Invalid column name" (SQL 207) khi chia bang.
        // Gio cho chay ca tren Production. AN TOAN vi moi cau deu la
        // "IF COL_LENGTH ... IS NULL" -> chi them neu chua co, khong pha du lieu,
        // chay lai bao nhieu lan cung khong sao.
        {
            var syncColumns = new[]
            {
                @"IF NOT EXISTS (SELECT 1 FROM sys.columns
                      WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'Plan')
                  ALTER TABLE dbo.Users ADD [Plan] NVARCHAR(20) NOT NULL DEFAULT N'free';",

                @"IF NOT EXISTS (SELECT 1 FROM sys.columns
                      WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'PlanExpiry')
                  ALTER TABLE dbo.Users ADD PlanExpiry DATETIME2 NULL;",

                @"IF NOT EXISTS (SELECT 1 FROM sys.columns
                      WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'TournamentsCreated')
                  ALTER TABLE dbo.Users ADD TournamentsCreated INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Tournaments','Prize1') IS NULL
                  ALTER TABLE dbo.Tournaments ADD Prize1 INT NOT NULL DEFAULT 0;",
                @"IF COL_LENGTH('dbo.Tournaments','Prize2') IS NULL
                  ALTER TABLE dbo.Tournaments ADD Prize2 INT NOT NULL DEFAULT 0;",
                @"IF COL_LENGTH('dbo.Tournaments','Prize3') IS NULL
                  ALTER TABLE dbo.Tournaments ADD Prize3 INT NOT NULL DEFAULT 0;",

                @"IF COL_LENGTH('dbo.Matches','BracketSlot') IS NULL
                  ALTER TABLE dbo.Matches ADD BracketSlot INT NOT NULL DEFAULT 0;",

                // ── PHI KICH HOAT GIAI ──
                @"IF COL_LENGTH('dbo.Tournaments','IsPaid') IS NULL
                  ALTER TABLE dbo.Tournaments ADD IsPaid BIT NOT NULL DEFAULT 0;",
                @"IF COL_LENGTH('dbo.Tournaments','IsFree') IS NULL
                  ALTER TABLE dbo.Tournaments ADD IsFree BIT NOT NULL DEFAULT 0;",
                @"IF COL_LENGTH('dbo.Tournaments','ActivationFee') IS NULL
                  ALTER TABLE dbo.Tournaments ADD ActivationFee INT NOT NULL DEFAULT 0;",
                @"IF COL_LENGTH('dbo.Tournaments','PaidAt') IS NULL
                  ALTER TABLE dbo.Tournaments ADD PaidAt DATETIME2 NULL;",
                @"IF COL_LENGTH('dbo.Tournaments','PaymentNote') IS NULL
                  ALTER TABLE dbo.Tournaments ADD PaymentNote NVARCHAR(50) NULL;",
                @"IF COL_LENGTH('dbo.Tournaments','PaymentClaimedAt') IS NULL
                  ALTER TABLE dbo.Tournaments ADD PaymentClaimedAt DATETIME2 NULL;",

                @"IF COL_LENGTH('dbo.Tournaments','PaymentRejectedAt') IS NULL
                  ALTER TABLE dbo.Tournaments ADD PaymentRejectedAt DATETIME2 NULL;",

                // ── CHON DOI VAO VONG TRONG ──
                @"IF COL_LENGTH('dbo.Tournaments','BestThirdPlaceCount') IS NULL
                  ALTER TABLE dbo.Tournaments ADD BestThirdPlaceCount INT NULL;",
                @"IF COL_LENGTH('dbo.Tournaments','ManualQualifiedIds') IS NULL
                  ALTER TABLE dbo.Tournaments ADD ManualQualifiedIds NVARCHAR(MAX) NULL;",

                // ── BANG KHO DOI CA NHAN ──
                // EnsureCreated khong tao bang moi khi database da ton tai, nen phai
                // tu tao. Moi user co kho doi rieng (UserId), khong lan sang nhau.
                @"IF OBJECT_ID('dbo.TeamLibraries', 'U') IS NULL
                  CREATE TABLE dbo.TeamLibraries (
                      Id          INT IDENTITY(1,1) PRIMARY KEY,
                      UserId      INT NOT NULL,
                      Name        NVARCHAR(200) NOT NULL,
                      LogoUrl     NVARCHAR(MAX) NULL,
                      CreatedAt   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                      LastUsedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                  );",
                // Chi so giup tim nhanh theo user
                @"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TeamLibraries_UserId')
                  AND OBJECT_ID('dbo.TeamLibraries', 'U') IS NOT NULL
                  CREATE INDEX IX_TeamLibraries_UserId ON dbo.TeamLibraries(UserId);",

                // MO KHOA GIAI CU — cac giai tao truoc khi co tinh nang phi deu co
                // IsPaid = 0. Neu khong xu ly, nguoi dung dang dung binh thuong bong
                // dung bi khoa het.
                // Dieu kien ActivationFee = 0 AND PaidAt IS NULL nhan dien dung "giai cu":
                // giai MOI chua tra tien luon co ActivationFee > 0 (30000/35000) nen
                // KHONG bi cau lenh nay mo khoa nham.
                @"UPDATE dbo.Tournaments SET IsFree = 1, IsPaid = 1
                  WHERE IsPaid = 0 AND IsFree = 0 AND ActivationFee = 0 AND PaidAt IS NULL;",
            };

            foreach (var sql in syncColumns)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { Console.WriteLine($"[Startup] Bo qua dong bo cot: {ex.Message}"); }
            }

            Console.WriteLine($"[Startup] Da dong bo cot ({app.Environment.EnvironmentName}).");
        }

        // Doc thong tin admin tu BIEN MOI TRUONG (KHONG hard-code mat khau de tranh lo).
        // BAO MAT: chi tao admin khi da cau hinh Admin:Password (bien moi truong Admin__Password).
        // Neu chua cau hinh -> BO QUA seed (khong tao admin mat khau mac dinh de tranh lo hong).
        var adminEmail = builder.Configuration["Admin:Email"];
        var adminPassword = builder.Configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            Console.WriteLine("[Startup] Chua cau hinh Admin:Email / Admin:Password -> BO QUA tao admin. " +
                              "Hay dat bien moi truong Admin__Email va Admin__Password de tao tai khoan admin an toan.");
        }
        else if (!db.Users.Any(u => u.Email == adminEmail))
        {
            db.Users.Add(new User
            {
                FullName = "Administrator",
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                Role = "Admin",
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            Console.WriteLine($"[Startup] Da tao tai khoan admin: {adminEmail}");
        }
    }
    catch (Exception ex)
    {
        // Neu DB loi luc khoi dong -> chi ghi log, KHONG lam sap app.
        // App van chay, cac request sau se thu ket noi lai.
        Console.WriteLine($"[Startup Warning] Khong the seed admin luc khoi dong: {ex.Message}");
    }
}

// 7. Pipeline

// 7a. Bat loi TOAN CUC: moi loi chua xu ly se bi bat o day, ghi log,
// va tra ve JSON gon gang (KHONG lo stack trace ra ngoai o production).
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;

        // Ghi log loi kem duong dan de tien tra sau nay
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalError");
        logger.LogError(ex, "Loi chua xu ly tai {Path}", context.Request.Path);

        var isDev = context.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Đã có lỗi xảy ra trên máy chủ. Vui lòng thử lại sau.",
            // Chi lo chi tiet loi khi chay Local (Development), production thi an di
            detail = isDev ? ex?.Message : null
        });
    });
});

// BAO MAT: chi bat Swagger khi chay Local (Development). Tren production tat di
// de khong lo toan bo danh sach API cho nguoi ngoai.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors(CorsPolicy);

app.UseAuthentication(); // PHẢI trước UseAuthorization
app.UseAuthorization();

app.UseRateLimiter(); // bat gioi han so lan thu (phai truoc MapControllers)

app.MapControllers();
app.MapGet("/", () => Results.Ok(new { status = "PNH Football API is running" }));

// /health: chay "SELECT 1" de DANH THUC DB va giu DB + backend KHONG bi ngu.
// QUAN TRONG: UptimeRobot phai ping vao /health (KHONG phai /) thi moi danh thuc DB,
// vi endpoint / khong dung DB nen ping vao / chi giu backend, DB van ngu -> van cham.
app.MapGet("/health", async (AppDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "healthy", db = "awake" });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { status = "degraded", db = "sleeping", note = ex.Message });
    }
});

app.Run();