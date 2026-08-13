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
COPY --from=build /app .

# The base image already runs as a non-root user.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Movie.Api.dll"]
