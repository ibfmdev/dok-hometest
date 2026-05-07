# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["src/Dok.FakeProviders/Dok.FakeProviders.csproj", "src/Dok.FakeProviders/"]
RUN dotnet restore "src/Dok.FakeProviders/Dok.FakeProviders.csproj"

COPY src/Dok.FakeProviders/ ./src/Dok.FakeProviders/
RUN dotnet publish "src/Dok.FakeProviders/Dok.FakeProviders.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Dok.FakeProviders.dll"]
