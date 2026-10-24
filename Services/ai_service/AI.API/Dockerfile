FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Services/ai_service/ Services/ai_service/
COPY Contracts/Protos/ Contracts/Protos/
RUN dotnet publish Services/ai_service/AI.API/AI.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 5008 5009
ENTRYPOINT ["dotnet", "AI.API.dll"]
