using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;
using Movie.Infrastructure.Authentication;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddIdentity();
        services.AddJwtAuthentication(configuration);

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Database is not configured.");

        services.AddDbContext<MovieDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
    }

    private static void AddIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Length over composition. Supabase enforced six characters and
                // nothing else; Identity's defaults demand a digit, both cases
                // and a symbol, which pushes people toward "Password1!" rather
                // than anything actually strong. There are no existing accounts
                // to keep compatible, so this is a free choice.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;

                // Mirrors Supabase: an account exists after sign-up but cannot
                // sign in until the emailed code is entered.
                options.SignIn.RequireConfirmedEmail = true;

                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<MovieDbContext>()
            .AddSignInManager()

            // Registers the TOTP-based email provider used for the six-digit
            // sign-up and password-reset codes.
            .AddDefaultTokenProviders();
    }

    private static void AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        var jwt = section.Get<JwtOptions>()
            ?? throw new InvalidOperationException($"The '{JwtOptions.SectionName}' section is missing.");

        // Fail at startup rather than issuing tokens signed with a weak or
        // absent key. In development the key comes from user-secrets, so an
        // unset key usually means `dotnet user-secrets set` was never run.
        if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < JwtOptions.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"{JwtOptions.SectionName}:SigningKey must be at least "
                + $"{JwtOptions.MinimumSigningKeyBytes} bytes for HMAC-SHA256.");
        }

        services.Configure<JwtOptions>(section);
        services.AddScoped<IAccessTokenGenerator, JwtAccessTokenGenerator>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Without this the handler rewrites 'sub' into the much longer
                // WS-Federation claim name, so reading it back would mean
                // referring to a claim nobody wrote.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),

                    // The default five minutes would keep a revoked or expired
                    // token working well past its stated life.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };
            });
    }
}
