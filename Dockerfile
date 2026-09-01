# Build ---------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaura antes de copiar o código: enquanto as dependências não mudam, esta
# camada é reaproveitada e o build fica muito mais rápido.
COPY Directory.Build.props ./
COPY src/FinFlower.Domain/FinFlower.Domain.csproj src/FinFlower.Domain/
COPY src/FinFlower.Application/FinFlower.Application.csproj src/FinFlower.Application/
COPY src/FinFlower.Infrastructure/FinFlower.Infrastructure.csproj src/FinFlower.Infrastructure/
COPY src/FinFlower.Api/FinFlower.Api.csproj src/FinFlower.Api/
RUN dotnet restore src/FinFlower.Api/FinFlower.Api.csproj

COPY src/ src/
RUN dotnet publish src/FinFlower.Api/FinFlower.Api.csproj -c Release -o /app --no-restore

# Runtime -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Imagem de runtime, sem SDK nem código-fonte: superfície de ataque menor.
COPY --from=build /app ./

# A imagem base já traz o usuário 'app' sem privilégios; um processo web não
# tem motivo para rodar como root.
USER app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "FinFlower.Api.dll"]
