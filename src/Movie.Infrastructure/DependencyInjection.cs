using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Authentication;
using Movie.Application.Abstractions.Email;
using Movie.Application.Abstractions.Library;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Users;
using Movie.Infrastructure.Authentication;
using Movie.Infrastructure.Email;
using Movie.Infrastructure.Library;
using Movie.Infrastructure.Lists;
using Movie.Infrastructure.Persistence;
using Movie.Infrastructure.Realtime;

namespace Movie.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddPersistence(configuration);
        services.AddIdentity();
        services.AddJwtAuthentication(configuration);
        services.AddEmail(configuration, isDevelopment);
        services.AddRealtime();

        return services;
    }

    private static void AddRealtime(this IServiceCollection services)
    {
        // See HttpContextPropagationHubFilter for why this is needed at all:
        // without it, ICurrentUser — and everything built on it, IListAccess
        // included — silently sees nobody signed in from inside a hub method.
        services.AddSingleton<HttpContextPropagationHubFilter>();
        services
            .AddSignalR(options => options.AddFilter<HttpContextPropagationHubFilter>())
            // The hub protocol has its own JsonSerializerOptions, entirely
            // separate from ConfigureHttpJsonOptions below — without this, an
            // enum in an event payload (ItemRemovedPayload.MediaType, for
            // instance) goes out as its raw numeric value instead of the
            // lower-case string every REST response already uses.
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        // The only way a handler reaches a list's connected members. Handlers
        // do not touch IHubContext directly, the same reason they do not touch
        // MovieDbContext's `lists` table directly — one seam, easy to find.
        services.AddScoped<IListEventPublisher, SignalRListEventPublisher>();
    }

    private static void AddEmail(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddScoped<IVerificationEmailSender, VerificationEmailSender>();
        services.AddScoped<IListInviteEmailSender, ListInviteEmailSender>();

        if (isDevelopment)
        {
            // Writes the code straight to the log so the flow can be exercised
            // without an email provider. Registering this anywhere else would
            // publish live codes to the logs, so it is confined to this branch.
            services.AddScoped<IEmailSender, LoggingEmailSender>();
            return;
        }

        var section = configuration.GetSection(BrevoOptions.SectionName);
        var brevo = section.Get<BrevoOptions>()
            ?? throw new InvalidOperationException($"The '{BrevoOptions.SectionName}' section is missing.");

        // Fail at startup rather than on the first email a handler tries to
        // send — the same reasoning as the signing key check below.
        if (string.IsNullOrWhiteSpace(brevo.ApiKey) || string.IsNullOrWhiteSpace(brevo.SenderEmail))
        {
            throw new InvalidOperationException(
                $"{BrevoOptions.SectionName}:ApiKey and {BrevoOptions.SectionName}:SenderEmail "
                + "must both be configured outside development.");
        }

        services.Configure<BrevoOptions>(section);
        services.AddHttpClient<IEmailSender, BrevoEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/v3/");
            client.DefaultRequestHeaders.Add("api-key", brevo.ApiKey);
            client.DefaultRequestHeaders.Add("accept", "application/json");
        });
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Database is not configured.");

        // The ownership filters on the context read from this.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

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

            // Just the one provider, not AddDefaultTokenProviders(). The
            // six-digit codes users type are ours (see VerificationCode);
            // this covers the internal token ResetPasswordAsync needs, which
            // never leaves the server and is never seen by anyone.
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
                TokenOptions.DefaultProvider);

        services.AddDataProtection();

        // The six-digit codes are ours, not Identity's: see VerificationCode
        // for why its TOTP provider was not used.
        services.AddScoped<IVerificationCodeService, VerificationCodeService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // The only route to a shared list. See IListAccess for why handlers do
        // not query `lists` themselves.
        services.AddScoped<IListAccess, ListAccess>();
        services.AddScoped<IListStore, ListStore>();
        services.AddScoped<IInvitationStore, InvitationStore>();
        services.AddScoped<IPollStore, PollStore>();

        // The caller's own content. Registered next to the list access for the
        // same reason: these are the only paths to those tables, and each one
        // resolves the owner itself rather than being handed one.
        services.AddScoped<ISavedMediaStore, SavedMediaStore>();
        services.AddScoped<IWatchLogStore, WatchLogStore>();
        services.AddScoped<IEpisodeProgressStore, EpisodeProgressStore>();
        services.AddScoped<IRecommendationFeedbackStore, RecommendationFeedbackStore>();
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

                // A browser's WebSocket handshake cannot carry an Authorization
                // header, so the SignalR client puts the token on the query
                // string instead. Only honoured under /hubs: everywhere else a
                // token in the URL would end up in logs and history for no
                // reason, since those requests can set the header instead.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];

                        if (!string.IsNullOrEmpty(accessToken)
                            && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });
    }
}