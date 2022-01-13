# README

This is a web app that has various features to help in the development of Thrive.

## System dependencies

### Database setup

ThriveDevCenter requires a PostgreSQL database to operate.

You can create a new account and a database for the account with `psql`:
```sql
CREATE USER thrivedevcenter WITH LOGIN PASSWORD 'PUTAPASSWORDHERE';
```

### Redis

Redis is an optional dependency for sharing state between multiple
instances (and remembering rate limit rates over restarts).

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
        "s3:ListBucketMultipartUploads"
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

After that running in development should work if you execute
the following in the Server folder:
```sh
dotnet watch run
```

Then the development site should be available at http://localhost:5000
now.

## Running

The server computer needs to the following packages
aspnetcore-runtime-5.0 (at the time of writing), nginx, and git
installed. Or you can alternatively have a different proxy
server than nginx. Additionally of course the database and redis can be ran on
the same server.

There are example template nginx and systemd files in the `templates`
folder. After copying and modifying them (remember to also setup the
devcenter linux local account and adjust the systemd service
environment variable files) you can enable and start the service
`systemctl enable --now thrivedevcenter` (might need to do a daemon
reload first). Then check that the service is up and running
`systemctl status thrivedevcenter`. After it is up, you should verify
nginx config and reload it.

Before starting the server you need to migrate the database. To do
this use the dotnet entity framework tool to update the localhost
server, or see how the deploy script generates an sql script and then
executes it on a remote server. There's an utility script provided
that can do it automatically:

```sh
ruby ef.rb -m
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
there needs to be a folder that both the service container and ThriveDevCenter
can access to have correct symbol files.

This can be setup for example with:
```sh
useradd stackwalk -s /sbin/nologin
mkdir /var/lib/thrivedevcenter/
mkdir /var/lib/thrivedevcenter/symbols
mkdir /var/lib/thrivedevcenter/symbols/production
chown root:thrivedevcenter /var/lib/thrivedevcenter/symbols/production
chmod g+w /var/lib/thrivedevcenter/symbols/production
```

Then the stackwalk service configured to have 
`/var/lib/thrivedevcenter/symbols/production` as the symbols folder mounted
inside the container. The container needs to also have the same port configured
as in the following ThriveDevCenter configuration:
```
Crashes__Enabled=true
Crashes__StackwalkService=http://localhost:3115
Crashes__StackwalkSymbolFolder=/var/lib/thrivedevcenter/symbols/production
```

Refer to the repository linked above for full instructions but the following
will get a running stackwalk service going:
```sh
podman run -d --rm -p 127.0.0.1:3115:9090 --mount type=bind,src=/var/lib/thrivedevcenter/symbols/production,destination=/Symbols,ro=true,relabel=shared --name stackwalkweb hhyyrylainen/stackwalk:latest --http-port 9090
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
ALTER USER thrivedevcenter_test CREATEDB;
```

Note that while you can use a single user for testing, the unittest
and test databases need to be separate.

Now set the secrets by running in the Server.Tests folder:
```sh
dotnet user-secrets set UnitTestConnection 'User ID=thrivedevcenter_test;Password=PASSWORDHERE;Server=localhost;Port=5432;Database=thrivedevcenter_unittest;Integrated Security=true;Pooling=true;'
```

And in the AutomatedUITests folder:
```sh
dotnet user-secrets set IntegrationTestConnection 'User ID=thrivedevcenter_test;Password=PASSWORDHERE;Server=localhost;Port=5432;Database=thrivedevcenter_test;Integrated Security=true;Pooling=true;'
```

### Selenium

For selenium to work you need to have Chrome / Chromium installed.


### Running the tests

Most tests can be ran like normal.

However the AutomatedUITests project contains tests that require a
test server instance to be running. To start that, run in the Server
folder:

```sh
dotnet run --launch-profile ThriveDevCenter.Server.Testing
```

You should then stop the test server once the tests have been ran.

See: https://github.com/dotnet/aspnetcore/issues/4892 for why this is needed

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
docker build . --target proxy --tag thrivedevcenter-proxy:latest
docker build . --target application --tag thrivedevcenter-web:latest

docker network create thrivedevcenter
docker volume create pgdata
docker run -d -v pgdata:/var/lib/postgresql/data --rm --restart on-failure --name thrivedevcenter_db --network=thrivedevcenter -e POSTGRES_PASSWORD=SPECIFYAPASSWORD -e POSTGRES_DB=thrivedevcenter postgres:13
docker volume create redis_data
docker run -d -v redis_data:/data --rm --restart on-failure --name thrivedevcenter_redis --network=thrivedevcenter redis:6 redis-server --appendonly yes
docker run -d --rm --restart on-failure --name thrivedevcenter_web --network=thrivedevcenter -e PGPASSWORD=SPECIFYAPASSWORD -e ASPNETCORE_ENVIRONMENT=Production thrivedevcenter-web:latest
docker run -d -p 80:80 --rm --restart on-failure --name thrivedevcenter_proxy --network=thrivedevcenter thrivedevcenter-proxy:latest
```

Creating an admin redeem code:
```sh
docker exec -it thrivedevcenter_db psql -U postgres -d thrivedevcenter
```

Provide the password used in the previous step if prompted.

And now follow the instructions in the "Getting an admin account"
section.

## Deploying

First prepare the server to deploy with all the software.

For CentOS / Fedora you can install things with:
```sh
dnf install aspnetcore-runtime-5.0 git postgresql-server redis nginx rsync cronie dnf-automatic emacs-nox certbot-nginx tmux wget
```
Note that some packages are optional but better for a full production setup.

And then configure them.
Finally edit the remote hosts configuration of the deploy script and run it.

Additional recommended server setup:

```sh
useradd -s /usr/bin/false thrivedevcenter
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

## Maintenance

### Out of sync sequences

HiLo sequences can be synced with actual data with a provided script:
```sh
psql -d thrivedevcenter < fix_hilo_sequences.sql
```
