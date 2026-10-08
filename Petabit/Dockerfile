FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Petabit/Petabit.csproj", "Petabit/"]
RUN dotnet restore "Petabit/Petabit.csproj"

COPY . .
RUN dotnet publish "Petabit/Petabit.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
COPY Petabit/entrypoint.sh /app/entrypoint.sh

RUN mkdir -p /app/App_Data && chown "$APP_UID" /app/App_Data \
    && sed -i 's/\r$//' /app/entrypoint.sh && chmod 755 /app/entrypoint.sh

USER $APP_UID
ENTRYPOINT ["/app/entrypoint.sh"]
