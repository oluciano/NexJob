FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine@sha256:a3ded33a6488dccb1c990254a0ba23417f7c2be195761a341721ed21de776899 AS build
WORKDIR /src
COPY . .
RUN dotnet publish samples/NexJob.Sample.WorkerService -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine@sha256:628ffa789119d231880f32653b75aca3f528968058fdc3cdfb6969e583f8243a
WORKDIR /app
RUN apk add --no-cache tzdata icu-libs
COPY --chown=$APP_UID:$APP_UID --from=build /app/publish .
USER $APP_UID
ENV PORT=8080
ENV NexJob__Dashboard__LocalhostOnly=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexJob.Sample.WorkerService.dll"]
