#!/bin/bash

# Setup resolvers
echo resolver $(awk 'BEGIN{ORS=" "} $1=="nameserver" {print $2}' /etc/resolv.conf) \
     " valid=15s;" > /etc/nginx/resolvers.conf

if [ $? -eq 0 ]; then
    echo Set nginx resolvers
else
    echo Failed to set dns resolvers
    exit 1
fi

exec "$@"
