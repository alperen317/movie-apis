namespace Movie.Application.Abstractions;

/// <summary>
/// Who the request is being made by.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Null when nobody is signed in — an anonymous endpoint, or a context
    /// created outside a request. Ownership filters compare against this, so a
    /// null here matches no rows rather than all of them.
    /// </summary>
    Guid? Id { get; }
}
