FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY QuickStart.csproj Program.cs ./
RUN dotnet publish QuickStart.csproj -c Release -o /out --nologo
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/ ./
COPY index.html ./
COPY definition/ definition/
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuickStart.dll"]
