namespace Movie.Api.Endpoints;

/// <summary>
/// How much a single bulk request may carry.
/// </summary>
/// <remarks>
/// The bulk endpoints exist for the TV Time / Letterboxd importer, which sends
/// a whole library. Without a ceiling one request could spend as much memory as
/// the caller cares to ask for. An oversized request is refused rather than
/// truncated, because a silently shortened import looks like a complete one.
/// </remarks>
internal static class Batches
{
    /// <summary>The size the importer already chunks its titles into.</summary>
    public const int MaxTitles = 500;

    /// <summary>
    /// Higher than <see cref="MaxTitles"/> because an episode is four small
    /// columns rather than a copy of a TMDB record, and because marking a
    /// long-running show watched in one go is an ordinary thing to do.
    /// </summary>
    public const int MaxEpisodes = 2000;

    public static IResult TooLarge(int limit, string what) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["$"] = [$"A batch cannot hold more than {limit} {what}."],
        });
}
