FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ErongoIT.Backup.Api/ErongoIT.Backup.Api.csproj ErongoIT.Backup.Api/
COPY ErongoIT.Backup.Application/ErongoIT.Backup.Application.csproj ErongoIT.Backup.Application/
COPY ErongoIT.Backup.Domain/ErongoIT.Backup.Domain.csproj ErongoIT.Backup.Domain/
COPY ErongoIT.Backup.Infrastructure/ErongoIT.Backup.Infrastructure.csproj ErongoIT.Backup.Infrastructure/

RUN dotnet restore ErongoIT.Backup.Api/ErongoIT.Backup.Api.csproj

COPY . .

RUN dotnet publish ErongoIT.Backup.Api/ErongoIT.Backup.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/storage

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "ErongoIT.Backup.Api.dll"]
