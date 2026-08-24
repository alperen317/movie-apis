using System.Net;
using System.Net.Http.Json;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Its own factory, and therefore its own container and its own request budget,
/// so squeezing the limit down here cannot starve any other test.
/// </summary>
public sealed class ThrottledApiFactory : MovieApiFactory
{
    protected override int EmailDispatchPermitLimit => 2;
}

/// <summary>
/// Separate from <see cref="ThrottledApiFactory"/> because that one squeezes
/// sign-ups, and these tests need to be able to create the accounts they use.
/// </summary>
public sealed class ThrottledListApiFactory : MovieApiFactory
{
    protected override int ListInvitationPermitLimit => 2;

    protected override int JoinAttemptPermitLimit => 2;
}

public sealed class RateLimitingTests(ThrottledApiFactory factory) : IClassFixture<ThrottledApiFactory>
{
    [Fact]
    public async Task Repeated_sign_ups_from_one_caller_are_throttled()
    {
        var client = factory.CreateClient();

        var accepted = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/auth/register",
                new { email = $"{Guid.NewGuid():N}@example.com", password = "correct horse battery" });

            accepted.Add(response.StatusCode);
        }

        // Two get through, the third does not. Varying the email each time is
        // the point: the limit is keyed on the caller, not on the address they
        // claim, so changing it buys nothing.
        accepted[0].ShouldBe(HttpStatusCode.Accepted);
        accepted[1].ShouldBe(HttpStatusCode.Accepted);
        accepted[2].ShouldBe(HttpStatusCode.TooManyRequests);
    }
}