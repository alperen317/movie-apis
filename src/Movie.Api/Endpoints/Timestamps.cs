namespace Movie.Api.Endpoints;

/// <summary>
/// Turns a timestamp a caller sent into the instant the database stores.
/// </summary>
/// <remarks>
/// <para>
/// Every time column here is <c>timestamptz</c>, which holds an instant rather
/// than a reading off a wall clock. A value with an offset — <c>...Z</c> or
/// <c>...+03:00</c>, which is what <c>toISOString()</c> produces — names one.
/// A bare <c>2024-01-01T20:00:00</c> does not: it is twelve different instants
/// depending on where the person was.
/// </para>
/// <para>
/// So it is refused rather than assumed. Reading it as UTC would silently move
/// a diary entry by hours for anyone not on UTC, and reading it as the server's
/// local time would make the answer depend on where the server happens to run.
/// Left alone it reaches Npgsql, which refuses it as a 500 — the right call
/// made at the wrong layer, and with nothing useful to say to the caller.
/// </para>
/// </remarks>
internal static class Timestamps
{
    public static bool TryToUtc(DateTime value, out DateTime utc)
    {
        utc = value.Kind switch
        {
            DateTimeKind.Utc => value,

            // An offset was supplied and has already been applied, which is
            // why converting is exact rather than a guess.
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => default,
        };

        return value.Kind is not DateTimeKind.Unspecified;
    }

    /// <param name="index">
    /// Which item in a batch this is. Null where there is only one.
    /// </param>
    public static IResult NotAnInstant(string name, int? index = null)
    {
        var prefix = index is { } i ? $"[{i}]." : string.Empty;

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [$"{prefix}{name}"] =
                ["Must carry a time zone offset, for example 2024-01-01T20:00:00Z."],
        });
    }
}