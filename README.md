# README

## System Dependencies

You need to have PostgreSQL and Redis on localhost available to the current user

To make the stackwalk API work you need to put the executable for that at: `StackWalk/minidump_stackwalk`

## Environment variables

Various aspects are configured with environment variables. Most of the
features are disabled if the environment variables are
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
