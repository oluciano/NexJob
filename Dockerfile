# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish samples/NexJob.Sample.WorkerService/NexJob.Sample.WorkerService.csproj -c Release -o /app/publish -p:RunAnalyzers=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
RUN apk add --no-cache tzdata icu-libs
WORKDIR /app
COPY --from=build /app/publish .

ENV PORT=8080
ENV NexJob__Dashboard__LocalhostOnly=false
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

ENTRYPOINT ["dotnet", "NexJob.Sample.WorkerService.dll"]
