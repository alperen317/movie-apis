using System.Security.Cryptography;

namespace Movie.Domain.Lists;

/// <summary>
/// Produces the code that lets someone join a shared list by typing it in.
/// </summary>
public static class JoinCodeGenerator
{
    /// <summary>
    /// A 32-symbol alphabet with the easily confused characters (0/O, 1/I)
    /// removed, so a code can be read off a phone and typed by hand.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 8;

    /// <summary>
    /// Uses cryptographically secure randomness. The Supabase equivalent
    /// (<c>generate_list_join_code</c>) was built on Postgres' <c>random()</c>,
    /// which is a PRNG, not a CSPRNG. Since holding the code grants immediate
    /// membership with no approval step, the code is effectively an
    /// authorization token and must not be predictable.
    /// </summary>
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}