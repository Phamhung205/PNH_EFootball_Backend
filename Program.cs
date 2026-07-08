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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        // DB MonsterASP (goi mien phi) hay ngu/rot ket noi -> thu lai khi loi.
        // Giam maxRetryDelay 10s -> 4s: khi ket noi cu bi rot, phuc hoi NHANH hon nhieu
        // (truoc cho toi 10s/lan lam request cham ~20s; gio toi da ~4s/lan).
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(4),
            errorNumbersToAdd: null);
        // Cho moi cau lenh toi da 60 giay (thay vi 30s mac dinh) vi DB cham.
        sql.CommandTimeout(60);
    }));

// 2. Services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<ISmsSender, SmsSender>();

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