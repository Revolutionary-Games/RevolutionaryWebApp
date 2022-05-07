#!/bin/bash
set -e

# Setup DB
echo "Starting services"
su postgres -c "pg_ctl -D /var/lib/pgsql/data/ -l /var/lib/pgsql/log.txt start"
redis-server --daemonize yes

echo "DB setup"
su postgres -c 'psql < /db_setup.sql'

# Then run the command passed in
echo "Starting normal commands..."
exec "$@"
