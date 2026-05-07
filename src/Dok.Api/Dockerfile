# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore (cache friendly)
COPY ["Directory.Build.props", "./"]
COPY ["src/Dok.Api/Dok.Api.csproj",                       "src/Dok.Api/"]
COPY ["src/Dok.Application/Dok.Application.csproj",       "src/Dok.Application/"]
COPY ["src/Dok.Domain/Dok.Domain.csproj",                 "src/Dok.Domain/"]
COPY ["src/Dok.Infrastructure/Dok.Infrastructure.csproj", "src/Dok.Infrastructure/"]
RUN dotnet restore "src/Dok.Api/Dok.Api.csproj"

# build + publish
COPY src/ ./src/
RUN dotnet publish "src/Dok.Api/Dok.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Dok.Api.dll"]
