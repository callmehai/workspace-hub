# Multi-stage build cho WorkspaceHub.Api (.NET 8). Context build = thư mục backend/.
#   docker build -t wh-api -f backend/Dockerfile backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj trước để tận dụng cache layer restore (chỉ restore lại khi csproj đổi).
COPY src/WorkspaceHub.Domain/*.csproj         src/WorkspaceHub.Domain/
COPY src/WorkspaceHub.Application/*.csproj     src/WorkspaceHub.Application/
COPY src/WorkspaceHub.Infrastructure/*.csproj  src/WorkspaceHub.Infrastructure/
COPY src/WorkspaceHub.Api/*.csproj             src/WorkspaceHub.Api/
COPY global.json ./
RUN dotnet restore src/WorkspaceHub.Api/WorkspaceHub.Api.csproj

# Copy phần còn lại rồi publish.
COPY src/ src/
RUN dotnet publish src/WorkspaceHub.Api/WorkspaceHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Container chỉ nghe HTTP nội bộ; TLS do Caddy (reverse proxy) đảm nhiệm.
# Không có HTTPS port ⇒ app.UseHttpsRedirection() thành no-op (không loop redirect).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorkspaceHub.Api.dll"]
