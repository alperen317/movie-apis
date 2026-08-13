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
    public void An_issued_token_expires_an_hour_out()
    {
        var token = IssueToken();

        token.ExpiresAtUtc.ShouldBeInRange(
            DateTime.UtcNow.AddMinutes(59),
            DateTime.UtcNow.AddMinutes(61));
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
