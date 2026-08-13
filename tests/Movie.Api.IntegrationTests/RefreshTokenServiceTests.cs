using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;
using Movie.Infrastructure.Authentication;
using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class RefreshTokenServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_fresh_token_can_be_exchanged_for_a_new_one()
    {
        var user = await CreateUserAsync();

        var token = await IssueAsync(user);
        var outcome = await RotateAsync(token);

        outcome.Succeeded.ShouldBeTrue();
        outcome.UserId.ShouldBe(user.Id);
        outcome.RefreshToken.ShouldNotBe(token);
    }

    [Fact]
    public async Task Rotating_retires_the_token_that_was_spent()
    {
        var user = await CreateUserAsync();

        var first = await IssueAsync(user);
        var second = (await RotateAsync(first)).RefreshToken!;

        (await RotateAsync(second)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Replaying_a_spent_token_drops_every_session_for_that_user()
    {
        var user = await CreateUserAsync();

        // Two devices, each with its own live token.
        var phone = await IssueAsync(user);
        var tablet = await IssueAsync(user);

        var rotatedPhone = (await RotateAsync(phone)).RefreshToken!;

        // The spent one turning up again means someone else has a copy. Which
        // holder is the thief is unknowable, so nobody keeps their session.
        (await RotateAsync(phone)).Succeeded.ShouldBeFalse();

        (await RotateAsync(rotatedPhone)).Succeeded.ShouldBeFalse();
        (await RotateAsync(tablet)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Signing_out_on_one_device_leaves_the_other_signed_in()
    {
        var user = await CreateUserAsync();
        var phone = await IssueAsync(user);
        var tablet = await IssueAsync(user);

        await using (var context = postgres.CreateContext())
        {
            await new RefreshTokenService(context).RevokeAsync(phone);
        }

        (await RotateAsync(phone)).Succeeded.ShouldBeFalse();
        (await RotateAsync(tablet)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var user = await CreateUserAsync();
        var token = await IssueAsync(user);

        await using (var context = postgres.CreateContext())
        {
            await context.RefreshTokens
                .Where(x => x.UserId == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        (await RotateAsync(token)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_token_is_refused_and_signing_out_with_one_is_not_an_error()
    {
        var stranger = Convert.ToBase64String(new byte[32]);

        (await RotateAsync(stranger)).Succeeded.ShouldBeFalse();

        await using var context = postgres.CreateContext();
        await Should.NotThrowAsync(() => new RefreshTokenService(context).RevokeAsync(stranger));
    }

    [Fact]
    public async Task Tokens_are_not_stored_in_a_readable_form()
    {
        var user = await CreateUserAsync();

        var token = await IssueAsync(user);

        await using var context = postgres.CreateContext();
        var stored = await context.RefreshTokens.AsNoTracking().SingleAsync(x => x.UserId == user.Id);

        stored.TokenHash.ShouldNotBe(token);
    }

    // A context per call, matching the scoped lifetime a request gets. Sharing
    // one would let the change tracker answer from memory instead of the row.
    private async Task<string> IssueAsync(ApplicationUser user)
    {
        await using var context = postgres.CreateContext();
        return await new RefreshTokenService(context).IssueAsync(user);
    }

    private async Task<RefreshOutcome> RotateAsync(string token)
    {
        await using var context = postgres.CreateContext();
        return await new RefreshTokenService(context).RotateAsync(token);
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
        };
        user.UserName = user.Email;
        user.NormalizedEmail = user.Email!.ToUpperInvariant();
        user.NormalizedUserName = user.NormalizedEmail;
        user.SecurityStamp = Guid.NewGuid().ToString();

        await using var context = postgres.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }
}
