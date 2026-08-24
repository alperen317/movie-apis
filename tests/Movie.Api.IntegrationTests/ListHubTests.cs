using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Movie.Domain.Lists;
using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// The realtime side of a shared list. <see cref="SharedListTests"/> covers who
/// may reach a list over HTTP; this covers the same question over the hub, plus
/// whether a mutation actually reaches a connected member.
/// </summary>
public sealed class ListHubTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task A_member_can_join_their_lists_group()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        await using var connection = await ConnectedAsync(owner);

        // No exception is the assertion: JoinList only throws when it refuses.
        await connection.InvokeAsync("JoinList", listId);
    }

    [Fact]
    public async Task Joining_a_list_you_are_not_in_is_refused()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        await using var connection = await ConnectedAsync(stranger);

        var thrown = await Should.ThrowAsync<HubException>(
            () => connection.InvokeAsync("JoinList", listId));

        // Same non-answer as the REST endpoints: this also fires for a list id
        // that does not exist at all, and the two are indistinguishable here.
        thrown.Message.ShouldContain("not_a_member");
    }

    [Fact]
    public async Task A_member_hears_about_an_item_a_co_member_adds()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        await using var connection = await ConnectedAsync(member);
        var itemAdded = new TaskCompletionSource<ItemAddedPayload>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ItemAddedPayload>("ItemAdded", payload => itemAdded.TrySetResult(payload));
        await connection.InvokeAsync("JoinList", listId);

        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        var received = await itemAdded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Title.ShouldBe("Inception");
        // Regression check: MediaType is typed as `string` here rather than the
        // domain enum on purpose. The hub protocol has its own JsonSerializerOptions,
        // separate from ConfigureHttpJsonOptions — without AddJsonProtocol's
        // JsonStringEnumConverter, this field goes out as a raw number, which a
        // JS client (no enum awareness) reads as an unusable `0`/`1` instead of
        // "movie"/"tv". Deserializing that number into a C# enum here would have
        // masked the bug; a `string` target makes the mismatch fail loudly instead.
        received.MediaType.ShouldBe("movie");
    }

    [Fact]
    public async Task A_member_hears_about_a_rename()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        await using var connection = await ConnectedAsync(owner);
        var renamed = new TaskCompletionSource<ListRenamedPayload>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ListRenamedPayload>("ListRenamed", payload => renamed.TrySetResult(payload));
        await connection.InvokeAsync("JoinList", listId);

        await owner.Client.PutAsJsonAsync($"/lists/{listId}", new { name = "Best Picture" });

        var received = await renamed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Name.ShouldBe("Best Picture");
    }

    [Fact]
    public async Task A_member_hears_about_a_removed_item()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        await using var connection = await ConnectedAsync(member);
        var itemRemoved = new TaskCompletionSource<ItemRemovedPayload>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ItemRemovedPayload>("ItemRemoved", payload => itemRemoved.TrySetResult(payload));
        await connection.InvokeAsync("JoinList", listId);

        await owner.Client.DeleteAsync($"/lists/{listId}/items/movie/27205");

        var received = await itemRemoved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.MediaId.ShouldBe(27205);
        // Same regression this guards against as A_member_hears_about_an_item_a_co_member_adds.
        received.MediaType.ShouldBe("movie");
    }

    [Fact]
    public async Task Leaving_the_group_stops_the_notifications()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        await using var connection = await ConnectedAsync(member);
        var calls = 0;
        connection.On<ItemAddedPayload>("ItemAdded", _ => calls++);
        await connection.InvokeAsync("JoinList", listId);
        await connection.InvokeAsync("LeaveList", listId);

        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        // Nothing to await here, since nothing is supposed to arrive — give the
        // broadcast a moment it would otherwise have used, then check it did not.
        await Task.Delay(TimeSpan.FromSeconds(2));
        calls.ShouldBe(0);
    }

    private async Task<HubConnection> ConnectedAsync(MovieApiFactory.SignedInUser user)
    {
        var accessToken = user.Client.DefaultRequestHeaders.Authorization!.Parameter!;

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/list").ToString(), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();

                // TestServer has no real socket underneath it, so the transport
                // WebSockets would otherwise negotiate to has nothing to run on.
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

        await connection.StartAsync();

        return connection;
    }

    private async Task<Guid> CreateListAsync(MovieApiFactory.SignedInUser owner, string name)
    {
        var response = await owner.Client.PostAsJsonAsync("/lists", new { name });
        var list = await response.Content.ReadFromJsonAsync<CreatedListDto>();

        return list!.Id;
    }

    /// <remarks>
    /// Written straight to the database — see <see cref="SharedListTests"/> for
    /// why. What is under test here is what a connected member is told, not how
    /// they came to be one.
    /// </remarks>
    private async Task AddMemberAsync(Guid listId, Guid userId, MemberStatus status)
    {
        await using var context = factory.CreateContext();

        context.ListMembers.Add(new ListMember
        {
            ListId = listId,
            UserId = userId,
            Status = status,
            RespondedAt = status is MemberStatus.Accepted ? DateTime.UtcNow : null,
        });

        await context.SaveChangesAsync();
    }

    private static readonly object Inception = new
    {
        id = 27205,
        mediaType = "movie",
        title = "Inception",
        posterPath = "/inception.jpg",
        voteAverage = 8.4m,
        year = "2010",
        genres = new[] { "Action" },
    };

    private sealed record CreatedListDto(Guid Id);

    private sealed record ItemAddedPayload(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("mediaType")] string MediaType);

    private sealed record ItemRemovedPayload(
        [property: JsonPropertyName("mediaId")] int MediaId,
        [property: JsonPropertyName("mediaType")] string MediaType);

    private sealed record ListRenamedPayload([property: JsonPropertyName("name")] string Name);
}
