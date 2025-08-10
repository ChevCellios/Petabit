# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Kopiraj samo csproj prvo i restore da iskoristi cache
COPY ["Petabit/Petabit.csproj", "Petabit/"]
RUN dotnet restore "Petabit/Petabit.csproj"

# Kopiraj ostatak koda
COPY . .

WORKDIR "/src/Petabit"
RUN dotnet publish "Petabit.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Petabit.dll"]
