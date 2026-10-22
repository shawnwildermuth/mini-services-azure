# syntax=docker/dockerfile:1
# Build context is this folder:  docker build -t store-web apps/store-web
# One image serves every environment: the API address is read from the container's environment at start-up
# (see docker/10-env-js.sh), not baked in at build time.

# ---- build -----------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ---- runtime ---------------------------------------------------------------
# The unprivileged nginx image runs as a non-root user and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.28-alpine AS runtime

# Security patching. The base image pins nginx (and its optional modules) to the version it was built with, so a plain
# `apk upgrade` would skip it. Nothing here uses the optional modules (GeoIP, image filter, njs, XSLT); dropping them
# also removes the libraries they pull in (libtiff, libpng, libxml2, gd), then nginx is unpinned and everything upgraded.
# Runs on every image build, so each build picks up whatever fixes exist at that moment.
USER root
RUN apk del --no-cache nginx-module-geoip nginx-module-image-filter nginx-module-njs nginx-module-xslt \
 && apk add --no-cache nginx \
 && apk upgrade --no-cache

COPY --from=build --chown=nginx:nginx /app/dist /usr/share/nginx/html
COPY docker/default.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=755 docker/10-env-js.sh /docker-entrypoint.d/10-env-js.sh

# Required at start-up: API_BASE_URL (checked by docker/10-env-js.sh).
EXPOSE 8080

# The base image already runs as uid 101 (nginx); say so explicitly so it can't silently change.
USER 101
