using Movie.Api.Endpoints;
using Movie.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapMeEndpoints();

app.Run();

/// <summary>
/// Exposed so the integration tests can drive the real application through
/// <c>WebApplicationFactory</c> instead of a stand-in host.
/// </summary>
public partial class Program;
