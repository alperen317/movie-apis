using Mediator;
using Movie.Application.Abstractions.Authentication;
using Movie.Application.Features.Authentication;

namespace Movie.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").AllowAnonymous();

        group.MapPost("/register", Register).RequireRateLimiting(RateLimiting.EmailDispatch);

        group.MapPost("/resend-verification", ResendVerification)
            .RequireRateLimiting(RateLimiting.EmailDispatch);

        group.MapPost("/verify-email", VerifyEmail)
            .RequireRateLimiting(RateLimiting.CodeSubmission);
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
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Email and password are required."],
            });
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
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required."],
            });
        }

        await sender.Send(new ResendVerificationCommand(request.Email));

        // Always 202, for the same reason register is.
        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmail(VerifyEmailRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Email and code are required."],
            });
        }

        var result = await sender.Send(new VerifyEmailCommand(request.Email, request.Code));

        return result switch
        {
            VerificationResult.Success => Results.NoContent(),

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

    private static IResult Problem(string code, string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: code);

    public sealed record RegisterRequest(string Email, string Password);

    public sealed record ResendVerificationRequest(string Email);

    public sealed record VerifyEmailRequest(string Email, string Code);
}
