# Build context: McpServer/
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GoogleWorkspace.McpServer/GoogleWorkspace.McpServer.csproj src/GoogleWorkspace.McpServer/
RUN dotnet restore src/GoogleWorkspace.McpServer/GoogleWorkspace.McpServer.csproj
COPY src/ src/
RUN dotnet publish src/GoogleWorkspace.McpServer/GoogleWorkspace.McpServer.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Token store + Data Protection keys live here (mount a volume); owned by the non-root user.
RUN mkdir /data && chown $APP_UID:$APP_UID /data
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    MCP_TOKEN_STORE_PATH=/data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GoogleWorkspace.McpServer.dll"]
