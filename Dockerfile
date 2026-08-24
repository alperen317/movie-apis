# Restore before the source is copied, so a change to a .cs file does not
# invalidate the layer that pulls down every NuGet package.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Movie.Domain/Movie.Domain.csproj src/Movie.Domain/
COPY src/Movie.Application/Movie.Application.csproj src/Movie.Application/
COPY src/Movie.Infrastructure/Movie.Infrastructure.csproj src/Movie.Infrastructure/
COPY src/Movie.Api/Movie.Api.csproj src/Movie.Api/
RUN dotnet restore src/Movie.Api/Movie.Api.csproj

COPY src/ src/
RUN dotnet publish src/Movie.Api/Movie.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Not in the base image; only needed so HEALTHCHECK below can hit the app
# from inside its own container.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# The base image already runs as a non-root user.
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Movie.Api.dll"]
