using Microsoft.AspNetCore.DataProtection;
using Tindy.Api.Commands;
using Tindy.Api.Extensions;
using Tindy.Infrastructure.Extensions;
using Tindy.Api.ExceptionHandlers;

if (args is ["migrate-db", ..])
{
    Environment.ExitCode = await MigrateDatabaseCommand.RunAsync(args);
    return;
}

if (args is ["seed-demo", ..])
{
    Environment.ExitCode = await DemoSeedCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// ==============================
// 1. REGISTER SERVICES
// ==============================

// Api services
builder.Services.AddApiServices(builder.Configuration);

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var ssmParameterPath = builder.Configuration["DataProtection:SsmParameterPath"];
if (!string.IsNullOrEmpty(ssmParameterPath))
{
    if (!string.IsNullOrEmpty(keyRingPath))
        throw new InvalidOperationException("Configure only one Data Protection key store.");

    builder.Services.AddDataProtection()
        .SetApplicationName("Tindy.Api")
        .PersistKeysToAWSSystemsManager(ssmParameterPath);
}
else if (!string.IsNullOrEmpty(keyRingPath))
{
    if (!Path.IsPathFullyQualified(keyRingPath) || !Directory.Exists(keyRingPath))
        throw new InvalidOperationException("DataProtection:KeyRingPath must be an existing absolute directory.");

    builder.Services.AddDataProtection()
        .SetApplicationName("Tindy.Api")
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

// Infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);

// Global Exception Handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ProblemDetails
builder.Services.AddProblemDetails();

// Keep existing DB, Authentication,
// Authorization, and other registrations here.



var app = builder.Build();

// ==============================
// 2. HTTP REQUEST PIPELINE
// ==============================

// Global Exception Handling
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    AllowStatusCode404Response = true
});

app.UseApiPipeline();

app.Run();

// Makes the API entry point accessible to WebApplicationFactory integration tests.
public partial class Program { }
