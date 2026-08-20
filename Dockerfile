# Single application image: Kestrel serves both the API and the Angular build.
# Build context is the repo root. Base images pinned by digest for reproducible
# builds — re-pin deliberately when bumping Node/.NET.

FROM node:20-bookworm@sha256:8f693eaa7e0a8e71560c9a82b55fd54c2ae920a2ba5d2cde28bac7d1c01c9ba5 AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx ng build --configuration production

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:306301580fcaa5b445180e759db59309979002d1000669cb4cf58a567d0014bc AS api-build
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

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:b0beb9cc1dee1c1b0749796110d4734292071b814207ad0d4f40611f7db04f7b AS runtime
WORKDIR /app
COPY --from=api-build /app .
COPY --from=web-build /web/dist/xtce-workshop-web/browser ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Xtce.Workshop.Api.dll"]
