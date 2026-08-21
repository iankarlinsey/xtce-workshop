# Single application image: Kestrel serves both the API and the Angular build.
# Build context is the repo root. Base images pinned by digest for reproducible
# builds — re-pin deliberately when bumping Node/.NET.

FROM node:20-bookworm@sha256:8f693eaa7e0a8e71560c9a82b55fd54c2ae920a2ba5d2cde28bac7d1c01c9ba5 AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx ng build --configuration production

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS api-build
WORKDIR /src
COPY src/Xtce.Workshop.Model/Xtce.Workshop.Model.csproj src/Xtce.Workshop.Model/
COPY src/Xtce.Workshop.Validation/Xtce.Workshop.Validation.csproj src/Xtce.Workshop.Validation/
COPY src/Xtce.Workshop.Api/Xtce.Workshop.Api.csproj src/Xtce.Workshop.Api/
RUN dotnet restore src/Xtce.Workshop.Api/Xtce.Workshop.Api.csproj
COPY src/Xtce.Workshop.Model/ src/Xtce.Workshop.Model/
COPY src/Xtce.Workshop.Validation/ src/Xtce.Workshop.Validation/
COPY src/Xtce.Workshop.Api/ src/Xtce.Workshop.Api/
# Embedded resources for the validator live outside src/.
COPY research/xtce-1.2-triage-log.csv research/
COPY reference/1.2/SpaceSystem.xsd reference/1.2/xml.xsd reference/1.2/
RUN dotnet publish src/Xtce.Workshop.Api/Xtce.Workshop.Api.csproj -c Release -o /app --no-restore

# Version stamp (no git binary in the image): resolve HEAD from the copied .git metadata.
COPY .git /tmp/repo-git
RUN set -e; \
    HEADREF="$(cat /tmp/repo-git/HEAD)"; \
    case "$HEADREF" in \
      ref:*) REF="${HEADREF#ref: }"; \
        if [ -f "/tmp/repo-git/$REF" ]; then SHA="$(cat "/tmp/repo-git/$REF")"; \
        else SHA="$(grep " $REF\$" /tmp/repo-git/packed-refs | head -1 | cut -c1-40)"; fi ;; \
      *) SHA="$HEADREF" ;; \
    esac; \
    printf '%.7s %s' "$SHA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > /app/version.txt; \
    cat /app/version.txt

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS runtime
WORKDIR /app
COPY --from=api-build /app .
COPY --from=web-build /web/dist/xtce-workshop-web/browser ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Xtce.Workshop.Api.dll"]
