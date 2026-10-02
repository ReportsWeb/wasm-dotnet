FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /work
COPY SampleApp/ SampleApp/
RUN dotnet publish SampleApp/SampleApp.csproj -c Release -o /out --nologo
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /opt/reports/sample
COPY --from=build /out/ ./
COPY resources/ /opt/reports/resources/
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 REPORTS_RESOURCE_ROOT=/opt/reports/resources REPORTS_WEB_ROOT=/opt/reports/web
EXPOSE 8080
ENTRYPOINT ["dotnet", "SampleApp.dll"]
