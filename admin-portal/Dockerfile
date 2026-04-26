# ── Build stage ───────────────────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Branding build args — passed from docker-compose via build.args
ARG VITE_APP_NAME="Diva AI"
ARG VITE_APP_SLUG="diva"

# Empty VITE_API_URL so all API calls are relative — nginx proxies them to diva-api
RUN VITE_API_URL="" \
    VITE_APP_NAME="${VITE_APP_NAME}" \
    VITE_APP_SLUG="${VITE_APP_SLUG}" \
    npm run build

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM nginx:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 6010
