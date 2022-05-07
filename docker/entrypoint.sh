#!/bin/bash

# Run db migrations
psql -h thrivedevcenter_db -d thrivedevcenter -U postgres < /migration.sql

if [ $? -eq 0 ]; then
    echo Migrations successfully ran
else
    echo Failed to run database migrations
    exit 1
fi

# Then run the command passed in
echo "Starting web server..."
exec "$@"
