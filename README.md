# Handlezer

Handlezer is a .NET-based API for palm-reading-inspired identification, access control, distribution rules, and presence registration.

## Repository Layout

- `src/Handlezer.Api`: main ASP.NET Core API.
- `tests/Handlezer.Tests`: unit tests for application behavior.
- `onderzoek digitale handlezer.md`: project notes and background material.

## Getting Started

Prerequisites:

- .NET SDK 10
- MySQL 8

Restore, build, and test:

```bash
dotnet build Handlezer.sln
dotnet test tests/Handlezer.Tests/Handlezer.Tests.csproj
```

Apply database migrations:

```bash
cd src/Handlezer.Api
dotnet ef database update
```

Run the API:

```bash
dotnet run --project src/Handlezer.Api/Handlezer.Api.csproj
```

## Configuration

The API expects at least:

- `ConnectionStrings:DefaultConnection`
- `Authentication:BootstrapApiKey` for initial API key provisioning
- optionally `Authentication:OpenId:*` for bearer token validation

For local development, prefer user secrets.

Authentication and local setup details are documented in [src/Handlezer.Api/README.md](src/Handlezer.Api/README.md).

## Current Capabilities

- Hand registration and recognition
- Access policy creation and evaluation
- Distribution policy creation and consumption
- Presence check-in and check-out
- API key authentication for third-party integrations
- OpenID-compatible bearer token support for future dashboard/admin access
