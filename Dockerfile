# SHP-61 This file has been changed during migration
# One need to check if it works correctly

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build

WORKDIR /src
COPY ["SHP.AuthorizationServer.Web/SHP.AuthorizationServer.Web.csproj", "SHP.AuthorizationServer.Web/"]
RUN dotnet restore "SHP.AuthorizationServer.Web\SHP.AuthorizationServer.Web.csproj"
WORKDIR "/src/SHP.AuthorizationServer.Web"
COPY . .
RUN dotnet build "SHP.AuthorizationServer.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SHP.AuthorizationServer.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SHP.AuthorizationServer.Web.dll"]