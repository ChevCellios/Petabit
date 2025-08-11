# KORAK 1: Build stage s .NET SDK image-om
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Postavi radni direktorij u /src
WORKDIR /src

# Kopiraj samo .csproj fajl iz poddirektorija Petabit/Petabit u lokalni folder Petabit
COPY Petabit/Petabit.csproj Petabit/

# Pokreni restore ovisnosti za projekt (iz Petabit/Petabit foldera)
RUN dotnet restore Petabit/Petabit.csproj

# Kopiraj sav ostatak koda iz poddirektorija Petabit/Petabit u lokalni folder Petabit
COPY Petabit/Petabit/ Petabit/

# Promijeni radni direktorij u Petabit (gdje je csproj)
WORKDIR /src/Petabit

# Build i publish aplikacije u release modu u folder /app
RUN dotnet publish -c Release -o /app

# KORAK 2: Runtime stage s ASP.NET Core runtime image-om
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Postavi radni direktorij u /app
WORKDIR /app

# Kopiraj iz build faze objavljeni output u runtime image
COPY --from=build /app .

# Definiraj komandu za pokretanje aplikacije
ENTRYPOINT ["dotnet", "Petabit.dll"]
