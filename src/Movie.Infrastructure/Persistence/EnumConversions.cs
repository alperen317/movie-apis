using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Movie.Infrastructure.Persistence;

/// <summary>
/// Persists an enum as its snake_case name. EF's built-in
/// <c>HasConversion&lt;string&gt;()</c> would write <c>"Movie"</c>; Supabase
/// stored <c>'movie'</c>, and the check constraints, existing exports and the
/// mobile client's string literals all assume that casing.
/// </summary>
/// <remarks>
/// Single-word values are unaffected, so this only shows up on the likes of
/// <c>EmailConfirmation</c>, which would otherwise land as
/// <c>emailconfirmation</c> — the one lower-case run in a schema that is
/// snake_case everywhere else.
/// </remarks>
public static class EnumConversions
{
    public static PropertyBuilder<TEnum> HasLowerCaseStringConversion<TEnum>(
        this PropertyBuilder<TEnum> builder)
        where TEnum : struct, Enum =>
        builder.HasConversion(ToText<TEnum>(), FromText<TEnum>());

    public static ComplexTypePropertyBuilder<TEnum> HasLowerCaseStringConversion<TEnum>(
        this ComplexTypePropertyBuilder<TEnum> builder)
        where TEnum : struct, Enum =>
        builder.HasConversion(ToText<TEnum>(), FromText<TEnum>());

    private static System.Linq.Expressions.Expression<Func<TEnum, string>> ToText<TEnum>()
        where TEnum : struct, Enum =>
        value => ToSnakeCase(value.ToString()!);

    private static System.Linq.Expressions.Expression<Func<string, TEnum>> FromText<TEnum>()
        where TEnum : struct, Enum =>
        text => Enum.Parse<TEnum>(text.Replace("_", string.Empty), true);

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
