using System.Net;
using System.Net.Http.Json;

using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class PasswordResetTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private const string OldPassword = "correct horse battery";
    private const string NewPassword = "a different long one";

    [Fact]
    public async Task A_reset_replaces_the_password()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);

        await client.PostAsJsonAsync("/auth/forgot-password", new { email });
        var code = factory.Emails.CodeSentTo(email);

        var reset = await client.PostAsJsonAsync(
            "/auth/reset-password",
            new { email, code, newPassword = NewPassword });

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SignInAsync(client, email, NewPassword)).ShouldBe(HttpStatusCode.OK);
        (await SignInAsync(client, email, OldPassword)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_reset_ends_every_session_the_account_had()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);

        var phone = await TokensFromSignInAsync(client, email, OldPassword);
        var tablet = await TokensFromSignInAsync(client, email, OldPassword);

        await client.PostAsJsonAsync("/auth/forgot-password", new { email });
        var code = factory.Emails.CodeSentTo(email);
        await client.PostAsJsonAsync("/auth/reset-password", new { email, code, newPassword = NewPassword });

        // People reset a password because somebody else knows it. Leaving that
        // person's sessions alive would defeat the exercise.
        (await RefreshAsync(client, phone)).ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(client, tablet)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_reset_code_cannot_confirm_an_email()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = OldPassword });
        var signUpCode = factory.Emails.CodeSentTo(email);
        await client.PostAsJsonAsync("/auth/verify-email", new { email, code = signUpCode });

        await client.PostAsJsonAsync("/auth/forgot-password", new { email });
        var resetCode = factory.Emails.CodeSentTo(email);

        // Purposes are separate so a code mailed for one flow cannot be spent
        // on the other.
        var misused = await client.PostAsJsonAsync(
            "/auth/verify-email",
            new { email, code = resetCode });

        misused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_without_spending_the_code()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);
        await client.PostAsJsonAsync("/auth/forgot-password", new { email });
        var code = factory.Emails.CodeSentTo(email);

        var rejected = await client.PostAsJsonAsync(
            "/auth/reset-password",
            new { email, code, newPassword = "short" });

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The code has to survive, or a typo would send the user back to their
        // inbox for a new one.
        var retried = await client.PostAsJsonAsync(
            "/auth/reset-password",
            new { email, code, newPassword = NewPassword });

        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_reset_code_works_once()
    {
        var client = factory.CreateClient();
        var email = await CreateConfirmedAccountAsync(client);
        await client.PostAsJsonAsync("/auth/forgot-password", new { email });
        var code = factory.Emails.CodeSentTo(email);
        await client.PostAsJsonAsync("/auth/reset-password", new { email, code, newPassword = NewPassword });

        var replay = await client.PostAsJsonAsync(
            "/auth/reset-password",
            new { email, code, newPassword = "yet another long one" });

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Asking_to_reset_an_unknown_address_is_accepted_and_sends_nothing()
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new { email });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Emails.LastTo(email).ShouldBeNull();
    }

    [Fact]
    public async Task An_account_that_never_finished_sign_up_gets_no_reset_code()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = OldPassword });
        factory.Emails.Clear();

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new { email });

        // There is no password to reset yet, and sending anyway would make this
        // a way to mail someone repeatedly.
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Emails.LastTo(email).ShouldBeNull();
    }

    private async Task<string> CreateConfirmedAccountAsync(HttpClient client)
    {
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = OldPassword });
        var code = factory.Emails.CodeSentTo(email);
        await client.PostAsJsonAsync("/auth/verify-email", new { email, code });

        return email;
    }

    private static async Task<HttpStatusCode> SignInAsync(
        HttpClient client,
        string email,
        string password) =>
        (await client.PostAsJsonAsync("/auth/login", new { email, password })).StatusCode;

    private static async Task<string> TokensFromSignInAsync(
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();

        return tokens!.RefreshToken;
    }

    private static async Task<HttpStatusCode> RefreshAsync(HttpClient client, string refreshToken) =>
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken })).StatusCode;

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    private sealed record TokenResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken);
}