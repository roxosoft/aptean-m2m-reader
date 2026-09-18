# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["aptean-m2m-reader.sln", "./"]
COPY ["src/ApteanM2MReader/JetSolutions.ApteanM2MReader.csproj", "src/ApteanM2MReader/"]
COPY ["src/BillVendorSync/JetSolutions.BillVendorSync.csproj", "src/BillVendorSync/"]

RUN dotnet restore src/ApteanM2MReader/JetSolutions.ApteanM2MReader.csproj

COPY . .
RUN dotnet publish src/ApteanM2MReader/JetSolutions.ApteanM2MReader.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "JetSolutions.ApteanM2MReader.dll"]
