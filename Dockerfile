# ==========================================
# 1. Build SvelteKit Frontend
# ==========================================
FROM node:22-alpine AS frontend-builder
WORKDIR /app/frontend

COPY frontend/package*.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# ==========================================
# 2. Build .NET 10 Backend
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-builder
WORKDIR /src

COPY backend/TradeMASter.Core/TradeMASter.Core.csproj backend/TradeMASter.Core/
COPY backend/TradeMASter.Infrastructure/TradeMASter.Infrastructure.csproj backend/TradeMASter.Infrastructure/
COPY backend/TradeMASter.Agents/TradeMASter.Agents.csproj backend/TradeMASter.Agents/
COPY backend/TradeMASter.Api/TradeMASter.Api.csproj backend/TradeMASter.Api/
COPY backend/TradeMASter.Tests/TradeMASter.Tests.csproj backend/TradeMASter.Tests/
COPY backend/TradeMASter.slnx backend/

RUN dotnet restore backend/TradeMASter.slnx

COPY backend/ ./backend/
WORKDIR /src/backend/TradeMASter.Api
RUN dotnet publish TradeMASter.Api.csproj -c Release -o /app/publish

# ==========================================
# 3. Final Production Runtime Image
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5126
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy backend published binaries
COPY --from=backend-builder /app/publish .

# Copy frontend static build assets into wwwroot
COPY --from=frontend-builder /app/frontend/build ./wwwroot

EXPOSE 5126

ENTRYPOINT ["dotnet", "TradeMASter.Api.dll"]
