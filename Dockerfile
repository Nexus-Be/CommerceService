FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8003
ENV ASPNETCORE_URLS=http://+:8003

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Commerce.Api/Commerce.Api.csproj", "Commerce.Api/"]
COPY ["Commerce.Application/Commerce.Application.csproj", "Commerce.Application/"]
COPY ["Commerce.Domain/Commerce.Domain.csproj", "Commerce.Domain/"]
COPY ["Commerce.Infrastructure/Commerce.Infrastructure.csproj", "Commerce.Infrastructure/"]
RUN dotnet restore "Commerce.Api/Commerce.Api.csproj"
COPY . .
RUN dotnet build "Commerce.Api/Commerce.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Commerce.Api/Commerce.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Commerce.Api.dll"]
