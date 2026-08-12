FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["version.json", "."]
COPY ["RasGate.sln", "."]

COPY ["src/RasGate.Web/RasGate.Web.csproj", "src/RasGate.Web/"]
COPY ["src/RasGate.Core/RasGate.Core.csproj", "src/RasGate.Core/"]
COPY ["src/RasGate.Infrastructure/RasGate.Infrastructure.csproj", "src/RasGate.Infrastructure/"]
COPY ["tests/RasGate.FakeRac/RasGate.FakeRac.csproj", "tests/RasGate.FakeRac/"]
COPY ["tests/RasGate.IntegrationTests/RasGate.IntegrationTests.csproj", "tests/RasGate.IntegrationTests/"]
COPY ["tests/RasGate.UnitTests/RasGate.UnitTests.csproj", "tests/RasGate.UnitTests/"]

RUN dotnet restore "RasGate.sln"

COPY . .

RUN dotnet test "RasGate.sln" \
    --configuration $BUILD_CONFIGURATION \
    --no-restore \
    -m:1

RUN dotnet publish "src/RasGate.Web/RasGate.Web.csproj" \
    --configuration $BUILD_CONFIGURATION \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS="" \
    Urls=http://0.0.0.0:8080

COPY --from=build /app/publish .

RUN mkdir -p /app/logs/requests /app/logs/errors \
    && chown -R app:app /app

USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "RasGate.Web.dll"]
