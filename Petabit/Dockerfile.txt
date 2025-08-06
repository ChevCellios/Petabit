# 1. Build faza
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Kopiraj csproj i restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Kopiraj sve ostalo i buildaj
COPY . ./
RUN dotnet publish -c Release -o /out

# 2. Runtime faza
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /out ./

# Port koji Render prosljeđuje u varijabli PORT
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT}

# Pokreni aplikaciju
ENTRYPOINT ["dotnet", "Petabit.dll"]
