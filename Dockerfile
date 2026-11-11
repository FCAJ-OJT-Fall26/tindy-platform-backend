FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Tindy.Api/Tindy.Api.csproj src/Tindy.Api/
COPY src/Tindy.Application/Tindy.Application.csproj src/Tindy.Application/
COPY src/Tindy.Infrastructure/Tindy.Infrastructure.csproj src/Tindy.Infrastructure/
RUN dotnet restore src/Tindy.Api/Tindy.Api.csproj

COPY src/ src/
RUN dotnet publish src/Tindy.Api/Tindy.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /var/lib/tindy/media && chown app:app /var/lib/tindy/media
ENV ASPNETCORE_HTTP_PORTS=8080 \
    MediaStorage__Local__RootPath=/var/lib/tindy/media

COPY --from=build /out/ ./
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Tindy.Api.dll"]