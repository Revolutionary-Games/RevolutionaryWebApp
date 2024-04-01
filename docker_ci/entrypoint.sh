#!/bin/bash
set -e

# Bring dotnet to PATH (needed when installed with script)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools

# Setup DB
echo "Starting services"

# Make sure lock file folder exists to prevent start failure
mkdir -p /var/run/postgresql/
chown postgres:postgres /var/run/postgresql
su postgres -c "pg_ctl -D /var/lib/pgsql/data/ -l /var/lib/pgsql/log.txt start" || \
    (cat /var/lib/pgsql/log.txt && exit 1)

redis-server --daemonize yes

echo "DB setup"
su postgres -c 'psql < /db_setup.sql'

# Then run the command passed in
echo "Starting normal commands..."
exec "$@"
