using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

using Movie.Api;
using Movie.Api.Endpoints;
using Movie.Api.OpenApi;
using Movie.Application;
using Movie.Infrastructure;
using Movie.Infrastructure.Persistence;
using Movie.Infrastructure.Realtime;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// A no-op when Sentry:Dsn is unset, the same behavior as the mobile client's
// EXPO_PUBLIC_SENTRY_DSN -- see appsettings.json.
builder.WebHost.UseSentry(options => options.Dsn = builder.Configuration["Sentry:Dsn"]);

builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
// Enums travel as lower-case names, the same values the database stores and
// the mobile client's string literals already assume ("beam", not 0 or "Beam").
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddAuthorization();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<MovieDbContext>();

if (builder.Environment.IsDevelopment())
{
    // Only a browser enforces this at all — the mobile client (React Native)
    // never does, so this has no production equivalent. It exists purely so
    // the Expo web preview, running on its own Metro origin, can reach this
    // API during local development.
    builder.Services.AddCors(options => options.AddPolicy(
        "dev",
        policy => policy
            .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

var app = builder.Build();

// A one-shot alternative to running the app: apply pending migrations and
// exit. Development applies migrations on every start (below); production
// has no such block, so a deploy runs this explicitly first — see the
// production compose file.
if (args.Contains("--migrate"))
{
    await using var migrateScope = app.Services.CreateAsyncScope();
    await migrateScope.ServiceProvider.GetRequiredService<MovieDbContext>().Database.MigrateAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Movie API"));

    // So a fresh checkout works from `docker compose up` alone. Deliberately
    // development-only: applying schema changes automatically on start is not
    // something a deployment should do behind your back.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<MovieDbContext>().Database.MigrateAsync();
}

// The proxy in front of this API terminates TLS and forwards plain HTTP over
// the Docker network, so both the real client IP (RateLimiting.cs's
// per-caller partitions) and the original https scheme (UseHttpsRedirection
// below) only become visible once these headers are trusted. Scoped to
// Docker's private bridge range, not every network: a caller reaching this
// container directly can never have a raw connection originating from
// 172.16.0.0/12, so it cannot forge these headers to spoof its IP or dodge
// the rate limiter.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
var trustedProxyNetwork = new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12);
forwardedHeaders.KnownIPNetworks.Add(trustedProxyNetwork);
app.UseForwardedHeaders(forwardedHeaders);

// This assumes the proxy reaches the container through Docker's own bridge
// NAT and therefore appears in this range -- true for the topology this was
// configured against, but not verified against the actual host. Logged so a
// deploy can be checked against reality rather than trusted blindly.
app.Logger.LogInformation(
    "Trusting X-Forwarded-For/X-Forwarded-Proto only from {TrustedProxyNetwork}.",
    trustedProxyNetwork);

app.UseHttpsRedirection();
app.UseAuthentication();

// After authentication, because the invitation and join limits count the
// signed-in account rather than the host, and before this runs there is no
// account to count — the partition would quietly fall back to an address, and
// the limit would be the wrong limit rather than a missing one. Reading the
// token here rather than earlier also means the claim has been verified, so it
// is not something a caller can set to refill their own budget.
app.UseRateLimiter();

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapSavedMediaEndpoints();
app.MapWatchLogEndpoints();
app.MapEpisodeProgressEndpoints();
app.MapRecommendationFeedbackEndpoints();
app.MapListEndpoints();
app.MapInvitationEndpoints();
app.MapPollEndpoints();

app.MapHub<ListHub>("/hubs/list");

// No .RequireAuthorization() -- Docker's HEALTHCHECK and any orchestrator
// probing this need anonymous access.
app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// Exposed so the integration tests can drive the real application through
/// <c>WebApplicationFactory</c> instead of a stand-in host.
/// </summary>
public partial class Program;