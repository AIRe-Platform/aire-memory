# AIRe Memory

This module handles storing the data that the AIRe platform manages.

## Getting Started

You need to have [.NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) installed. Pull the repository and its submodules.

Open the solution in VS Code (recommended, works on Windows/Linux/macOS). You may also use Visual Studio on macOS and Windows.

On VS Code: Install the recommended extensions.

Configure `local.settings.json` as instructed. You should be running the AIRe Services module locally, too.

Hit F5 and you should be good to go.

## Configuration

You should create `local.settings.json` in the root of the repository when developing locally. It should look something like this:

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "AirePlatformService": "http://localhost:7071/api",
        "AIRE_SERVICE_KEY": "<service key secret>",
        "TokenSigningKey": "<signing key shared between platform modules>",
        "TokenEncryptionKey": "<enryption key shared between platform modules>",
        "DatabaseConnectionString": "Host=localhost;Database=aire-memory;Username=...;Password=..."
    },
    "Host": {
        "LocalHttpPort": 7073,
        "CORS": "*",
        "CORSCredentials": false
    }
}
```

Use the same token keys you are using in AIRe Services module.

## Setting Up the Database

The schema is managed in a code-first fashion. Define your entities in `src/DatabaseContext.cs` and then use the command below to generate migrations.

```sh
dotnet ef migrations add MyNewMigrationName
```

Run the following command to run migrations.

```sh
DatabaseConnectionString="..." dotnet ef database update
```

## API Documentation

Visit path `/api/swagger/ui` to inspect. The default host is set to `/api` path.

You can set a custom host with `OpenApi__HostNames` environment value.

## Deployment

Run migrations on the database as instructed above.

Publish the Fuctions app and then setup the following required environment values:

- `AirePlatformService` The endpoint of the AIRe Services module.
- `TokenSigningKey` The token signing key shared between the platform instance modules.
- `TokenEncryptionKey` The token encryption key shared between the platform instance modules.
- `DatabaseConnectionString` The connection string for a PostgreSql database.

## Disclaimer

This README is a work-in-progress. The information above may be out-dated or incorrect.
