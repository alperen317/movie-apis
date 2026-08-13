using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class RegistrationTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private const string GoodPassword = "correct horse battery";

    [Fact]
    public async Task Signing_up_sends_a_code_and_verifying_it_confirms_the_account()
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var registered = await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });
        registered.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var code = factory.Emails.CodeSentTo(email);
        code.Length.ShouldBe(6);

        var verified = await client.PostAsJsonAsync("/auth/verify-email", new { email, code });
        verified.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = factory.CreateContext();
        var user = await context.Users.SingleAsync(u => u.Email == email);
        user.EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_password_that_fails_policy_is_reported()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register",
            new { email = NewEmail(), password = "short" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_weak_password_is_rejected_even_for_an_address_already_taken()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });

        var response = await client.PostAsJsonAsync("/auth/register", new { email, password = "short" });

        // If the password were checked only after the address lookup, a weak
        // password would come back rejected for a free address and accepted for
        // a taken one — telling an attacker which addresses have accounts.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signing_up_with_a_confirmed_address_is_answered_the_same_and_sends_nothing()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });
        var code = factory.Emails.CodeSentTo(email);
        await client.PostAsJsonAsync("/auth/verify-email", new { email, code });

        factory.Emails.Clear();
        var response = await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });

        // Same answer as a brand new address, so the response tells an attacker
        // nothing — and no mail goes out, so the endpoint cannot be turned into
        // a way to send messages to someone on demand.
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Emails.LastTo(email).ShouldBeNull();
    }

    [Fact]
    public async Task Signing_up_again_before_confirming_resends_rather_than_failing()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });
        var first = factory.Emails.CodeSentTo(email);

        await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });
        var second = factory.Emails.CodeSentTo(email);

        second.ShouldNotBe(first);
        (await Verify(client, email, first)).ShouldBe(HttpStatusCode.BadRequest);
        (await Verify(client, email, second)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Resending_to_an_unknown_address_is_accepted_and_sends_nothing()
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/auth/resend-verification", new { email });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Emails.LastTo(email).ShouldBeNull();
    }

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/auth/register", new { email, password = GoodPassword });
        var code = factory.Emails.CodeSentTo(email);
        var wrong = code == "000000" ? "111111" : "000000";

        (await Verify(client, email, wrong)).ShouldBe(HttpStatusCode.BadRequest);

        await using var context = factory.CreateContext();
        var user = await context.Users.SingleAsync(u => u.Email == email);
        user.EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task Verifying_an_unknown_address_looks_like_a_wrong_code()
    {
        var client = factory.CreateClient();

        var response = await Verify(client, NewEmail(), "123456");

        response.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpStatusCode> Verify(HttpClient client, string email, string code) =>
        (await client.PostAsJsonAsync("/auth/verify-email", new { email, code })).StatusCode;

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";
}
