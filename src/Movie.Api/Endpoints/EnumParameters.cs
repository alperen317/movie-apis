namespace Movie.Api.Endpoints;

/// <summary>
/// Reads an enum out of a route segment or a query string.
/// </summary>
/// <remarks>
/// <para>
/// Enum values travel as the lower-case names the database stores and the
/// mobile client already writes — <c>favorite</c>, <c>movie</c>. Request bodies
/// handle that themselves, because the JSON options in <c>Program</c> apply a
/// camel-case naming policy. Route and query binding does not go through those
/// options: it parses enums case-sensitively, so it would refuse the very
/// spellings the rest of the system uses.
/// </para>
/// <para>
/// Rather than change the wire format to suit the binder, these parameters
/// arrive as text and are parsed here — which also lets a bad value come back
/// as a 400 naming the parameter and listing what it accepts, instead of an
/// unexplained one from the framework.
/// </para>
/// </remarks>
internal static class EnumParameters
{
    public static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);

    /// <param name="name">
    /// The parameter as the caller wrote it, so the error points at their
    /// request rather than at ours.
    /// </param>
    public static IResult NotOneOf<TEnum>(string name)
        where TEnum : struct, Enum =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [name] =
            [
                "Must be one of: "
                + string.Join(", ", Enum.GetNames<TEnum>().Select(x => x.ToLowerInvariant()))
                + ".",
            ],
        });
}
