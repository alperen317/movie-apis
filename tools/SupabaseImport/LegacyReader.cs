using Movie.Domain.Library;
using Movie.Domain.Lists;
using Movie.Domain.Media;
using Movie.Domain.Users;

using Npgsql;

namespace SupabaseImport;

/// <summary>
/// Reads the old Supabase project's tables and turns each row straight into
/// the entity its new-schema counterpart uses — see the mapping table in the
/// migration plan for why this is a column-for-column correspondence rather
/// than anything that needs its own DTOs.
/// </summary>
/// <remarks>
/// Materializes each table into a <see cref="List{T}"/> rather than
/// streaming. Fine for the size of dataset one Supabase free/pro project
/// accumulates pre-launch; if this is ever run against something much
/// larger, switch to yielding rows as they're read instead.
/// </remarks>
internal sealed class LegacyReader(NpgsqlConnection source)
{
    public async Task<List<ApplicationUser>> ReadUsersAsync(CancellationToken ct)
    {
        var hasDeletedAt = await HasColumnAsync("auth", "users", "deleted_at", ct);

        var sql = $"""
            select u.id, u.email, u.encrypted_password, u.email_confirmed_at, u.created_at,
                   p.display_name, p.avatar_variant, p.avatar_seed, p.watch_region
            from auth.users u
            join public.profiles p on p.id = u.id
            {(hasDeletedAt ? "where u.deleted_at is null" : string.Empty)}
            """;

        var users = new List<ApplicationUser>();

        await using var command = new NpgsqlCommand(sql, source);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var email = reader.GetString(reader.GetOrdinal("email"));

            users.Add(new ApplicationUser
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),

                // Matches how RegisterCommandHandler sets it up for every new
                // account -- the address is the username, there is no other.
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),

                EmailConfirmed = !reader.IsDBNull(reader.GetOrdinal("email_confirmed_at")),

                // GoTrue's bcrypt hash, unchanged -- LegacyPasswordHasher
                // verifies it, and it gets replaced with a PBKDF2 hash the
                // first time this account signs in successfully.
                PasswordHash = reader.GetString(reader.GetOrdinal("encrypted_password")),

                // Identity requires both to be non-null; there is no old
                // equivalent, so these accounts get fresh ones.
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),

                LockoutEnabled = true,

                DisplayName = GetNullableString(reader, "display_name"),
                AvatarVariant = Enum.Parse<AvatarVariant>(
                    reader.GetString(reader.GetOrdinal("avatar_variant")), ignoreCase: true),
                AvatarSeed = GetNullableString(reader, "avatar_seed"),
                WatchRegion = GetNullableString(reader, "watch_region"),

                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            });
        }

        return users;
    }

    public Task<List<MediaList>> ReadListsAsync(CancellationToken ct) => ReadAllAsync(
        "select id, name, created_by, join_code, created_at, updated_at from public.lists",
        reader => new MediaList
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            CreatedById = reader.GetGuid(reader.GetOrdinal("created_by")),
            JoinCode = reader.GetString(reader.GetOrdinal("join_code")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        },
        ct);

    public Task<List<ListMember>> ReadListMembersAsync(CancellationToken ct) => ReadAllAsync(
        """
        select id, list_id, user_id, role, status, invited_by, created_at, responded_at
        from public.list_members
        """,
        reader => new ListMember
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            ListId = reader.GetGuid(reader.GetOrdinal("list_id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            Role = Enum.Parse<MemberRole>(reader.GetString(reader.GetOrdinal("role")), ignoreCase: true),
            Status = Enum.Parse<MemberStatus>(reader.GetString(reader.GetOrdinal("status")), ignoreCase: true),
            InvitedById = GetNullableGuid(reader, "invited_by"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            RespondedAt = GetNullableDateTime(reader, "responded_at"),
        },
        ct);

    public Task<List<ListItem>> ReadListItemsAsync(CancellationToken ct) => ReadAllAsync(
        """
        select id, list_id, media_id, media_type, title, poster_path, vote_average, year,
               genres, added_by, created_at
        from public.list_items
        """,
        reader => new ListItem
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            ListId = reader.GetGuid(reader.GetOrdinal("list_id")),
            MediaId = reader.GetInt32(reader.GetOrdinal("media_id")),
            MediaType = Enum.Parse<MediaType>(reader.GetString(reader.GetOrdinal("media_type")), ignoreCase: true),
            Title = reader.GetString(reader.GetOrdinal("title")),
            PosterPath = GetNullableString(reader, "poster_path"),
            VoteAverage = GetNullableDecimal(reader, "vote_average"),
            Year = GetNullableString(reader, "year"),
            Genres = reader.GetFieldValue<string[]>(reader.GetOrdinal("genres")),
            AddedById = reader.GetGuid(reader.GetOrdinal("added_by")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        },
        ct);

    public Task<List<ListPoll>> ReadListPollsAsync(CancellationToken ct) => ReadAllAsync(
        "select id, list_id, created_by, deadline, created_at from public.list_polls",
        reader => new ListPoll
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            ListId = reader.GetGuid(reader.GetOrdinal("list_id")),
            CreatedById = reader.GetGuid(reader.GetOrdinal("created_by")),
            Deadline = reader.GetDateTime(reader.GetOrdinal("deadline")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        },
        ct);

    public Task<List<ListPollCandidate>> ReadListPollCandidatesAsync(CancellationToken ct) => ReadAllAsync(
        "select id, poll_id, list_item_id from public.list_poll_candidates",
        reader => new ListPollCandidate
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            PollId = reader.GetGuid(reader.GetOrdinal("poll_id")),
            ListItemId = reader.GetGuid(reader.GetOrdinal("list_item_id")),
        },
        ct);

    public Task<List<ListPollVote>> ReadListPollVotesAsync(CancellationToken ct) => ReadAllAsync(
        "select id, poll_id, candidate_id, user_id, created_at from public.list_poll_votes",
        reader => new ListPollVote
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            PollId = reader.GetGuid(reader.GetOrdinal("poll_id")),
            CandidateId = reader.GetGuid(reader.GetOrdinal("candidate_id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        },
        ct);

    public Task<List<SavedMedia>> ReadSavedMediaAsync(CancellationToken ct) => ReadAllAsync(
        """
        select id, user_id, list_type, media_id, media_type, title, poster_path, vote_average,
               year, genres, created_at
        from public.saved_media
        """,
        reader => new SavedMedia
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            ListType = Enum.Parse<ListType>(reader.GetString(reader.GetOrdinal("list_type")), ignoreCase: true),
            MediaId = reader.GetInt32(reader.GetOrdinal("media_id")),
            MediaType = Enum.Parse<MediaType>(reader.GetString(reader.GetOrdinal("media_type")), ignoreCase: true),
            Title = reader.GetString(reader.GetOrdinal("title")),
            PosterPath = GetNullableString(reader, "poster_path"),
            VoteAverage = GetNullableDecimal(reader, "vote_average"),
            Year = GetNullableString(reader, "year"),
            Genres = reader.GetFieldValue<string[]>(reader.GetOrdinal("genres")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        },
        ct);

    public Task<List<WatchLogEntry>> ReadWatchLogAsync(CancellationToken ct) => ReadAllAsync(
        """
        select id, user_id, media_id, media_type, title, poster_path, vote_average, year,
               genres, watched_at, rating, note, created_at
        from public.watch_log
        """,
        reader => new WatchLogEntry
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            MediaId = reader.GetInt32(reader.GetOrdinal("media_id")),
            MediaType = Enum.Parse<MediaType>(reader.GetString(reader.GetOrdinal("media_type")), ignoreCase: true),
            Title = reader.GetString(reader.GetOrdinal("title")),
            PosterPath = GetNullableString(reader, "poster_path"),
            VoteAverage = GetNullableDecimal(reader, "vote_average"),
            Year = GetNullableString(reader, "year"),
            Genres = reader.GetFieldValue<string[]>(reader.GetOrdinal("genres")),
            WatchedAt = reader.GetDateTime(reader.GetOrdinal("watched_at")),
            Rating = GetNullableInt(reader, "rating"),
            Note = GetNullableString(reader, "note"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        },
        ct);

    public Task<List<EpisodeProgress>> ReadEpisodeProgressAsync(CancellationToken ct) => ReadAllAsync(
        "select user_id, show_id, season_number, episode_number, watched_at from public.episode_progress",
        reader => new EpisodeProgress
        {
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            ShowId = reader.GetInt32(reader.GetOrdinal("show_id")),
            SeasonNumber = reader.GetInt32(reader.GetOrdinal("season_number")),
            EpisodeNumber = reader.GetInt32(reader.GetOrdinal("episode_number")),
            WatchedAt = reader.GetDateTime(reader.GetOrdinal("watched_at")),
        },
        ct);

    public Task<List<RecommendationFeedback>> ReadRecommendationFeedbackAsync(CancellationToken ct) => ReadAllAsync(
        "select id, user_id, media_id, media_type, created_at from public.recommendation_feedback",
        reader => new RecommendationFeedback
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            MediaId = reader.GetInt32(reader.GetOrdinal("media_id")),
            MediaType = Enum.Parse<MediaType>(reader.GetString(reader.GetOrdinal("media_type")), ignoreCase: true),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        },
        ct);

    private async Task<List<T>> ReadAllAsync<T>(
        string sql,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct)
    {
        var results = new List<T>();

        await using var command = new NpgsqlCommand(sql, source);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            results.Add(map(reader));
        }

        return results;
    }

    private async Task<bool> HasColumnAsync(string schema, string table, string column, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            select exists (
                select 1 from information_schema.columns
                where table_schema = @schema and table_name = @table and column_name = @column
            )
            """,
            source);

        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);

        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static decimal? GetNullableDecimal(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTime? GetNullableDateTime(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}