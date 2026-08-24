using System.Net;

using Movie.Application.Abstractions.Email;

namespace Movie.Infrastructure.Email;

/// <summary>
/// Ported from the Supabase edge function's send-list-invite-email/index.ts,
/// keeping the same Turkish copy and layout so the email users already
/// receive does not change.
/// </summary>
/// <remarks>
/// Unlike the edge function, the list name and inviter label are HTML-encoded
/// here — both are user-supplied text (a list's name, a display name) landing
/// straight in an HTML body. The edge function got away with it because
/// nothing sanitized either field before this point; this is the same
/// tightening phase 4d applied to poll candidates.
/// </remarks>
internal static class ListInviteEmailTemplates
{
    public static EmailMessage For(string recipient, string listName, string inviterLabel) => new(
        recipient,
        $"Previously — {inviterLabel} seni \"{listName}\" listesine davet etti",
        Build(listName, inviterLabel));

    private static string Build(string listName, string inviterLabel)
    {
        var safeListName = WebUtility.HtmlEncode(listName);
        var safeInviterLabel = WebUtility.HtmlEncode(inviterLabel);

        return $"""
        <!doctype html>
        <html lang="tr">
          <body style="margin:0;padding:0;background:#131313;font-family:-apple-system,Segoe UI,Roboto,sans-serif;">
            <div style="max-width:420px;margin:0 auto;padding:32px 16px;">
              <div style="background:#1c1c1c;border:1px solid rgba(255,255,255,0.08);border-radius:16px;padding:32px;">
                <p style="margin:0 0 16px;font-size:12px;letter-spacing:2px;text-transform:uppercase;color:#F5C451;">Previously</p>
                <h1 style="margin:0 0 16px;font-size:20px;line-height:1.3;color:#f4f4f4;">Bir listeye davet edildin</h1>
                <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#a1a1aa;">
                  {safeInviterLabel}, seni "{safeListName}" adlı paylaşımlı listeye davet etti. Kabul etmek için Previously'de Listeler sekmesini aç.
                </p>
                <p style="margin:24px 0 0;font-size:12px;line-height:1.5;color:#71717a;">
                  Bu daveti sen istemediysen bu e-postayı yok sayabilirsin.
                </p>
              </div>
            </div>
          </body>
        </html>
        """;
    }
}