using Tindy.Api.Extensions;
using Tindy.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);


// Api services
builder.Services.AddApiServices();

// Infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseApiPipeline();

app.Run();