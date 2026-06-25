FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/api-dotnet/EnterpriseDocumentAssistant.Api.csproj ./api-dotnet/
RUN dotnet restore ./api-dotnet/EnterpriseDocumentAssistant.Api.csproj

COPY src/api-dotnet/ ./api-dotnet/
RUN dotnet publish ./api-dotnet/EnterpriseDocumentAssistant.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "EnterpriseDocumentAssistant.Api.dll"]
