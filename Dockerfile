# ── Build Stage ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar solución y proyectos
COPY CasaticDirectorio.sln .
COPY src/CasaticDirectorio.Domain/CasaticDirectorio.Domain.csproj src/CasaticDirectorio.Domain/
COPY src/CasaticDirectorio.Infrastructure/CasaticDirectorio.Infrastructure.csproj src/CasaticDirectorio.Infrastructure/
COPY src/CasaticDirectorio.Api/CasaticDirectorio.Api.csproj src/CasaticDirectorio.Api/

RUN dotnet restore

COPY src/ src/
WORKDIR /src/src/CasaticDirectorio.Api
RUN dotnet publish -c Release -o /app/publish

# ── Runtime Stage ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 5000
ENTRYPOINT ["dotnet", "CasaticDirectorio.Api.dll"]
