using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Movie.Infrastructure.Persistence;

/// <summary>
/// Persists an enum as its lower-cased name. EF's built-in
/// <c>HasConversion&lt;string&gt;()</c> would write <c>"Movie"</c>; Supabase
/// stored <c>'movie'</c>, and the check constraints, existing exports and the
/// mobile client's string literals all assume that casing.
/// </summary>
/// <remarks>
/// Two overloads because entity properties and complex-type properties are
/// configured through unrelated builder types.
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
        value => value.ToString()!.ToLowerInvariant();

    private static System.Linq.Expressions.Expression<Func<string, TEnum>> FromText<TEnum>()
        where TEnum : struct, Enum =>
        text => Enum.Parse<TEnum>(text, true);
}
