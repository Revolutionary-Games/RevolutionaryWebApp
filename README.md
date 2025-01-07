# README

This is a web app that has various features to help in the development of Thrive.

## System dependencies

### Database setup

RevolutionaryWebApp requires a PostgreSQL database to operate.

You can create a new account and a database for the account with `psql`:
```sql
CREATE USER revolutionarywebapp WITH LOGIN PASSWORD 'PUTAPASSWORDHERE';
```

### Redis (compatible)

Redis is basically required to run the app. A Redis alternative like
KeyDB is recommended.

The connection string to redis is configured in the app
configuration. It's recommended to have a password on the redis.

### Remote S3 Storage

Various features require (but these features are optional so you can
setup a site without this but many features will be unavailable) an S3
storage bucket. It's recommended to have a separate LFS and general
file storage buckets.

The buckets need to specify their access details, and the keys for the
buckets need the following kind of permissions:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:Get*",
        "s3:List*",
        "s3:Put*",
        "s3:Delete*",
        "s3:AbortMultipartUpload"
      ],
      "Resource": [
        "arn:aws:s3:::bucket-name/*"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:ListMultipartUploadParts",
        "s3:ListBucketMultipartUploads",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::bucket-name"
      ]
    }
  ]
}
```

Note that LFS bucket doesn't require the multipart permissions as that
is not used there.

For providing downloads a CI system from bunny.net is used. To
configure that you should add separate access key with only the `Get`
and `List` actions allowed on it.

## Site configuration

To configure the site in production you should use environment
variables with double underscores, for example: `Tasks__ThreadCount=1`

One of the most important things is to define the `BaseUrl` with a
trailing slash. For development environment this is already included
in the template.

To setup local running in development mode, copy the
`appsettings.Development.json.template` file without the `.template`
suffix and edit with your database details and potentially other
tweaks.

Now you can use the dotnet tool to setup the local database (run in the Server folder):
```sh
dotnet ef database update --context ApplicationDbContext
```

Note that this repo contains a helper tool that allows more easily
running the `ef` tool:

```sh
dotnet run --project Scripts -- ef -m
```

There's also an option to install or update the `ef` tool to the right
version.

After that running in development should work if you execute
the following in the Server folder:
```sh
dotnet watch run
```

Then the development site should be available at http://localhost:5000
now.

A data protection certificate is required, it can be generated with:
```sh
openssl req -x509 -newkey ed25519 -keyout key.pem -out cert.pem -sha256 -days 3650 -nodes
```

Note that some environments don't have working ed25519 keys, so for
those the above command will need to have `rsa:4096` substituted as
the key type.

## Running

When cloning this repository, you need to clone
recursively. Alternatively (and also when submodules are updated) you
need to run:

```sh
git submodule update --init --recursive
```

The server computer needs to the following packages
aspnetcore-runtime-7.0 (at the time of writing), nginx, and git
installed. Or you can alternatively have a different proxy
server than nginx. Additionally of course the database and redis can be ran on
the same server.

There are example template nginx and systemd files in the `templates`
folder. After copying and modifying them (remember to also setup the
devcenter linux local account and adjust the systemd service
environment variable files) you can enable and start the service
`systemctl enable --now revolutionarywebapp` (might need to do a daemon
reload first). Then check that the service is up and running
`systemctl status revolutionarywebapp`. After it is up, you should verify
nginx config and reload it.

Before starting the server you need to migrate the database. To do
this use the dotnet entity framework tool to update the localhost
server, or see how the deploy script generates an sql script and then
executes it on a remote server. There's an utility script provided
that can do it automatically:

```sh
dotnet run --project Scripts -- ef -m
```

### Getting an admin account

To get the first admin account, first setup the DB then run this SQL
on it (replace the id with a uuid):
```sql
INSERT INTO redeemable_codes (id, hashed_id, granted_resource) VALUES ('UUID_GOES_HERE', 'HASH', 'GroupAdmin');
```

The `HASH` value needs to be replaced by the sha256 hash of the `id`
this is done to protect against timing attacks. To compute that for
example on Linux, you can use:
```sh
echo -n VALUEHERE | openssl dgst -binary -sha256 | openssl base64
```

Then you can redeem the code on your user profile after logging in to become an admin.

### CI executor

Note that when running locally (and not with the deploy script) the
CIExecutor executable is not automatically moved to the webroot,
meaning that running CI jobs on controlled servers is not possible
without a little bit of manual work.

### Stackwalk

The DevCenter uses a stackwalk service in order to decode crash dump files. This
is accessed through HTTP. You can use for example 
https://github.com/hhyyrylainen/StackWalkAsAService running on the local host.
You can find instructions in that repo on how to run the service. Note that
there needs to be a folder that both the service container and RevolutionaryWebApp
can access to have correct symbol files.

This can be setup for example with:
```sh
useradd stackwalk -s /sbin/nologin
mkdir /var/lib/revolutionarywebapp/
mkdir /var/lib/revolutionarywebapp/symbols
mkdir /var/lib/revolutionarywebapp/symbols/production
chown root:revolutionarywebapp /var/lib/revolutionarywebapp/symbols/production
chmod g+w /var/lib/revolutionarywebapp/symbols/production
```

Then the stackwalk service configured to have 
`/var/lib/revolutionarywebapp/symbols/production` as the symbols folder mounted
inside the container. The container needs to also have the same port configured
as in the following RevolutionaryWebApp configuration:
```
Crashes__Enabled=true
Crashes__StackwalkService=http://localhost:3115
Crashes__StackwalkSymbolFolder=/var/lib/revolutionarywebapp/symbols/production
```

Refer to the repository linked above for full instructions but the following
will get a running stackwalk service going:
```sh
podman run -d --rm -p 127.0.0.1:3115:9090 --mount type=bind,src=/var/lib/revolutionarywebapp/symbols/production,destination=/Symbols,ro=true,relabel=shared --name stackwalkweb hhyyrylainen/stackwalk:latest --http-port 9090
podman generate systemd --new --name stackwalkweb > /etc/systemd/system/stackwalkweb.service
# Add `User=stackwalk` to the service section and replace `%t/` with `/home/stackwalk/`
emacs /etc/systemd/system/stackwalkweb.service
systemctl daemon-reload
su - stackwalk -s /usr/bin/bash
podman run --rm --name stackwalkweb hhyyrylainen/stackwalk:latest
exit
systemctl enable --now stackwalkweb
```

Note that running as root is not recommended.

## Testing

To test the project (run the xunit tests), you need to have some extra
services setup for integration tests to work.

### Test database

Testing requires separate databases. See the database setup part for
how to setup new accounts. In addition to the other permissions the
account needs to be able to create databases:

```sql
ALTER USER revolutionarywebapp_test CREATEDB;
```

Note that while you can use a single user for testing, the unittest
and test databases need to be separate.

Now set the secrets by running in the Server.Tests folder:
```sh
dotnet user-secrets set UnitTestConnection 'User ID=revolutionarywebapp_test;Password=PASSWORDHERE;Server=localhost;Port=5432;Database=revolutionarywebapp_unittest;Pooling=true;'
```

And in the AutomatedUITests folder:
```sh
dotnet user-secrets set IntegrationTestConnection 'User ID=revolutionarywebapp_test;Password=PASSWORDHERE;Server=localhost;Port=5432;Database=revolutionarywebapp_test;Pooling=true;'
```

### Test browsers

After building the solutions, you need to install the needed playwrigth browsers using:
```
pwsh AutomatedUITests/bin/Debug/net8.0/playwright.ps1 install
```

### Running the tests

Most tests can be ran like normal.

### Local mail server

You can use mailcatcher to work as a local test email server.

## Running with Docker

The app can be ran entirely in docker instead of a system
install. With this you just need docker to build the images with and a
server running docker where you can deploy the images as running
containers.

Before building the images you need to copy the template files for
docker usage from the `templates` folder. Remove the `.template`
suffix after copying and then edit the values to be suitable for your
setup.

```sh
docker build . --target proxy --tag revolutionarywebapp-proxy:latest
docker build . --target application --tag revolutionarywebapp-web:latest

docker network create revolutionarywebapp
docker volume create pgdata
docker run -d -v pgdata:/var/lib/postgresql/data --rm --restart on-failure --name revolutionarywebapp_db --network=revolutionarywebapp -e POSTGRES_PASSWORD=SPECIFYAPASSWORD -e POSTGRES_DB=revolutionarywebapp postgres:13
docker volume create redis_data
docker run -d -v redis_data:/data --rm --restart on-failure --name revolutionarywebapp_redis --network=revolutionarywebapp redis:6 redis-server --appendonly yes
docker run -d --rm --restart on-failure --name revolutionarywebapp_web --network=revolutionarywebapp -e PGPASSWORD=SPECIFYAPASSWORD -e ASPNETCORE_ENVIRONMENT=Production revolutionarywebapp-web:latest
docker run -d -p 80:80 --rm --restart on-failure --name revolutionarywebapp_proxy --network=revolutionarywebapp revolutionarywebapp-proxy:latest
```

Creating an admin redeem code:
```sh
docker exec -it revolutionarywebapp_db psql -U postgres -d revolutionarywebapp
```

Provide the password used in the previous step if prompted.

And now follow the instructions in the "Getting an admin account"
section.

## Code checks

Code checks can be ran with:
```sh
dotnet run --project Scripts -- check
```

### CI container

To build a CI container image for running tests in it, use the
following command (4 is the version number):
```sh
dotnet run --project Scripts -- container 4
```

## Deploying

First prepare the server to deploy with all the software.

For Rocky linux / Fedora you can install things with:
```sh
dnf install aspnetcore-runtime-7.0 git postgresql-server keydb nginx rsync cronie dnf-automatic emacs-nox certbot-nginx tmux wget fontconfig-devel
```
Note that some packages are optional but better for a full production setup.

And then configure them.
Finally edit the remote hosts configuration of the deploy script and run it.

Additional recommended server setup:

```sh
useradd -s /usr/bin/false revolutionarywebapp
systemctl enable firewalld --now
firewall-cmd --add-service http
firewall-cmd --add-service https
firewall-cmd --permanent --add-service http
firewall-cmd --permanent --add-service https
```

Edit auto config to have just security upgrades (and enable it):
```sh
emacs /etc/dnf/automatic.conf
systemctl enable --now dnf-automatic-install.timer
```

This repository contains a script for deploying:
```sh
dotnet run --project Scripts -- deploy --help
```

### Extra tools

To get better release binaries some extra tools are needed.

Runtime relinking will reduce the runtime size clients need to
download. It should enable if you run the following:
```sh
dotnet workload install wasm-tools
```

Though it seems only to apply when using AOT compilation. To enable that
edit the client `.csproj` to include:

```xml
<PropertyGroup>
  <RunAOTCompilation>true</RunAOTCompilation>
</PropertyGroup>
```

Note that this increases the app download size but should help performance.

### Backups

TODO: redo this for multi server

Backups can be configured to be stored in S3. They will contain a
database dump and redis data.

For database dumping `pg_dump` needs to be installed.

For redis, the user running RevolutionaryWebApp needs to be in the redis
group or some other way read access needs to be arranged to the redis
state file.

```sh
usermod -a -G redis revolutionarywebapp
```

## Developing

This section has some info on developing the RevolutionaryWebApp.

### Creating DB migrations

After creating a new model and putting registration of it into
`ApplicationDbContext`, a database migration needs to be created that
will actually create the right data in the database about the new
model. Again, the `ef` helper tool can be used to create the migration:

```sh
dotnet run --project Scripts -- ef -c AddedSomeStuff
```

After confirming the migration looks good (the tool also has an option
to delete and recreate the migration), you need to migrate your local
database.

## Maintenance

### Out of sync sequences

HiLo sequences can be synced with actual data with a provided script:
```sh
psql -d revolutionarywebapp < fix_hilo_sequences.sql
```
