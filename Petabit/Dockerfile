FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["NuGet.Config", "."]
COPY ["Petabit/Petabit.csproj", "Petabit/"]
RUN dotnet restore "Petabit/Petabit.csproj" --configfile NuGet.Config \
    -p:NuGetAudit=true -p:NuGetAuditMode=all \
    -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905

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
