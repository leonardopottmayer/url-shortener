# syntax=docker/dockerfile:1
# Frontend: build the Vite SPA, then serve it with nginx. Build context is this
# folder (src/Pottmayer.UrlShortener.Web).

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
