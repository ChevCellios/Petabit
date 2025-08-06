FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Petabit/*.csproj ./Petabit/
RUN dotnet restore Petabit/Petabit.csproj

COPY . .
WORKDIR /src/Petabit
RUN dotnet build Petabit.csproj -c Release -o /app/build
RUN dotnet publish Petabit.csproj -c Release -o /app/publish /p:SelfContained=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "Petabit.dll"]
