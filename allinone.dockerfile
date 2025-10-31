# Stage 1: Build backend
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy all project files (only .csproj first for caching)
COPY WsAllInOne/WsAllInOne.csproj ./WsAllInOne/
COPY WsTuneCommon/WsTuneCommon.csproj ./WsTuneCommon/
COPY WsTuneCli.Host/WsTuneCli.Host.csproj ./WsTuneCli.Host/
COPY WsTuneCli.Listener/WsTuneCli.Listener.csproj ./WsTuneCli.Listener/
COPY WsTuneCli.Server/WsTuneCli.Server.csproj ./WsTuneCli.Server/
COPY WsTune.SignalR.Extensions/WsTune.SignalR.Extensions.csproj ./WsTune.SignalR.Extensions/

# Restore dependencies
RUN dotnet restore ./WsAllInOne/WsAllInOne.csproj

# Copy the full source
COPY WsAllInOne ./WsAllInOne
COPY WsTuneCommon ./WsTuneCommon
COPY WsTuneCli.Host ./WsTuneCli.Host
COPY WsTuneCli.Listener ./WsTuneCli.Listener
COPY WsTuneCli.Server ./WsTuneCli.Server
COPY WsTune.SignalR.Extensions ./WsTune.SignalR.Extensions


# Build and publish backend
RUN dotnet publish ./WsAllInOne/WsAllInOne.csproj \
    -c Release \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:EnableCompressionInSingleFile=true \
    -o /app/publish



# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

COPY --from=build /app/publish ./
# if there is front end
# COPY --from=frontend /frontend/dist ./wwwroot

EXPOSE 8080
ENV ASPNETCORE_URLS="http://+:8080"
RUN chmod +x WsAllInOne
CMD ["./WsAllInOne"]


