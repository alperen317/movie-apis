namespace Movie.Domain.Media;

/// <summary>
/// TMDB content kind. Supabase repeated this on every table as
/// <c>media_type text check (media_type in ('movie','tv'))</c>; here it is one
/// enum, persisted as those same text values.
/// </summary>
public enum MediaType
{
    Movie,
    Tv,
}