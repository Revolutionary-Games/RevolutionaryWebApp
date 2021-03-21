# README

IN PROGRESS ATTEMPT IN REWRITING IN BLAZOR AND ASP.NET CORE

## Database setup



### Getting an admin account

To get the first admin account, first setup the DB then run this SQL
on it (replace the id with a uuid):
```sql
INSERT INTO redeemable_codes (id, granted_resource) VALUES ('UUID_GOES_HERE', 'GroupAdmin');
```

Then you can redeem the code on your user profile after logging in to become an admin.


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
