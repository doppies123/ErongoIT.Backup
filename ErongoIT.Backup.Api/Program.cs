using ErongoIT.Backup.Application.BackupJobs;
using ErongoIT.Backup.Application.BackupPlans;
using ErongoIT.Backup.Application.Customers;
using ErongoIT.Backup.Application.Devices;
using ErongoIT.Backup.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("BackupDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'BackupDatabase' was not configured.");

var storageRoot =
    builder.Configuration["BackupStorage:RootPath"]
    ?? throw new InvalidOperationException(
        "Backup storage root path was not configured.");

builder.Services.AddBackupDatabase(connectionString);
builder.Services.AddBackupStorage(storageRoot);

builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IBackupPlanService, BackupPlanService>();
builder.Services.AddScoped<IBackupJobService, BackupJobService>();
builder.Services.AddScoped<IBackupSnapshotService, BackupSnapshotService>();

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
