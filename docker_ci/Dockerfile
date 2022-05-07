FROM fedora:35
ENV DOTNET_VERSION "6.0"

RUN dnf install -y --setopt=deltarpm=false dotnet-sdk-${DOTNET_VERSION} ruby chromium \
    postgresql-server postgresql-contrib redis git \
    glibc-locale-source glibc-langpack-en langpacks-en && dnf clean all

# This seems to fail anyway for some reason
RUN LANG=en_GB.UTF8 localedef --verbose --force -i en_GB -f UTF-8 en_GB.UTF-8 || true

RUN PGDATA=/var/lib/pgsql/data/ su postgres -c "pg_ctl initdb -locale=en_GB.UTF-8"
COPY pg_hba.conf /var/lib/pgsql/data/pg_hba.conf
RUN chown postgres:postgres /var/lib/pgsql/data/pg_hba.conf

COPY db_setup.sql /db_setup.sql
COPY entrypoint.sh /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
