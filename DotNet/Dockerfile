FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/NSerf.Lighthouse/NSerf.Lighthouse.csproj", "src/NSerf.Lighthouse/"]
RUN dotnet restore "src/NSerf.Lighthouse/NSerf.Lighthouse.csproj"
COPY . .
WORKDIR "/src/src/NSerf.Lighthouse"
RUN dotnet build "NSerf.Lighthouse.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NSerf.Lighthouse.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "NSerf.Lighthouse.dll"]
