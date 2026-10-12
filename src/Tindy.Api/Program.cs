using Tindy.Api.Extensions;
using Tindy.Infrastructure.Extensions;
using Tindy.Api.ExceptionHandlers;

var builder = WebApplication.CreateBuilder(args);

// ==============================
// 1. REGISTER SERVICES
// ==============================

// Api services
builder.Services.AddApiServices(builder.Configuration);

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