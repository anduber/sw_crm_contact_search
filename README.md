# sw_crm_contact_search (Product Catalog Dataset)

This repository now demonstrates a simple .NET console benchmark where both `repository_before` and `repository_after` expose product lists entirely from memory.  
The “before” app keeps data in loose dictionaries while the “after” app introduces a typed catalog with a lightweight cache, showcasing a tiny but clear refactor.

## Requirements

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)

## Projects

- `sw_crm_contact_search.csproj` - Launcher that lets you choose which console app to run.
- `repository_before/` – Legacy console app exposing two helper functions that read in-memory dictionaries.
- `repository_after/` – Refined console app exposing the same product data via a reusable catalog and typed records.
- `tests/` – xUnit test project validating that both implementations serve the same data.

## Usage

```bash
# Launcher (prompts for before/after)
dotnet run --project sw_crm_contact_search.csproj

# Run projects individually
dotnet run --project repository_before/repository_before.csproj
dotnet run --project repository_after/repository_after.csproj

# Execute tests
dotnet test tests/tests.csproj
```

## Containers

- `docker build -t product-console .` builds the .NET image and runs the Release build steps.
- `docker run --rm product-console` executes the xUnit suite inside the container.
- `docker-compose up` launches the interactive launcher via `dotnet run` inside the container (bind mounted to the local workspace).
