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
        "StorageConnectionString": "<Connection string for Table storage or storage emulator>",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "AIRE_MODULE_ID": "aire.development.memory",
        "AIRE_SERVICE_BASE": "http://localhost:7071/api/",
        "AIRE_SERVICE_KEY": "<service key secret>",
        "TOKEN_ENCRYPTION_KEY": "<enryption key shared between platform modules>",
        "TOKEN_SIGNING_KEY": "<signing key shared between platform modules>",
    },
    "Host": {
        "LocalHttpPort": 7073,
        "CORS": "*",
        "CORSCredentials": false
    }
}
```

Use the same token keys you are using in AIRe Services module.

## Module Settings

AIRe Services platform module may configure the following settings:

```jsonc
{
    // Tells AI module which vector database to use to store this service's embeddings
    "vector_database_name": "aire_development_memory_db"
}
```

## API Documentation

Visit path `/api/swagger/ui` to inspect. The default host is set to `/api` path.

You can set a custom host with `OpenApi__HostNames` environment value.

## Deployment

Publish the Fuctions app and then setup the following required environment values:

- `AIRE_MODULE_ID` The identifier of the module as it is configured in the platform.
- `AIRE_SERVICE_BASE` The endpoint of the AIRe Services module.
- `AIRE_SERVICE_KEY` The service key for the AIRe Services module.
- `TOKEN_SIGNING_KEY` The token signing key shared between the platform instance modules.
- `TOKEN_ENCRYPTION_KEY` The token encryption key shared between the platform instance modules.
- `StorageConnectionString` Azure Table Storage connection string
