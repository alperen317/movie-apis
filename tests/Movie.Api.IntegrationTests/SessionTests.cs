using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class SessionTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task A_confirmed_account_can_sign_in_and_use_the_token()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);

        var tokens = await SignInAsync(client, email);

        tokens.ShouldNotBeNull();
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        tokens.ExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);

        // The token is the whole point, so it is exercised rather than assumed.
        var me = await Authenticated(tokens.AccessToken).GetAsync("/me");
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Finishing_sign_up_already_hands_back_a_usable_session()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = Password });
        var code = factory.Emails.CodeSentTo(email);

        var verified = await client.PostAsJsonAsync("/auth/verify-email", new { email, code });
        var tokens = await verified.Content.ReadFromJsonAsync<TokenResponse>();

        // Otherwise the user would be asked to type the password they just
        // chose two screens earlier.
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        tokens.ShouldNotBeNull();
        (await Authenticated(tokens.AccessToken).GetAsync("/me")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unconfirmed_account_is_told_so_only_once_the_password_is_right()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = Password });

        var wrongPassword = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password = "not the right one" });
        var rightPassword = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });

        // With the wrong password the answer must be indistinguishable from an
        // address that has no account at all, or anyone could ask this endpoint
        // which addresses are registered.
        (await TitleOf(wrongPassword)).ShouldBe("invalid_credentials");
        (await TitleOf(rightPassword)).ShouldBe("email_not_confirmed");
    }

    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_are_answered_alike()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);

        var unknown = await client.PostAsJsonAsync(
            "/auth/login",
            new { email = NewEmail(), password = Password });
        var wrong = await client.PostAsJsonAsync("/auth/login", new { email, password = "wrong" });

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TitleOf(unknown)).ShouldBe(await TitleOf(wrong));
    }

    [Fact]
    public async Task Refreshing_returns_a_new_pair_and_retires_the_old_token()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);
        var first = (await SignInAsync(client, email))!;

        var refreshed = await client.PostAsJsonAsync(
            "/auth/refresh",
            new { refreshToken = first.RefreshToken });
        var second = await refreshed.Content.ReadFromJsonAsync<TokenResponse>();

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        second!.RefreshToken.ShouldNotBe(first.RefreshToken);
        (await Authenticated(second.AccessToken).GetAsync("/me")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // Reusing the spent one is refused, which is what makes a stolen copy
        // detectable rather than silently useful.
        var replay = await client.PostAsJsonAsync(
            "/auth/refresh",
            new { refreshToken = first.RefreshToken });
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_retires_the_token_it_was_given()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);
        var tokens = (await SignInAsync(client, email))!;

        var loggedOut = await client.PostAsJsonAsync(
            "/auth/logout",
            new { refreshToken = tokens.RefreshToken });

        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refreshed = await client.PostAsJsonAsync(
            "/auth/refresh",
            new { refreshToken = tokens.RefreshToken });
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_on_one_device_leaves_another_signed_in()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);
        var phone = (await SignInAsync(client, email))!;
        var tablet = (await SignInAsync(client, email))!;

        await client.PostAsJsonAsync("/auth/logout", new { refreshToken = phone.RefreshToken });

        var stillWorks = await client.PostAsJsonAsync(
            "/auth/refresh",
            new { refreshToken = tablet.RefreshToken });
        stillWorks.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_made_up_refresh_token_is_refused()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/refresh",
            new { refreshToken = Convert.ToBase64String(new byte[32]) });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<string> CreateConfirmedAccountAsync(HttpClient client)
    {
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = Password });
        var code = factory.Emails.CodeSentTo(email);
        await client.PostAsJsonAsync("/auth/verify-email", new { email, code });

        return email;
    }

    private static async Task<TokenResponse?> SignInAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });

        return await response.Content.ReadFromJsonAsync<TokenResponse>();
    }

    private HttpClient Authenticated(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<string?> TitleOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())?.Title;

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    private sealed record TokenResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken);

    private sealed record ProblemBody(string Title);
}