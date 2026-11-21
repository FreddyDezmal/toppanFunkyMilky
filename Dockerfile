# Milky Shaky Custom Drinks: one container serves the API and the built website on one origin (M14 decision D-2).
# Build from the repository root:  docker build -t milkshake .
# No secrets are used or stored at build time: every secret is set by the host at run time (configuration reference:
# docs/deployment/configuration-reference.md). The site is built with VITE_API_BASE_URL empty, so it calls /api on
# its own origin.

# 1. The website (Vite production build).
FROM node:24-bookworm-slim AS site
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
ENV VITE_API_BASE_URL=""
RUN npm run build

# 2. The API (Release publish; warnings are errors, as in CI).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY backend/src/ backend/src/
RUN dotnet restore backend/src/Milkshake.Api/Milkshake.Api.csproj
RUN dotnet publish backend/src/Milkshake.Api/Milkshake.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# 3. Runtime: ASP.NET Core only (no SDK, no Node), as the image's non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /app ./
COPY --from=site /src/frontend/dist ./wwwroot
# Workstation garbage collection: lower memory use on the 512 MB free instance (one small CPU share either way).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
# Plain HTTP inside the platform: the host (Koyeb) ends TLS and forwards X-Forwarded-For/Proto (ForwardedHeaders:Enabled).
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Milkshake.Api.dll"]
