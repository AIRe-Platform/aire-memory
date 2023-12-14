# AIRe Memory

This module handles storing the data that the AIRe platform manages.

## Getting Started

Open the solution in VS Code (on Windows/Linux/macOS). Install the recommended extensions. Hit F5 and you should be good to go.

You may also use Visual Studio on macOS and Windows.

Remember to configure!

## Configuration


You should create `local.settings.json` in the root of the repository when developing locally. It should look something like this:

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "AirePlatformService": "http://localhost:7071/api",
        "TokenSigningKey": "<signing key shared between platform modules>",
        "TokenEncryptionKey": "<enryption key shared between platform modules>",
        "DatabaseConnectionString": "Host=localhost;Database=aire-memory;Username=...;Password=...",
        "OpenApi__HostNames": "http://localhost:7073/api/"
    },
    "Host": {
        "LocalHttpPort": 7073,
        "CORS": "*",
        "CORSCredentials": false
    }
}
```

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

Visit path `/api/swagger/ui` to inspect. If running in localhost, there's an issue where the configuration file URL gets an invalid port.

Simply change in the correct port in the top bar to work around the issue.

Example: If the module is running on port `7073` change the URL to `http://localhost:7073/api/swagger.json`.

## Deployment

Run migrations on the database as instructed above.

Publish the Fuctions app and then setup the following required environment values:

- `AirePlatformService` The endpoint of the AIRe Services module.
- `TokenSigningKey` The token signing key shared between the platform instance modules.
- `TokenEncryptionKey` The token encryption key shared between the platform instance modules.
- `DatabaseConnectionString` The connection string for a PostgreSql database.

## Disclaimer

This README is a work-in-progress. The information above may be out-dated or incorrect.
