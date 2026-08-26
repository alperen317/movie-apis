using System.Net;
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

    /// <remarks>
    /// Unlike <see cref="Leaving_the_group_stops_the_notifications"/>, the
    /// removed member never calls LeaveList themselves — nothing client-side
    /// tells their connection to leave the group. Regression test for the
    /// eviction that <c>IListEventPublisher.MemberEvictedAsync</c> now forces
    /// server-side: before it existed, a kicked member's still-open connection
    /// kept hearing about a list they could no longer reach over REST.
    /// </remarks>
    [Fact]
    public async Task Removing_a_member_stops_their_notifications()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        await using var connection = await ConnectedAsync(member);
        var calls = 0;
        connection.On<ItemAddedPayload>("ItemAdded", _ => calls++);
        await connection.InvokeAsync("JoinList", listId);

        var membershipId = await MembershipIdAsync(owner, listId, member.Id);
        await owner.Client.DeleteAsync($"/members/{membershipId}");

        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        // Nothing to await here, since nothing is supposed to arrive — give the
        // broadcast a moment it would otherwise have used, then check it did not.
        await Task.Delay(TimeSpan.FromSeconds(2));
        calls.ShouldBe(0);
    }

    /// <remarks>
    /// Regression test for DeleteAccountCommandHandler: before it called
    /// IListEventPublisher, deleting the account cascaded the list away at the
    /// DB level with no realtime signal at all — a co-member's client would
    /// just silently stop hearing about a list that, as far as it knew, still
    /// existed.
    /// </remarks>
    [Fact]
    public async Task Deleting_the_account_notifies_co_members_the_owned_list_is_gone()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        await using var connection = await ConnectedAsync(member);
        var listDeleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("ListDeleted", () => listDeleted.TrySetResult());
        await connection.InvokeAsync("JoinList", listId);

        var deleted = await owner.Client.DeleteAsync("/me");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await listDeleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <remarks>
    /// Same gap, the other direction: a list this account merely belongs to
    /// (not owns) loses this member's row through the cascade, but nothing
    /// told the list's remaining members the roster had changed.
    /// </remarks>
    [Fact]
    public async Task Deleting_the_account_notifies_the_owner_a_joined_lists_roster_changed()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        await using var connection = await ConnectedAsync(owner);
        var membersChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("MembersChanged", () => membersChanged.TrySetResult());
        await connection.InvokeAsync("JoinList", listId);

        var deleted = await member.Client.DeleteAsync("/me");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await membersChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <remarks>
    /// Regression test: removing an item cascades away any ListPollCandidate
    /// built on it (see ListPollCandidate), but RemoveListItemCommandHandler
    /// used to only broadcast ItemRemoved — a client watching the poll never
    /// heard that one of its candidates had just disappeared.
    /// </remarks>
    [Fact]
    public async Task Removing_a_poll_candidate_item_broadcasts_a_poll_update()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        var inceptionRowId = await AddItemAsync(owner, listId, Inception);
        var arrivalRowId = await AddItemAsync(owner, listId, Arrival);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/polls", new
        {
            deadline = DateTime.UtcNow.AddDays(1),
            itemIds = new[] { inceptionRowId, arrivalRowId },
        });

        await using var connection = await ConnectedAsync(owner);
        var pollUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("PollUpdated", () => pollUpdated.TrySetResult());
        await connection.InvokeAsync("JoinList", listId);

        await owner.Client.DeleteAsync($"/lists/{listId}/items/movie/27205");

        await pollUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10));
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

    private async Task<Guid> MembershipIdAsync(MovieApiFactory.SignedInUser asUser, Guid listId, Guid userId)
    {
        var members = await asUser.Client.GetFromJsonAsync<List<MemberDto>>($"/lists/{listId}/members");

        return members!.Single(m => m.UserId == userId).MembershipId;
    }

    /// <returns>The item's row id, which is what a poll's candidates point at.</returns>
    private static async Task<Guid> AddItemAsync(MovieApiFactory.SignedInUser owner, Guid listId, object title)
    {
        var response = await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", title);
        var item = await response.Content.ReadFromJsonAsync<ItemDto>();

        return item!.RowId;
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

    private static readonly object Arrival = new
    {
        id = 329865,
        mediaType = "movie",
        title = "Arrival",
        posterPath = "/arrival.jpg",
        voteAverage = 7.9m,
        year = "2016",
        genres = new[] { "Drama" },
    };

    private sealed record CreatedListDto(Guid Id);

    private sealed record MemberDto(Guid MembershipId, Guid UserId);

    private sealed record ItemDto(Guid RowId);

    private sealed record ItemAddedPayload(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("mediaType")] string MediaType);

    private sealed record ItemRemovedPayload(
        [property: JsonPropertyName("mediaId")] int MediaId,
        [property: JsonPropertyName("mediaType")] string MediaType);

    private sealed record ListRenamedPayload([property: JsonPropertyName("name")] string Name);
}