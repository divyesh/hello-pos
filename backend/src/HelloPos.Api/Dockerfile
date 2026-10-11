# Hello POS API image (feature 002, research R7). Build context: repository root.
#   podman build -f backend/src/HelloPos.Api/Dockerfile -t hellopos-api .
# No settings or secrets are baked in: the connection string, Seq address and environment come
# from environment variables at run time.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer: changes only when project files or central versions change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY backend/src/HelloPos.Kernel/HelloPos.Kernel.csproj backend/src/HelloPos.Kernel/
COPY backend/src/HelloPos.Api/HelloPos.Api.csproj backend/src/HelloPos.Api/
RUN dotnet restore backend/src/HelloPos.Api/HelloPos.Api.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/HelloPos.Api/HelloPos.Api.csproj -c Release --no-restore -o /app/publish

# Chiseled runtime: no shell, no package manager, non-root user; listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelloPos.Api.dll"]
