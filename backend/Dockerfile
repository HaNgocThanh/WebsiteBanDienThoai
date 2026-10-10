FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tooling
RUN dotnet tool install --tool-path /tools dotnet-ef --version 10.0.12
ENV PATH="/tools:${PATH}"
WORKDIR /src/PhoneStore.Api
# Each one-off container gets fresh bin/obj volumes. Restore before EF reads metadata.
ENTRYPOINT ["sh", "-c", "dotnet restore PhoneStore.Api.csproj && exec dotnet-ef \"$@\"", "--"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PhoneStore.Api/*.csproj PhoneStore.Api/
RUN dotnet restore PhoneStore.Api/PhoneStore.Api.csproj
COPY . .
RUN dotnet publish PhoneStore.Api/PhoneStore.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys /app/.local-mailbox \
    && chown -R app:app /home/app/.aspnet /app/.local-mailbox
ENV HOME=/home/app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "PhoneStore.Api.dll"]
