# syntax=docker/dockerfile:1

# Multi-stage: se compila el SPA con Node y la imagen final es solo Nginx
# sirviendo archivos estaticos. Node no viaja a produccion.

# --- Etapa de build ---
FROM node:22-alpine AS build
WORKDIR /app

# package.json y lock primero para cachear la instalacion de dependencias.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Build de produccion: optimizado, con hashing de archivos para cache-busting.
RUN npm run build

# --- Imagen final ---
FROM nginx:1.27-alpine AS final

# El SPA se sirve desde el directorio browser del build de Angular.
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

# Config propia: enruta /api al backend y hace fallback al index.html para que
# las rutas del SPA funcionen al recargar.
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
