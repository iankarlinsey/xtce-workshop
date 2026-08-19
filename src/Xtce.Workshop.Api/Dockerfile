# Build context must be the repo root (not this directory) — Xtce.Workshop.Api
# references ../Xtce.Workshop.Model via ProjectReference.
#
# Base images pinned by digest, not floating tag, for reproducible builds — matches
# web/Dockerfile's pinning discipline and the CI job's pinned mcr.microsoft.com/dotnet
# image. Re-pin both stages deliberately when bumping .NET.
FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:306301580fcaa5b445180e759db59309979002d1000669cb4cf58a567d0014bc AS build
WORKDIR /src

COPY src/Xtce.Workshop.Model/Xtce.Workshop.Model.csproj src/Xtce.Workshop.Model/
COPY src/Xtce.Workshop.Api/Xtce.Workshop.Api.csproj src/Xtce.Workshop.Api/
RUN dotnet restore src/Xtce.Workshop.Api/Xtce.Workshop.Api.csproj

COPY src/Xtce.Workshop.Model/ src/Xtce.Workshop.Model/
COPY src/Xtce.Workshop.Api/ src/Xtce.Workshop.Api/
RUN dotnet publish src/Xtce.Workshop.Api/Xtce.Workshop.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:b0beb9cc1dee1c1b0749796110d4734292071b814207ad0d4f40611f7db04f7b AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Xtce.Workshop.Api.dll"]
