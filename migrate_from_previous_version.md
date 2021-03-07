Migrating From Previous Version
===============================

Rails To Blazor
---------------

Run this on the server to create `db_backup.txt` file:

```sh
pg_dump -d thrivedevcenter -abOx --no-tablespaces -f db_backup_part_1.txt -T "debug_symbols*" -T "reports*" -T "hyperstack*" -T "ar_*" -T "sessions*" -T "schema*" -T "dehydrated_objects_dev_builds"
pg_dump -d thrivedevcenter -abOx --no-tablespaces -f db_backup_part_2.txt -t "dehydrated_objects_dev_builds"
```

Now some adjustments need to be made, here's an example with sed:
```sh
sed -i 's/COPY public.dev_builds (id, build_hash, platform, branch, storage_item_id, verified, anonymous, description, score, downloads, important, keep, pr_url, pr_fetched, created_at, updated_at, build_zip_hash, build_of_the_day, user_id)/COPY public.dev_builds (id, build_hash, platform, branch, storage_item_id, verified, anonymous, description, score, downloads, important, keep, pr_url, pr_fetched, created_at, updated_at, build_zip_hash, build_of_the_day, verified_by_id)/g' db_backup_part_1.txt
sed -i 's/COPY public.lfs_objects (id, oid, size, storage_path, lfs_project_id, created_at, updated_at)/COPY public.lfs_objects (id, lfs_oid, size, storage_path, lfs_project_id, created_at, updated_at)/g' db_backup_part_1.txt

sed -i -e "s/SELECT pg_catalog.setval('public.dehydrated_objects_id_seq'/--/g" \
-e "s/SELECT pg_catalog.setval('public.lfs_objects_id_seq'/--/g" \
-e "s/SELECT pg_catalog.setval('public.project_git_files_id_seq'/--/g" \
-e "s/SELECT pg_catalog.setval('public.storage_files_id_seq'/--/g" \
-e "s/SELECT pg_catalog.setval('public.storage_item_versions_id_seq'/--/g" \
-e "s/SELECT pg_catalog.setval('public.storage_items_id_seq'/--/g" db_backup_part_1.txt

sed -i 's/COPY public.dehydrated_objects_dev_builds (dehydrated_object_id, dev_build_id)/COPY public.dehydrated_objects_dev_builds (dehydrated_objects_id, dev_builds_id)/g' db_backup_part_2.txt
```




Now on the new server create the `thrivedevcenter` database, then run
(adjust login info as needed):

```sh
psql -d thrivedevcenter -U thrivedevcenter -h 127.0.0.1 --single-transaction < db_backup_part_1.txt
psql -d thrivedevcenter -U thrivedevcenter -h 127.0.0.1 --single-transaction < db_backup_part_2.txt
```

Then run the post import actions (NOTE: that this drops data that
can't be used anymore):
```sh
psql -d thrivedevcenter -U thrivedevcenter -h 127.0.0.1 < post_rails_migration.sql
```
