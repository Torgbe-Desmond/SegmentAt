# ─── Build ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SegmentAtAPI/SegmentAtAPI.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# ─── Runtime ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# PuppeteerSharp launches headless Chromium — install the shared libs it needs.
# Using regex matching targets modern t64 library extensions in .NET 10 environments.
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    fonts-liberation \
    ^libasound2.* \
    ^libatk-bridge2.0-0.* \
    ^libatk1.0-0.* \
    ^libcups2.* \
    libdbus-1-3 \
    libgbm1 \
    ^libgtk-3-0.* \
    libnspr4 \
    libnss3 \
    libx11-xcb1 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    xdg-utils \
    wget \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "SegmentAtAPI.Api.dll"]
