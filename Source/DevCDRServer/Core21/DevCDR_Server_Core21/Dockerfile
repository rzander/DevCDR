FROM microsoft/dotnet:2.2-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
ENV ASPNETCORE_ENVIRONMENT Development
ENV AzureAD__TenantId c83db094-b07e-4c8d-9c71-1ff05c4dcef7
ENV AzureAD__Domain rzander.onmicrosoft.com
ENV AzureAD__ClientId ba8fd992-f3ed-4a38-b37f-0b6149bce880
ENV AzureAD__AppIDURL https://rzander.onmicrosoft.com/abf47594-0960-461d-bb5c-5dd9ded06a05
ENV REPORTUSER DEMO	
ENV REPORTPASSWORD iq3ghwegiu2

FROM microsoft/dotnet:2.2-sdk AS build
WORKDIR /src
COPY DevCDR_Server_Core21/DevCDR_Server_Core21.csproj DevCDR_Server_Core21/
RUN dotnet restore DevCDR_Server_Core21/DevCDR_Server_Core21.csproj
COPY . .
WORKDIR /src/DevCDR_Server_Core21
RUN dotnet build DevCDR_Server_Core21.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish DevCDR_Server_Core21.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "DevCDR_Server_Core21.dll"]
