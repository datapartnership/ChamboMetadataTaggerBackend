# ---------- Build stage ----------
FROM artifactory.worldbank.org/itsdt-docker-virtual/dotnet/sdk:10.0.200 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MetadataTagging.csproj -c Release -o /out

# ---------- Runtime stage ----------
FROM artifactory.worldbank.org/itsdt-docker-virtual/dotnet/aspnet:8.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /out .
ENTRYPOINT ["dotnet", "MetadataTagging.dll"]