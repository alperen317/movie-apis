using System.Net;

using Movie.Application.Abstractions.Email;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Email;

/// <summary>
/// Ported from the Supabase edge function's emailTemplates.ts, keeping the same
/// Turkish copy and layout so the emails users already receive do not change.
/// </summary>
internal static class VerificationEmailTemplates
{
    public static EmailMessage For(string recipient, CodePurpose purpose, string code) => purpose switch
    {
        CodePurpose.EmailConfirmation => new EmailMessage(
            recipient,
            "Previously — E-posta doğrulama kodun",
            Build(
                heading: "Hesabını doğrula",
                body: $"{WebUtility.HtmlEncode(recipient)} için Previously hesabını doğrulamak "
                      + "üzeresin. Aşağıdaki kodu uygulamaya gir.",
                code)),

        CodePurpose.PasswordReset => new EmailMessage(
            recipient,
            "Previously — Şifre sıfırlama kodun",
            Build(
                heading: "Şifreni sıfırla",
                body: $"{WebUtility.HtmlEncode(recipient)} hesabının şifresini sıfırlamak için "
                      + "aşağıdaki kodu uygulamaya gir.",
                code)),

        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "No template for this purpose."),
    };

    private static string Build(string heading, string body, string code) =>
        $"""
        <!doctype html>
        <html lang="tr">
          <body style="margin:0;padding:0;background:#131313;font-family:-apple-system,Segoe UI,Roboto,sans-serif;">
            <div style="max-width:420px;margin:0 auto;padding:32px 16px;">
              <div style="background:#1c1c1c;border:1px solid rgba(255,255,255,0.08);border-radius:16px;padding:32px;">
                <p style="margin:0 0 16px;font-size:12px;letter-spacing:2px;text-transform:uppercase;color:#F5C451;">Previously</p>
                <h1 style="margin:0 0 16px;font-size:20px;line-height:1.3;color:#f4f4f4;">{heading}</h1>
                <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#a1a1aa;">{body}</p>
                <div style="background:#131313;border-radius:8px;padding:20px;text-align:center;">
                  <span id="verification-code" style="font-size:32px;font-weight:700;letter-spacing:8px;color:#F5C451;">{code}</span>
                </div>
                <p style="margin:24px 0 0;font-size:12px;line-height:1.5;color:#71717a;">
                  Bu kodu kimseyle paylaşma. Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin.
                </p>
              </div>
            </div>
          </body>
        </html>
        """;
}