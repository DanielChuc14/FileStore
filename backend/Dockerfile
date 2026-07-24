# syntax=docker/dockerfile:1

# Multi-stage: la etapa de build usa el SDK completo (pesado, con compilador);
# la imagen final usa solo el runtime de ASP.NET (mas chica, menos superficie de
# ataque). El SDK y el codigo fuente no viajan a produccion.

# --- Etapa de build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Se copian primero solo los .csproj y se restaura: si el codigo cambia pero las
# dependencias no, Docker reutiliza esta capa cacheada y no vuelve a restaurar.
COPY FileStore.Domain/FileStore.Domain.csproj FileStore.Domain/
COPY FileStore.Application/FileStore.Application.csproj FileStore.Application/
COPY FileStore.Infrastructure/FileStore.Infrastructure.csproj FileStore.Infrastructure/
COPY FileStore.API/FileStore.API.csproj FileStore.API/
RUN dotnet restore FileStore.API/FileStore.API.csproj

# Ahora si el resto del codigo, y se publica en modo Release.
COPY . .
RUN dotnet publish FileStore.API/FileStore.API.csproj \
    -c Release \
    -o /app \
    --no-restore

# --- Imagen final ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Se crea /storage con dueño 'app' ANTES de cambiar de usuario. Cuando el
# docker-compose monte un volumen ahi, Docker copia esta propiedad al volumen
# nuevo: sin esto, el volumen seria de root y el proceso (que corre como 'app',
# sin privilegios) no podria escribir los archivos subidos.
RUN mkdir -p /storage && chown app:app /storage

COPY --from=build /app .

# Usuario sin privilegios: el proceso no corre como root, de modo que una
# vulnerabilidad no da control de la maquina. La imagen de .NET ya trae 'app'.
USER app

# El contenedor escucha HTTP en el 8080; el TLS lo termina Nginx por delante.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FileStore.API.dll"]
