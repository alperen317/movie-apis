using Movie.Domain.Lists;

namespace Movie.Application.Abstractions.Email;

/// <summary>
/// Composes and sends the "you've been invited to a list" email. Handlers
/// depend on this rather than on <see cref="IEmailSender"/> plus a template,
/// the same reason <see cref="IVerificationEmailSender"/> exists.
/// </summary>
/// <remarks>
/// Send-and-forget, deliberately: a delivery failure must not undo an
/// invitation that already exists, which is how the Supabase edge function
/// this replaces behaved too.
/// </remarks>
public interface IListInviteEmailSender
{
    Task SendAsync(ListMember invitation, string listName, CancellationToken cancellationToken = default);
}
