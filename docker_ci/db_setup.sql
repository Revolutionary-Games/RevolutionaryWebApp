ALTER database template1 is_template=false;

DROP database template1;

CREATE DATABASE template1
WITH OWNER = postgres
   ENCODING = 'UTF8'
   TABLESPACE = pg_default
   LC_COLLATE = 'en_GB.UTF8'
   LC_CTYPE = 'en_GB.UTF8'
   CONNECTION LIMIT = -1
   TEMPLATE template0;

ALTER database template1 is_template=true;

DO
$do$
BEGIN
   IF EXISTS (
      SELECT FROM pg_catalog.pg_roles
      WHERE rolname = 'devcenter') THEN

      RAISE NOTICE 'Role "devcenter" already exists. Skipping.';
   ELSE
      CREATE ROLE devcenter LOGIN SUPERUSER PASSWORD 'testing';
   END IF;
END
$do$;

