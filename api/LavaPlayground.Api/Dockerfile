FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY LavaPlayground.Api/ LavaPlayground.Api/
RUN dotnet publish LavaPlayground.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5133
EXPOSE 5133
ENTRYPOINT ["dotnet", "LavaPlayground.Api.dll"]
