using Tindy.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiServices();

var app = builder.Build();

app.UseApiPipeline();

app.Run();