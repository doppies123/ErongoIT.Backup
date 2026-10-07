using System.Text;
using ErongoIT.Backup.Application.BackupJobs;
using ErongoIT.Backup.Application.BackupPlans;
using ErongoIT.Backup.Application.Customers;
using ErongoIT.Backup.Application.Devices;
using ErongoIT.Backup.Application.Users;
using ErongoIT.Backup.Domain.Entities;
using ErongoIT.Backup.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("BackupDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'BackupDatabase' was not configured.");

var storageRoot =
    builder.Configuration["BackupStorage:RootPath"]
    ?? throw new InvalidOperationException(
        "Backup storage root path was not configured.");

var jwtKey =
    builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT signing key was not configured.");

var jwtIssuer =
    builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT issuer was not configured.");

var jwtAudience =
    builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT audience was not configured.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT signing key must be at least 32 bytes.");
}

builder.Services.AddBackupDatabase(connectionString);
builder.Services.AddBackupStorage(storageRoot);

builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IBackupPlanService, BackupPlanService>();
builder.Services.AddScoped<IBackupJobService, BackupJobService>();
builder.Services.AddScoped<IBackupSnapshotService, BackupSnapshotService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddSingleton<PasswordHasher<User>>();

builder.Services.AddHostedService<ErongoIT.Backup.Api.Maintenance.RetentionBackgroundService>();

builder.Services.AddAuthentication(
    JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateLifetime = true,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),

                ClockSkew = TimeSpan.FromMinutes(1)
            };
    });

builder.Services.AddAuthorization();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
