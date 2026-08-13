using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Movie.Api.OpenApi;

/// <summary>
/// Declares the bearer scheme on the generated document and marks the
/// endpoints that need it.
/// </summary>
/// <remarks>
/// Without this the document describes every operation as anonymous: the
/// documentation browser offers nowhere to paste a token, and a protected
/// endpoint just answers 401 with no hint as to why.
/// </remarks>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token issued by /auth/login.",
        };

        // Applied to the whole document rather than per operation: everything
        // outside /auth is authenticated, and the anonymous ones are harmless
        // to send a token to.
        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
            },
        ];

        return Task.CompletedTask;
    }
}
