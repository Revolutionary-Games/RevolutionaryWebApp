# If the image version is updated here also Scripts/ContainerTool.cs needs to be updated
FROM almalinux:9 as builder
ENV DOTNET_VERSION "8.0"

# Update used here to have slightly less outdated system packages
# (this'll only really matter if some of the system libs from this
# build will be used in the final output, which might be the case if
# the dotnet runtime is bundled with the output)
RUN dnf update -y && dnf install -y --setopt=deltarpm=false dotnet-sdk-${DOTNET_VERSION} \
    libatomic glibc-langpack-en && dnf clean all

RUN dotnet workload install wasm-tools

#
# The following is very outdated and likely doesn't work very well!:
#
FROM builder as build

COPY RevolutionaryWebApp.sln /root/build/
# Causes a bunch of extra layers because docker folder copy is terrible
COPY Client/ /root/build/Client
COPY Server/ /root/build/Server
COPY Shared/ /root/build/Shared

WORKDIR /root/build

# Building release binaries
RUN dotnet publish -c Release Client/RevolutionaryWebApp.Client.csproj && \
    dotnet publish -c Release Server/RevolutionaryWebApp.Server.csproj

# Migrations file
RUN dotnet tool install --global dotnet-ef
RUN PATH="$PATH:/root/.dotnet/tools" dotnet ef migrations script --idempotent \
    --project Server/RevolutionaryWebApp.Server.csproj --context ApplicationDbContext \
    -o /migration.sql

FROM rockylinux:9 as proxy
ENV DOTNET_VERSION "7.0"

# Update used here to have slightly less outdated alma packages
RUN dnf update -y && dnf install -y --setopt=deltarpm=false nginx && dnf clean all

COPY --from=builder /root/build/Client/bin/Release/net${DOTNET_VERSION}/publish/wwwroot/ \
    /var/www/html/revolutionarywebapp

COPY docker_nginx.conf /etc/nginx/nginx.conf

COPY docker/nginx_entrypoint.sh /entrypoint.sh

RUN ln -sf /dev/stdout /var/log/nginx/access.log && ln -sf /dev/stderr /var/log/nginx/error.log

ENTRYPOINT ["/entrypoint.sh"]
CMD ["nginx"]

FROM rockylinux:9 as application
ENV DOTNET_VERSION "7.0"

# Update used here to have slightly less outdated rocky packages
RUN dnf update -y && dnf install -y --setopt=deltarpm=false aspnetcore-runtime-${DOTNET_VERSION} postgresql \
    fontconfig-devel && dnf clean all

RUN useradd revolutionarywebapp -m

COPY --from=builder /root/build/Server/bin/Release/net${DOTNET_VERSION}/publish/ \
    /home/revolutionarywebapp

COPY --from=builder /migration.sql /migration.sql

COPY docker_appsettings.Production.json /home/revolutionarywebapp/appsettings.Production.json

COPY docker/entrypoint.sh /entrypoint.sh

WORKDIR /home/revolutionarywebapp
ENTRYPOINT ["/entrypoint.sh"]
CMD ["/home/revolutionarywebapp/RevolutionaryWebApp.Server", "--urls", "http://0.0.0.0:5000"]
