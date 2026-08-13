using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;
using Movie.Infrastructure.Authentication;
using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class VerificationCodeServiceTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();

    [Fact]
    public async Task A_freshly_issued_code_is_accepted()
    {
        var user = await CreateUserAsync();

        var code = await IssueAsync(user, CodePurpose.EmailConfirmation);
        var result = await ConsumeAsync(user, CodePurpose.EmailConfirmation, code);

        code.Length.ShouldBe(VerificationCode.Length);
        code.ShouldAllBe(character => char.IsAsciiDigit(character));
        result.ShouldBe(VerificationResult.Success);
    }

    [Fact]
    public async Task A_code_cannot_be_used_twice()
    {
        var user = await CreateUserAsync();

        var code = await IssueAsync(user, CodePurpose.EmailConfirmation);
        await ConsumeAsync(user, CodePurpose.EmailConfirmation, code);

        var replay = await ConsumeAsync(user, CodePurpose.EmailConfirmation, code);

        replay.ShouldBe(VerificationResult.Invalid);
    }

    [Fact]
    public async Task A_code_issued_for_one_purpose_does_not_satisfy_the_other()
    {
        var user = await CreateUserAsync();

        var code = await IssueAsync(user, CodePurpose.EmailConfirmation);

        var result = await ConsumeAsync(user, CodePurpose.PasswordReset, code);

        result.ShouldBe(VerificationResult.Invalid);
    }

    [Fact]
    public async Task Reissuing_kills_the_previous_code()
    {
        var user = await CreateUserAsync();

        var first = await IssueAsync(user, CodePurpose.EmailConfirmation);
        var second = await IssueAsync(user, CodePurpose.EmailConfirmation);

        // Otherwise every "resend" would leave another working code behind.
        (await ConsumeAsync(user, CodePurpose.EmailConfirmation, first))
            .ShouldBe(VerificationResult.Invalid);
        (await ConsumeAsync(user, CodePurpose.EmailConfirmation, second))
            .ShouldBe(VerificationResult.Success);
    }

    [Fact]
    public async Task Guessing_is_cut_off_after_the_attempt_limit()
    {
        var user = await CreateUserAsync();

        var code = await IssueAsync(user, CodePurpose.EmailConfirmation);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 1; attempt < VerificationCode.MaxAttempts; attempt++)
        {
            (await ConsumeAsync(user, CodePurpose.EmailConfirmation, wrong))
                .ShouldBe(VerificationResult.Invalid);
        }

        (await ConsumeAsync(user, CodePurpose.EmailConfirmation, wrong))
            .ShouldBe(VerificationResult.TooManyAttempts);

        // The real point of the limit: the correct code is dead too, so an
        // attacker cannot simply keep guessing until they land on it.
        (await ConsumeAsync(user, CodePurpose.EmailConfirmation, code))
            .ShouldBe(VerificationResult.TooManyAttempts);
    }

    [Fact]
    public async Task An_expired_code_is_reported_as_expired_rather_than_wrong()
    {
        var user = await CreateUserAsync();

        var code = await IssueAsync(user, CodePurpose.EmailConfirmation);

        await using (var context = postgres.CreateContext())
        {
            await context.VerificationCodes
                .Where(x => x.UserId == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        var result = await ConsumeAsync(user, CodePurpose.EmailConfirmation, code);

        result.ShouldBe(VerificationResult.Expired);
    }

    [Fact]
    public async Task Codes_are_not_stored_in_a_readable_form()
    {
        var user = await CreateUserAsync();

        var code = await IssueAsync(user, CodePurpose.EmailConfirmation);

        await using var context = postgres.CreateContext();
        var stored = await context.VerificationCodes
            .AsNoTracking()
            .SingleAsync(x => x.UserId == user.Id);

        stored.CodeHash.ShouldNotContain(code);
    }

    // Each call gets its own context, the way a scoped service does per HTTP
    // request. Sharing one across issue and verify would let the change tracker
    // answer from memory and hide whatever was actually written.
    private async Task<string> IssueAsync(ApplicationUser user, CodePurpose purpose)
    {
        await using var context = postgres.CreateContext();
        return await new VerificationCodeService(context, Hasher).IssueAsync(user, purpose);
    }

    private async Task<VerificationResult> ConsumeAsync(
        ApplicationUser user,
        CodePurpose purpose,
        string code)
    {
        await using var context = postgres.CreateContext();
        return await new VerificationCodeService(context, Hasher).ConsumeAsync(user, purpose, code);
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
