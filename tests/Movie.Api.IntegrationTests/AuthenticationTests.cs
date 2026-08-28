using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class AuthenticationTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task A_protected_endpoint_rejects_a_request_with_no_token()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_protected_endpoint_accepts_a_signed_token_and_sees_the_caller()
    {
        // The account is seeded directly rather than registered, so this stays
        // a test of the token pipeline. It has to exist at all because /me
        // reads the row — a valid signature alone is not enough.
        await SeedCallerAsync();
        var client = CreateAuthenticatedClient(IssueToken().Value);

        var response = await client.GetAsync("/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        body.ShouldNotBeNull();
        body.Id.ShouldBe(UserId.ToString());
        body.Email.ShouldBe("caller@example.com");
    }

    private async Task SeedCallerAsync()
    {
        await using var context = factory.CreateContext();

        if (await context.Users.FindAsync(UserId) is not null)
        {
            return;
        }

        context.Users.Add(new ApplicationUser
        {
            Id = UserId,
            Email = "caller@example.com",
            UserName = "caller@example.com",
            NormalizedEmail = "CALLER@EXAMPLE.COM",
            NormalizedUserName = "CALLER@EXAMPLE.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_token_whose_payload_was_altered_is_rejected()
    {
        // Flipping one character of the payload breaks the signature, which is
        // the only thing standing between a client and any user id it likes.
        var token = IssueToken().Value;
        var parts = token.Split('.');
        parts[1] = parts[1][..^1] + (parts[1][^1] == 'A' ? 'B' : 'A');

        var client = CreateAuthenticatedClient(string.Join('.', parts));

        var response = await client.GetAsync("/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void An_issued_token_is_short_lived()
    {
        var token = IssueToken();

        // Signing out cannot revoke a token, so its lifetime is the window in
        // which a signed-out one still works. Kept short for that reason.
        token.ExpiresAtUtc.ShouldBeInRange(
            DateTime.UtcNow.AddMinutes(14),
            DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task A_missing_account_costs_as_much_as_a_wrong_password()
    {
        // Both branches must run a full PBKDF2 verification, or the time to
        // fail becomes a side channel for "does this email have an account"
        // -- see LoginCommandHandler's DummyUser/DummyPasswordHash. Best-of-3
        // and a generous ratio bound keep this from flaking on CI jitter
        // while still catching a regression back to an early return.
        var owner = await factory.SignedInAsync();
        var client = factory.CreateClient();

        var wrongPassword = await FastestLoginAttemptAsync(client, owner.Email, "not the password");
        var missingAccount = await FastestLoginAttemptAsync(
            client,
            $"{Guid.NewGuid():N}@example.com",
            "not the password");

        var slower = Math.Max(wrongPassword.TotalMilliseconds, missingAccount.TotalMilliseconds);
        var faster = Math.Max(1, Math.Min(wrongPassword.TotalMilliseconds, missingAccount.TotalMilliseconds));

        (slower / faster).ShouldBeLessThan(3);
    }

    private static async Task<TimeSpan> FastestLoginAttemptAsync(HttpClient client, string email, string password)
    {
        var fastest = TimeSpan.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
            stopwatch.Stop();

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            if (stopwatch.Elapsed < fastest)
            {
                fastest = stopwatch.Elapsed;
            }
        }

        return fastest;
    }

    private AccessToken IssueToken()
    {
        using var scope = factory.Services.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>();

        return generator.Generate(new ApplicationUser
        {
            Id = UserId,
            Email = "caller@example.com",
            UserName = "caller@example.com",
        });
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record MeResponse(string Id, string Email);
}