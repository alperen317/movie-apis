using Mediator;
using Movie.Application.Abstractions.Authentication;
using Movie.Application.Features.Authentication;

namespace Movie.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Tagged explicitly: left alone, the generated document names the group
        // after this class, which is an implementation detail.
        var group = app.MapGroup("/auth").AllowAnonymous().WithTags("Authentication");

        group.MapPost("/register", Register).RequireRateLimiting(RateLimiting.EmailDispatch);

        group.MapPost("/resend-verification", ResendVerification)
            .RequireRateLimiting(RateLimiting.EmailDispatch);

        group.MapPost("/verify-email", VerifyEmail)
            .RequireRateLimiting(RateLimiting.CredentialSubmission);

        group.MapPost("/login", Login).RequireRateLimiting(RateLimiting.CredentialSubmission);

        // Not throttled: it carries a 256-bit secret, so there is nothing to
        // guess, and throttling it would punish a client whose access token
        // expired at an awkward moment.
        group.MapPost("/refresh", Refresh);

        group.MapPost("/logout", Logout);

        group.MapPost("/forgot-password", ForgotPassword)
            .RequireRateLimiting(RateLimiting.EmailDispatch);

        group.MapPost("/reset-password", ResetPassword)
            .RequireRateLimiting(RateLimiting.CredentialSubmission);
    }

    /// <summary>
    /// Answers 202 whether the address was free, pending or already in use.
    /// Only password problems come back as errors, because those reveal nothing
    /// about the address.
    /// </summary>
    private static async Task<IResult> Register(RegisterRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return MissingFields("Email and password are required.");
        }

        var result = await sender.Send(new RegisterCommand(request.Email, request.Password));

        return result.IsAccepted
            ? Results.Accepted()
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = [.. result.PasswordErrors],
            });
    }

    private static async Task<IResult> ResendVerification(ResendVerificationRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return MissingFields("Email is required.");
        }

        await sender.Send(new ResendVerificationCommand(request.Email));

        // Always 202, for the same reason register is.
        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmail(VerifyEmailRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code))
        {
            return MissingFields("Email and code are required.");
        }

        var result = await sender.Send(new VerifyEmailCommand(request.Email, request.Code));

        return result.Outcome switch
        {
            // Confirming signs the user in, so sign-up ends with a usable
            // session rather than a trip back to the login screen.
            VerificationResult.Success => Results.Ok(TokenResponse.From(result.Tokens!)),

            // Separated from a plain rejection so the client can say "ask for a
            // new code" instead of "wrong code". Both require already knowing
            // the address, so neither leaks anything the other does not.
            VerificationResult.Expired => Problem("code_expired", "That code has expired."),
            VerificationResult.TooManyAttempts => Problem(
                "too_many_attempts",
                "Too many incorrect attempts. Request a new code."),
            _ => Problem("invalid_code", "That code is not valid."),
        };
    }

    private static async Task<IResult> Login(LoginRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return MissingFields("Email and password are required.");
        }

        var result = await sender.Send(new LoginCommand(request.Email, request.Password));

        if (result.Tokens is not null)
        {
            return Results.Ok(TokenResponse.From(result.Tokens));
        }

        return result.Failure switch
        {
            LoginFailure.EmailNotConfirmed => Unauthorized(
                "email_not_confirmed",
                "Confirm your email address before signing in."),
            LoginFailure.LockedOut => Unauthorized(
                "locked_out",
                "Too many failed attempts. Try again later."),
            _ => Unauthorized("invalid_credentials", "That email or password is not right."),
        };
    }

    private static async Task<IResult> Refresh(RefreshRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return MissingFields("A refresh token is required.");
        }

        var tokens = await sender.Send(new RefreshCommand(request.RefreshToken));

        return tokens is null
            ? Unauthorized("invalid_refresh_token", "Sign in again.")
            : Results.Ok(TokenResponse.From(tokens));
    }

    private static async Task<IResult> Logout(RefreshRequest request, ISender sender)
    {
        // Signing out with a token that was already dead still leaves the
        // caller signed out, which is what they asked for.
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await sender.Send(new LogoutCommand(request.RefreshToken));
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPassword(ForgotPasswordRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return MissingFields("Email is required.");
        }

        await sender.Send(new ForgotPasswordCommand(request.Email));

        // Always 202: an unknown address must look exactly like a known one.
        return Results.Accepted();
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Code)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return MissingFields("Email, code and new password are required.");
        }

        var result = await sender.Send(
            new ResetPasswordCommand(request.Email, request.Code, request.NewPassword));

        if (result.PasswordErrors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = [.. result.PasswordErrors],
            });
        }

        return result.Outcome switch
        {
            VerificationResult.Success => Results.NoContent(),
            VerificationResult.Expired => Problem("code_expired", "That code has expired."),
            VerificationResult.TooManyAttempts => Problem(
                "too_many_attempts",
                "Too many incorrect attempts. Request a new code."),
            _ => Problem("invalid_code", "That code is not valid."),
        };
    }

    private static IResult MissingFields(string detail) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [detail] });

    private static IResult Problem(string code, string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: code);

    private static IResult Unauthorized(string code, string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status401Unauthorized, title: code);

    public sealed record RegisterRequest(string Email, string Password);

    public sealed record ResendVerificationRequest(string Email);

    public sealed record VerifyEmailRequest(string Email, string Code);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record RefreshRequest(string RefreshToken);

    public sealed record ForgotPasswordRequest(string Email);

    public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);

    /// <param name="ExpiresAt">
    /// So the client can refresh ahead of expiry rather than finding out
    /// through a failed request.
    /// </param>
    public sealed record TokenResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken)
    {
        public static TokenResponse From(AuthTokens tokens) =>
            new(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.RefreshToken);
    }
}
