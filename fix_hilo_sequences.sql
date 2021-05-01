-- Fixes all HiLo sequences in case they are out of order


-- dehydrated_objects
BEGIN;
LOCK TABLE dehydrated_objects IN EXCLUSIVE MODE;
SELECT setval('dehydrated_objects_hilo', (SELECT GREATEST(MAX(id) + 1, nextval('dehydrated_objects_hilo')) - 1 FROM dehydrated_objects));
COMMIT;
-- lfs_objects
BEGIN;
LOCK TABLE lfs_objects IN EXCLUSIVE MODE;
SELECT setval('lfs_objects_hilo', (SELECT GREATEST(MAX(id) + 1, nextval('lfs_objects_hilo')) - 1 FROM lfs_objects));
COMMIT;
-- project_git_files
BEGIN;
LOCK TABLE project_git_files IN EXCLUSIVE MODE;
SELECT setval('project_git_files_hilo', (SELECT GREATEST(MAX(id) + 1, nextval('project_git_files_hilo')) - 1 FROM project_git_files));
COMMIT;
-- storage_files
BEGIN;
LOCK TABLE storage_files IN EXCLUSIVE MODE;
SELECT setval('storage_files_hilo', (SELECT GREATEST(MAX(id) + 1, nextval('storage_files_hilo')) - 1 FROM storage_files));
COMMIT;
-- storage_item_versions
BEGIN;
LOCK TABLE storage_item_versions IN EXCLUSIVE MODE;
SELECT setval('storage_item_versions_hilo', (SELECT GREATEST(MAX(id) + 1, nextval('storage_item_versions_hilo')) - 1 FROM storage_item_versions));
COMMIT;
-- storage_items
BEGIN;
LOCK TABLE storage_items IN EXCLUSIVE MODE;
SELECT setval('storage_items_hilo', (SELECT GREATEST(MAX(id) + 1, nextval('storage_items_hilo')) - 1 FROM storage_items));
COMMIT;
