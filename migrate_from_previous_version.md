Migrating From Previous Version
===============================

Rails To Blazor
---------------

Run this on the server to create `db_backup.txt` file:

```sh
pg_dump -d thrivedevcenter -b -f db_backup.txt -T "debug_symbols*" -T "reports*" -T "hyperstack*" -T "ar_*" -T "sessions*" -T "schema*"
```

Copy this to the new server / nuke the DB on the current server.

Now on the new server create the `thrivedevcenter` database, then run
(adjust login info as needed):

```sh
psql -d thrivedevcenter -U thrivedevcenter -h 127.0.0.1 < db_backup.txt
```

Then run the post import actions (NOTE: that this drops data that
can't be used anymore):
```sh
psql -d thrivedevcenter -U thrivedevcenter -h 127.0.0.1 < post_rails_migration.sql
```
