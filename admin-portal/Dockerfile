# ── Build stage ───────────────────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# VITE_API_URL is empty so all API calls are relative — nginx proxies them to diva-api.
# VITE_AUTH_ENABLED must be baked in at build time (Vite strips import.meta.env at bundle time).
ARG VITE_AUTH_ENABLED=false
RUN VITE_API_URL="" VITE_AUTH_ENABLED=${VITE_AUTH_ENABLED} npm run build

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM nginx:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
