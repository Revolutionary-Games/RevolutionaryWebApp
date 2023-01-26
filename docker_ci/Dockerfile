FROM fedora:37
ENV DOTNET_VERSION "7.0"

RUN rpm --import https://packages.microsoft.com/keys/microsoft.asc

# Powershell needs to be installed separately to not mess with the normal sdk install
# Chromium is installed as a handy way to get all the dependencies Playwright browsers need
RUN dnf install -y --setopt=deltarpm=false dotnet-sdk-${DOTNET_VERSION} chromium \
    postgresql-server redis git ruby-devel \
    glibc-locale-source glibc-langpack-en langpacks-en && \
    curl https://packages.microsoft.com/config/rhel/7/prod.repo | \
    tee /etc/yum.repos.d/microsoft.repo && dnf install -y powershell && dnf clean all

# This seems to fail anyway for some reason
RUN LANG=en_GB.UTF8 localedef --verbose --force -i en_GB -f UTF-8 en_GB.UTF-8 || true

RUN PGDATA=/var/lib/pgsql/data/ su postgres -c "pg_ctl initdb -locale=en_GB.UTF-8"
COPY pg_hba.conf /var/lib/pgsql/data/pg_hba.conf
RUN chown postgres:postgres /var/lib/pgsql/data/pg_hba.conf

COPY db_setup.sql /db_setup.sql
COPY entrypoint.sh /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
