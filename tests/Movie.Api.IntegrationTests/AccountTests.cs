using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class AccountTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task The_profile_comes_back_with_its_defaults()
    {
        var (client, email) = await SignedInAsync();

        var profile = await client.GetFromJsonAsync<Profile>("/me");

        profile.ShouldNotBeNull();
        profile.Email.ShouldBe(email);
        profile.DisplayName.ShouldBeNull();
        profile.AvatarVariant.ShouldBe("beam");
        profile.WatchRegion.ShouldBeNull();
    }

    [Fact]
    public async Task Editing_the_profile_persists()
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PutAsJsonAsync("/me", new
        {
            displayName = "Alperen",
            avatarVariant = "bauhaus",
            avatarSeed = "seed-42",
            watchRegion = "tr",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var reloaded = await client.GetFromJsonAsync<Profile>("/me");
        reloaded!.DisplayName.ShouldBe("Alperen");
        reloaded.AvatarVariant.ShouldBe("bauhaus");
        reloaded.AvatarSeed.ShouldBe("seed-42");

        // Stored upper-case so a region never has to be compared case-insensitively.
        reloaded.WatchRegion.ShouldBe("TR");
    }

    [Fact]
    public async Task An_omitted_field_is_cleared_rather_than_left_alone()
    {
        var (client, _) = await SignedInAsync();
        await client.PutAsJsonAsync("/me", new
        {
            displayName = "Alperen",
            avatarVariant = "ring",
            watchRegion = "TR",
        });

        // The whole editable profile is replaced, so leaving a field out is how
        // you clear it.
        await client.PutAsJsonAsync("/me", new { avatarVariant = "ring" });

        var reloaded = await client.GetFromJsonAsync<Profile>("/me");
        reloaded!.DisplayName.ShouldBeNull();
        reloaded.WatchRegion.ShouldBeNull();
        reloaded.AvatarVariant.ShouldBe("ring");
    }

    [Fact]
    public async Task A_blank_display_name_is_stored_as_absent()
    {
        var (client, _) = await SignedInAsync();

        await client.PutAsJsonAsync("/me", new { displayName = "   ", avatarVariant = "beam" });

        var reloaded = await client.GetFromJsonAsync<Profile>("/me");
        reloaded!.DisplayName.ShouldBeNull();
    }

    [Fact]
    public async Task An_overlong_display_name_is_refused()
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PutAsJsonAsync("/me", new
        {
            displayName = new string('a', 61),
            avatarVariant = "beam",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_region_that_is_not_two_letters_is_refused()
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PutAsJsonAsync("/me", new
        {
            avatarVariant = "beam",
            watchRegion = "TUR",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_the_account_takes_everything_with_it()
    {
        var (client, email) = await SignedInAsync();

        var deleted = await client.DeleteAsync("/me");

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = factory.CreateContext();
        (await context.Users.AnyAsync(u => u.Email == email)).ShouldBeFalse();
        (await context.RefreshTokens.CountAsync()).ShouldBe(
            await context.RefreshTokens.CountAsync(t =>
                context.Users.Any(u => u.Id == t.UserId)));
    }

    [Fact]
    public async Task A_token_stops_working_the_moment_its_account_is_gone()
    {
        var (client, _) = await SignedInAsync();
        await client.DeleteAsync("/me");

        // The token is still correctly signed and unexpired. What stops it is
        // that /me reads the row rather than trusting the claims.
        var response = await client.GetAsync("/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_in_again_after_deletion_fails()
    {
        var (client, email) = await SignedInAsync();
        await client.DeleteAsync("/me");

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/auth/login", new { email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpClient Client, string Email)> SignedInAsync()
    {
        var setup = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";

        await setup.PostAsJsonAsync("/auth/register", new { email, password = Password });
        var code = factory.Emails.CodeSentTo(email);
        var verified = await setup.PostAsJsonAsync("/auth/verify-email", new { email, code });
        var tokens = await verified.Content.ReadFromJsonAsync<TokenResponse>();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return (client, email);
    }

    private sealed record Profile(
        Guid Id,
        string Email,
        string? DisplayName,
        string AvatarVariant,
        string? AvatarSeed,
        string? WatchRegion);

    private sealed record TokenResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken);
}
