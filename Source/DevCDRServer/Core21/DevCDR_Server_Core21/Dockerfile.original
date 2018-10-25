FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM microsoft/dotnet:2.1-aspnetcore-runtime AS build
WORKDIR /src
COPY ["DevCDR_Server_Core21/DevCDR_Server_Core21.csproj", "DevCDR_Server_Core21/"]
RUN dotnet restore "DevCDR_Server_Core21/DevCDR_Server_Core21.csproj"
COPY . .
WORKDIR "/src/DevCDR_Server_Core21"
RUN dotnet build "DevCDR_Server_Core21.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "DevCDR_Server_Core21.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "DevCDR_Server_Core21.dll"]