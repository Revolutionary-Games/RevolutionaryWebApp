Docs
====

This folder contains example systemd services for running this app in production.

Also an example logrotate settings file.

No downtime restart
-------------------

Note: this is currently broken, likely some incompatibility issue with hyperstack

When using the default puma config it can be gracefully made to cycle the workers by sendings SIGUSR1.

With the systemd files such a restart can be done with:
```sh
systemctl kill -s SIGUSR1 thrivedevcenter-web
systemctl restart thrivedevcenter-sidekiq
```
