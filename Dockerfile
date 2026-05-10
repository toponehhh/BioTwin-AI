FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/BioTwin_AI/BioTwin_AI.csproj", "src/BioTwin_AI/"]
RUN dotnet restore "src/BioTwin_AI/BioTwin_AI.csproj"
COPY . .
WORKDIR "/src/src/BioTwin_AI"
RUN dotnet build "BioTwin_AI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BioTwin_AI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BioTwin_AI.dll"]
