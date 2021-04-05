# README

IN PROGRESS ATTEMPT IN REWRITING IN BLAZOR AND ASP.NET CORE

## Database setup

ThriveDevCenter requires a PostgreSQL database to operate.

You can create a new account with `psql`:

And a database for the account:
```sql
CREATE USER thrivedevcenter WITH LOGIN PASSWORD 'PUTAPASSWORDHERE';
```

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
dotnet ef database update
```

After that running in development should work if you execute
the following in the Server folder:
```sh
dotnet watch run
```

Then the development site should be available at http://localhost:5000
now.


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

### Test settings

The second database connection string needs to be also setup for
running the actual application for use in browser-based tests. To set
this up copy the `appsettings.Testing.json.template` as
`appsettings.Testing.json.template` and adjust the database connection
string in it.


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

--

OLD CONTENT:

## System Dependencies

You need to have PostgreSQL and Redis on localhost available to the current user.

To make the stackwalk API work you need to have [StackWalk web
service](https://github.com/hhyyrylainen/StackWalkAsAService) running
on localhost at port 3211. As long as you have docker installed you
can do that by running:

```sh
docker pull hhyyrylainen/stackwalk:latest
sudo docker run -itd -p 3211:3211 -v $(pwd)/SymbolData:/Symbols:ro --restart always --name stackwalkweb hhyyrylainen/stackwalk:latest --http-port 3211
```

Note: this creates a persistent docker container from the image, which
you must manually stop if you no longer need it.


## Environment variables

Various aspects are configured with environment variables. Most of the
features are automatically disabled if the environment variables are
missing. However you must have one environment variable defined when
running `foreman start`:

```
export BASE_URL="http://localhost:5000"
```

## Gems

Just do `bundle install --path vendor/bundle`

and then run everything with `bundle exec ...`

## Creating admin accounts

`bundle exec rake thrive:create_admin[email,password]`

## Granting or revoking admin status

Granting:
`bundle exec rake thrive:grant_admin[email]`

Revoking:
`bundle exec rake thrive:revoke_admin[email]`


## Cleaning sessions

This uses database stored sessions. They need to be cleaned out every
now and then. There is an example systemd service file which can do
that at `doc/example_thrivedevcenter-clean-sessions.service`.


## Managing LFS projects

Currently not possible from the GUI.

Creation with rake:

`bundle exec rake 'thrive:create_lfs_project[Project Name,proj,true]'`

## Tests

This project contains tests that can be ran with:
```
bundle exec rails test
```

If you get errors about "WARNING: Rails was not able to disable
referential integrity". You need to run the following as PostgreSQL
admin in psql:
```
ALTER USER putyourusernamehere WITH SUPERUSER;
```

That should fix the permissions and allow the tests to run.

RSpecs can be ran with:
```
bundle exec rspec
```

If the specs are failing you can check to see if the problem is with
the testing process by running a smoke test:
```
bundle exec rspec spec/hyperspec_smoke_test.rb
```
