using Mediator;

using Movie.Application.Features.Lists;
using Movie.Domain.Media;

using static Movie.Api.Endpoints.EnumParameters;

namespace Movie.Api.Endpoints;

/// <summary>
/// Shared lists, their rosters and their contents.
/// </summary>
/// <remarks>
/// Every one of these resolves the list through <c>IListAccess</c> and answers
/// 404 when it comes back empty. Not 403: a list the caller has nothing to do
/// with should not be distinguishable from one that does not exist, or the
/// error itself would confirm it is there.
/// </remarks>
public static class ListEndpoints
{
    private const int MaxNameLength = 60;

    public static void MapListEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/lists").RequireAuthorization().WithTags("Lists");

        lists.MapGet("/", GetMyLists);
        lists.MapPost("/", CreateList);
        lists.MapGet("/{id:guid}", GetList);
        lists.MapPut("/{id:guid}", RenameList);
        lists.MapDelete("/{id:guid}", DeleteList);
        lists.MapGet("/{id:guid}/members", GetMembers);
        lists.MapGet("/{id:guid}/items", GetItems);
        lists.MapPost("/{id:guid}/items", AddItem);
        lists.MapDelete("/{id:guid}/items/{mediaType}/{mediaId:int}", RemoveItem);

        // Not under /lists: a membership is removed by its own id, and the
        // caller leaving a list does not need to name the list to leave it.
        var members = app.MapGroup("/members").RequireAuthorization().WithTags("Lists");

        members.MapDelete("/{membershipId:guid}", RemoveMember);
    }

    private static async Task<IResult> GetMyLists(ISender sender) =>
        Results.Ok(await sender.Send(new GetMyListsQuery()));

    private static async Task<IResult> CreateList(NameRequest request, ISender sender)
    {
        if (Invalid(request.Name, out var name) is { } problem)
        {
            return problem;
        }

        var list = await sender.Send(new CreateListCommand(name));

        return Results.Created($"/lists/{list.Id}", list);
    }

    private static async Task<IResult> GetList(Guid id, ISender sender)
    {
        var list = await sender.Send(new GetListQuery(id));

        return list is null ? Results.NotFound() : Results.Ok(list);
    }

    private static async Task<IResult> RenameList(Guid id, NameRequest request, ISender sender)
    {
        if (Invalid(request.Name, out var name) is { } problem)
        {
            return problem;
        }

        var list = await sender.Send(new RenameListCommand(id, name));

        return list is null ? Results.NotFound() : Results.Ok(list);
    }

    private static async Task<IResult> DeleteList(Guid id, ISender sender)
    {
        var deleted = await sender.Send(new DeleteListCommand(id));

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetMembers(Guid id, ISender sender)
    {
        var members = await sender.Send(new GetListMembersQuery(id));

        return members is null ? Results.NotFound() : Results.Ok(members);
    }

    private static async Task<IResult> RemoveMember(Guid membershipId, ISender sender)
    {
        var outcome = await sender.Send(new RemoveMemberCommand(membershipId));

        return outcome switch
        {
            RemoveMemberOutcome.Removed => Results.NoContent(),
            RemoveMemberOutcome.CreatorCannotLeave => Results.Conflict(new
            {
                error = "creator_cannot_leave",
                message = "A list's creator cannot leave it. Delete the list instead.",
            }),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> GetItems(Guid id, ISender sender)
    {
        var items = await sender.Send(new GetListItemsQuery(id));

        return items is null ? Results.NotFound() : Results.Ok(items);
    }

    private static async Task<IResult> AddItem(Guid id, TitleRequest request, ISender sender)
    {
        if (request.Validate() is { } problem)
        {
            return problem;
        }

        var item = await sender.Send(new AddListItemCommand(id, request.ToSnapshot()));

        // 200 rather than 201 even when the row is new, because the same call
        // is how a caller learns a co-member already added the title.
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> RemoveItem(
        Guid id,
        string mediaType,
        int mediaId,
        ISender sender)
    {
        if (!TryParse<MediaType>(mediaType, out var kind))
        {
            return NotOneOf<MediaType>(nameof(mediaType));
        }

        var member = await sender.Send(new RemoveListItemCommand(id, mediaId, kind));

        return member ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// Kept in step with the <c>lists_name_length</c> check constraint, which
    /// measures the trimmed name — so this does too, and stores what it
    /// measured.
    /// </summary>
    /// <param name="trimmed">
    /// What will actually be stored, so the value that was measured is the
    /// value that gets written.
    /// </param>
    private static IResult? Invalid(string? name, out string trimmed)
    {
        trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A name is required."],
            });
        }

        return trimmed.Length <= MaxNameLength
            ? null
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"A name cannot be longer than {MaxNameLength} characters."],
            });
    }

    public sealed record NameRequest(string? Name);
}