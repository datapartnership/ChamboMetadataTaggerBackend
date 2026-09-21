FROM artifactory.worldbank.org/itsdo-dec-lrll-docker-virtual/dotnet/sdk:8.0-jammy AS build
WORKDIR /src
COPY . .
RUN dotnet publish MetadataTagging.csproj -c Release -o /out

# ---------- Runtime stage ----------
FROM artifactory.worldbank.org/itsdo-dec-lrll-docker-virtual/dotnet/aspnet:8.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:80
EXPOSE 80
COPY --from=build /out .
ENTRYPOINT ["dotnet", "MetadataTagging.dll"]