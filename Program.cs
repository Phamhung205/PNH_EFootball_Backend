using System.Text;
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
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

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

        const string adminEmail = "aadmin588@gmail.com";
        if (!db.Users.Any(u => u.Email == adminEmail))
        {
            db.Users.Add(new User
            {
                FullName = "Administrator",
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@12345"),
                Role = "Admin",
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
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
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors(CorsPolicy);

app.UseAuthentication(); // PHẢI trước UseAuthorization
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Ok(new { status = "PNH Football API is running" }));

app.Run();