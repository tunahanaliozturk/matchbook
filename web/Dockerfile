# syntax=docker/dockerfile:1

# The console, built once and served as static files by nginx, which also proxies /api to the gateway so the browser
# talks to one origin. Base images pinned by digest as well as tag, as the services' are.
FROM node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS build
WORKDIR /source

# The lockfile first, so the install layer is reused whenever only source has changed.
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .
RUN npm run build && node scripts/bundle-budget.mjs

FROM nginx:1.30-alpine@sha256:985220252f3863977e468f611ef118ebd01421289dd86ee1ae99cb068c3bce2b AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.conf /etc/nginx/security-headers.conf
COPY --from=build /source/dist /usr/share/nginx/html

EXPOSE 8080
