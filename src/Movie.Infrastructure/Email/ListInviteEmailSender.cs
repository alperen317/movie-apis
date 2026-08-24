using Microsoft.Extensions.Logging;
using Movie.Application.Abstractions.Email;
using Movie.Domain.Lists;

namespace Movie.Infrastructure.Email;

/// <inheritdoc cref="IListInviteEmailSender"/>
public sealed class ListInviteEmailSender(IEmailSender sender, ILogger<ListInviteEmailSender> logger)
    : IListInviteEmailSender
{
    public async Task SendAsync(
        ListMember invitation,
        string listName,
        CancellationToken cancellationToken = default)
    {
        var recipient = invitation.User?.Email;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        var inviterLabel = invitation.InvitedBy?.DisplayName
            ?? invitation.InvitedBy?.Email
            ?? "Bir arkadaşın";

        try
        {
            await sender.SendAsync(
                ListInviteEmailTemplates.For(recipient, listName, inviterLabel),
                cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Send-and-forget, same as the Supabase edge function it replaces:
            // a failed delivery must not undo an invitation that already
            // exists in the database.
            logger.LogWarning(e, "Failed to send list invite email to {Recipient}.", recipient);
        }
    }
}
