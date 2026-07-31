FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["version.json", "."]

COPY ["src/RasGate.Web/RasGate.Web.csproj", "src/RasGate.Web/"]
COPY ["src/RasGate.Application/RasGate.Application.csproj", "src/RasGate.Application/"]
COPY ["src/RasGate.Contracts/RasGate.Contracts.csproj", "src/RasGate.Contracts/"]
COPY ["src/RasGate.Infrastructure/RasGate.Infrastructure.csproj", "src/RasGate.Infrastructure/"]

RUN dotnet restore "src/RasGate.Web/RasGate.Web.csproj"

COPY . .

RUN dotnet publish "src/RasGate.Web/RasGate.Web.csproj" \
    --configuration $BUILD_CONFIGURATION \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/logs/requests /app/logs/errors \
    && chown -R app:app /app

USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "RasGate.Web.dll"]
