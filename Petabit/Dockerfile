FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Petabit/*.csproj ./Petabit/
RUN dotnet restore Petabit/Petabit.csproj

COPY . .
WORKDIR /src/Petabit
RUN dotnet build Petabit.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish Petabit.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Petabit.dll"]

