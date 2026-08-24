using System.Net;

using Shouldly;

namespace Movie.Api.IntegrationTests;

public sealed class HealthCheckTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task Health_is_reachable_without_a_token()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}