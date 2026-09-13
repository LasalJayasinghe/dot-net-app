# Development Guide

## Prerequisites

- .NET 9 SDK
- Node.js (v18+)
- MySQL

## Running Locally

1. Create a `.env` file in the root based on `.env.example`, and fill in the values.
   `.env` is the only source of configuration — `appsettings.json` is intentionally
   empty. Keys use the `Section__Key` form, which ASP.NET Core reads as `Section:Key`
   (so `Jwt__Key` becomes `Jwt:Key`). Startup fails fast if `Jwt__Key` or
   `Cors__AllowedOrigins__0` is missing.
2. Start the database locally.
3. Apply migrations: `dotnet ef database update`.
4. Run backend: `dotnet watch run`.
5. For frontend, navigate to `insight-dashboard` and run `npm run dev`.

## Testing

Run backend tests using `dotnet test` inside the `dotnetApp.Tests` directory.
Run frontend e2e tests using `npx playwright test` inside `insight-dashboard`.
