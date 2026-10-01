# Repository guide

## Repository overview

RevolutionaryWebApp is an ASP.NET Core and Blazor application used by the Thrive
development infrastructure. The solution targets .NET 10. Keep the
`RevolutionaryGamesCommon` git submodule initialized and up to date when the
change depends on code from it:

```sh
git submodule update --init --recursive
```

The main projects are:

- `Server/` contains the ASP.NET Core host, controllers, services, Hangfire
  jobs, authentication, and the PostgreSQL Entity Framework Core model.
- `Client/` contains the Blazor WebAssembly pages and reusable UI components.
- `Shared/` contains DTOs and other types shared by the server and client.
- `Server.Common/` contains server-side code shared with other executables.
- `Server.Tests/` and `Shared.Tests/` contain unit and integration tests.
- `AutomatedUITests/` contains browser-based integration tests.
- `Scripts/` contains the repository helper commands, including the required
  EF migration wrapper.
- `RevolutionaryGamesCommon/` is a submodule containing shared libraries and
  their tests.

The server is configured in `Server/Startup.cs`. `ApplicationDbContext` is the
normal application database context; `ProtectionKeyContext` is the separate
context used for data-protection keys.

## Configuration and local development

The application requires PostgreSQL. Redis or a compatible service is normally
required as shared state. Optional features use S3-compatible storage. Copy
`Server/appsettings.Development.json.template` to
`Server/appsettings.Development.json` and fill in local values. The local file
is ignored and must not be committed.

Production configuration uses environment variables with double underscores,
such as `ConnectionStrings__WebApiConnection`. `BaseUrl` must end with a
trailing slash. Keep certificates, passwords, API keys, connection strings, and
other secrets out of tracked files.

Run the application from the repository root with:

```sh
dotnet watch --project Server run
```

The usual development URL is `http://localhost:5000`.

## Entity Framework migrations

The repository helper is mandatory for creating EF migrations. Do not create,
rename, or hand-edit migration files or the model snapshot, and do not use a
raw `dotnet ef migrations add` command for a new migration. Manually-created
migrations can be missing EF metadata and will not be discovered or applied,
even if their `Up` method looks correct.

After changing an entity model, registering a new `DbSet`, or changing
`ApplicationDbContext.OnModelCreating`, create the migration from the repository
root using:

```sh
dotnet run --project Scripts -- ef -c MigrationName
```

The helper invokes the version of `dotnet-ef` matching the server project and
runs it with `Server/RevolutionaryWebApp.Server.csproj` and
`ApplicationDbContext`. Inspect all generated migration files and the snapshot
before applying them. In particular, check that renames are represented as
renames rather than an unintended drop-and-add.

Useful helper operations are:

```sh
# Install or update the matching dotnet-ef tool
dotnet run --project Scripts -- ef -i
dotnet run --project Scripts -- ef -u

# Apply all pending migrations to the configured local database
dotnet run --project Scripts -- ef -m

# Create an idempotent SQL script in migration.sql
dotnet run --project Scripts -- ef --sql

# Remove the latest migration after first down-migrating the database
dotnet run --project Scripts -- ef -r

# Down-migrate to a named migration, or use 0 to clear the database
dotnet run --project Scripts -- ef -d MigrationName

# Down-migrate, recreate the latest migration, and migrate again
dotnet run --project Scripts -- ef --redo MigrationName,NewMigrationName

# Operate on the protection-key database context
dotnet run --project Scripts -- ef -t Keys -m
```

The short `-c` option means “create migration”; it is unrelated to the EF
database context. The default context is `ApplicationDbContext`; `-t Keys`
selects `ProtectionKeyContext`.

Before changing a migration that has already been shared or applied, inspect
the database's `__EFMigrationsHistory` and coordinate the correction. Do not
solve a schema mismatch by editing the snapshot to make EF stop detecting a
change. The Docker build also generates an idempotent script from the
discoverable migrations and the container entrypoint executes that script, so
missing migration metadata affects deployments too.

## Testing and verification

Run the repository checks relevant to the change. Common commands are:

```sh
dotnet build RevolutionaryWebApp.sln
dotnet test
dotnet run --project Scripts -- check
dotnet run --project Scripts -- test
```

`Scripts check` includes the repository's rewrite and inspection checks. The
rewrite check may report formatting changes; review those changes before
committing. Migration files are intentionally excluded from the normal code
checks because they are generated artifacts.

Most server tests can use the in-memory provider, but database-backed tests
need PostgreSQL databases and user secrets. Configure the test connection in
`Server.Tests` as `UnitTestConnection` and the browser test connection in
`AutomatedUITests` as `IntegrationTestConnection`. Test databases must be
separate from development databases, and the test PostgreSQL user needs
permission to create databases.

Browser tests require Playwright browsers after building the solution. Follow
the version-specific script path produced under the test project's build output
when installing them.

## Deployment and database scripts

The Docker build creates an idempotent EF SQL script from the `Server` project;
the application container applies it before starting the server. If a database
change is part of a deployment, generate and review the script through the
custom helper and confirm that the migration is listed by EF before building
the image.

The deployment helper is invoked with:

```sh
dotnet run --project Scripts -- deploy --help
```

Do not commit generated local files such as `migration.sql`, build output,
development configuration, logs, or credentials.

## Change guidelines

- Keep API contracts in `Shared/` synchronized with their server controllers
  and client consumers.
- Put persistence model changes in `Server/Models/` and configure them in
  `ApplicationDbContext`; always create the corresponding migration with the
  helper above.
- Follow existing controller, job, service, and Blazor component boundaries
  instead of moving server logic into the client.
- Preserve existing user data and migration history. Prefer an explicit,
  reversible migration for schema changes.
- Run focused tests first, then the relevant build or full checks before
  handing off the change.

