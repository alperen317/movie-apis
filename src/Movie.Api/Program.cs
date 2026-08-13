using Microsoft.EntityFrameworkCore;
using Movie.Api;
using Movie.Api.Endpoints;
using Movie.Api.OpenApi;
using Movie.Application;
using Movie.Infrastructure;
using Movie.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddAuthorization();
builder.Services.AddApiRateLimiting(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Movie API"));

    // So a fresh checkout works from `docker compose up` alone. Deliberately
    // development-only: applying schema changes automatically on start is not
    // something a deployment should do behind your back.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<MovieDbContext>().Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapMeEndpoints();

app.Run();

/// <summary>
/// Exposed so the integration tests can drive the real application through
/// <c>WebApplicationFactory</c> instead of a stand-in host.
/// </summary>
public partial class Program;
