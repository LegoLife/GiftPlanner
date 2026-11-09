# syntax=docker/dockerfile:1
# Cross-build for the Pi: docker buildx build --platform linux/arm64 -t giftplanner:latest --load .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY GiftPlanner/GiftPlanner.csproj GiftPlanner/
RUN dotnet restore GiftPlanner/GiftPlanner.csproj -a $TARGETARCH
COPY GiftPlanner/ GiftPlanner/
# No --no-restore: blazor.web.js is only added when the SDK sees the .razor sources.
RUN dotnet publish GiftPlanner/GiftPlanner.csproj -c Release -a $TARGETARCH -o /app \
 && ls /app/wwwroot/_framework/blazor.web*.js > /dev/null \
 && rm -f /app/giftplanner.db*

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown $APP_UID /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    GiftPlanner__DataDirectory=/data
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "GiftPlanner.dll"]
