FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine@sha256:3111b113abec80b4a8a3d4b5cebebf61a4cdab019b7e56ca3e6389e6ac4e1c8d AS build
WORKDIR /src
COPY . .
RUN dotnet publish samples/NexJob.Sample.WorkerService -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine@sha256:cf6f271f1c106dcb4dd403e7b3dc03b0c508dabef348c51fd6a709b12cda69b0
WORKDIR /app
RUN apk add --no-cache tzdata icu-libs
COPY --chown=$APP_UID:$APP_UID --from=build /app/publish .
USER $APP_UID
ENV PORT=8080
ENV NexJob__Dashboard__LocalhostOnly=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexJob.Sample.WorkerService.dll"]
