FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Petabit/Petabit.csproj", "Petabit/"]
RUN dotnet restore "Petabit/Petabit.csproj"

COPY . .
RUN dotnet publish "Petabit/Petabit.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Petabit.dll"]
