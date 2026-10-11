#!/usr/bin/env bash
# Runs once, when the PostgreSQL data volume is empty (feature 002, research R3).
# Creates the application database and a least-privilege application login. Table and schema
# privileges are granted later, with tenancy and migrations (roadmap #3).
set -euo pipefail

: "${HELLOPOS_DB_NAME:?HELLOPOS_DB_NAME is not set}"
: "${HELLOPOS_DB_APP_USER:?HELLOPOS_DB_APP_USER is not set}"
: "${HELLOPOS_DB_APP_PASSWORD:?HELLOPOS_DB_APP_PASSWORD is not set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  --set=db="$HELLOPOS_DB_NAME" \
  --set=admin="$POSTGRES_USER" \
  --set=app_user="$HELLOPOS_DB_APP_USER" \
  --set=app_password="$HELLOPOS_DB_APP_PASSWORD" <<'SQL'
CREATE ROLE :"app_user" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD :'app_password';
CREATE DATABASE :"db" OWNER :"admin";

-- The application login may connect to the application database only.
REVOKE CONNECT, TEMPORARY ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO :"app_user";
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;
SQL

echo "Created database '$HELLOPOS_DB_NAME' and least-privilege login '$HELLOPOS_DB_APP_USER'."
