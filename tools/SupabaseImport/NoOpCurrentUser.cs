using Movie.Application.Abstractions;

namespace SupabaseImport;

/// <summary>
/// This tool writes directly through <c>MovieDbContext</c>, never through a
/// signed-in request, so there is no caller for the ownership filters on
/// <c>saved_media</c>/<c>watch_log</c>/<c>episode_progress</c>/
/// <c>recommendation_feedback</c> to compare against. Those filters only
/// narrow reads; bulk <c>Add</c> never consults them, so a constantly-null
/// caller is harmless here.
/// </summary>
internal sealed class NoOpCurrentUser : ICurrentUser
{
    public Guid? Id => null;
}