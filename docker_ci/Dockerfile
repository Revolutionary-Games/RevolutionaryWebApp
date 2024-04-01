# This is fedora-based image as that makes using playwright much simpler
FROM fedora:39
ENV DOTNET_VERSION "8.0"

RUN rpm --import https://packages.microsoft.com/keys/microsoft.asc

# Chromium is installed as a handy way to get all the dependencies Playwright browsers need
RUN dnf install -y --setopt=deltarpm=false chromium \
    postgresql-server redis git ruby-devel \
    glibc-locale-source glibc-langpack-en langpacks-en && \
    curl https://packages.microsoft.com/config/rhel/7/prod.repo | \
    tee /etc/yum.repos.d/microsoft.repo && dnf install -y powershell && dnf clean all
    # dotnet-sdk-${DOTNET_VERSION} 

# Install latest dotnet (not using Fedora repos as it takes a while
# after a new release to be updated)
RUN curl -L https://dot.net/v1/dotnet-install.sh -o /dotnet-install.sh && chmod +x /dotnet-install.sh
RUN /dotnet-install.sh --channel $DOTNET_VERSION
RUN /dotnet-install.sh --channel $DOTNET_VERSION --runtime aspnetcore

# This seems to fail anyway for some reason
RUN LANG=en_GB.UTF8 localedef --verbose --force -i en_GB -f UTF-8 en_GB.UTF-8 || true

RUN PGDATA=/var/lib/pgsql/data/ su postgres -c "pg_ctl initdb -locale=en_GB.UTF-8"
COPY pg_hba.conf /var/lib/pgsql/data/pg_hba.conf
RUN chown postgres:postgres /var/lib/pgsql/data/pg_hba.conf

COPY db_setup.sql /db_setup.sql
COPY entrypoint.sh /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
