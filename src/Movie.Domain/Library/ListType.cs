namespace Movie.Domain.Library;

/// <summary>
/// The user's two personal lists. In Supabase this was
/// <c>list_type text check (list_type in ('favorite','watchlist'))</c>.
/// Unrelated to shared lists (<c>MediaList</c>).
/// </summary>
public enum ListType
{
    Favorite,
    Watchlist,
}