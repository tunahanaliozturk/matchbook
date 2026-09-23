# syntax=docker/dockerfile:1

# One image definition for every host. PROJECT is the Api (or gateway) project to publish and ASSEMBLY its
# entry assembly, both passed by compose.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /source

# The .editorconfig comes along because analyzer severities live in it and the build treats warnings as errors,
# so leaving it out makes the image build fail where a local build passes.
COPY global.json .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/ src/

# The package cache is a build cache mount, so the five service images restore from one shared download
# instead of five.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "${PROJECT}" --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
ARG ASSEMBLY
ENV ASSEMBLY=${ASSEMBLY}
WORKDIR /app

# The runtime image has neither curl nor wget, so the health check is a short bash script that speaks HTTP
# over /dev/tcp rather than a network client installed for one request.
COPY --chmod=755 deploy/docker/healthcheck.sh /usr/local/bin/healthcheck

# The image ships a non-root user. The only reason services still run as root is that nobody changed it.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet \"${ASSEMBLY}.dll\""]
