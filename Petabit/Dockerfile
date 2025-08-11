# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Kopiraj csproj i restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Kopiraj ostatak koda i buildaj aplikaciju
COPY . ./
RUN dotnet publish -c Release -o /app/out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Kopiraj buildane datoteke iz build stagea
COPY --from=build /app/out ./

# Izloži port (možeš staviti i environment varijablu)
EXPOSE 5000

# Postavi start naredbu
ENTRYPOINT ["dotnet", "Petabit.dll"]
