# Development Guide

## Prerequisites

- .NET 9 SDK
- Node.js (v18+)
- MySQL

## Running Locally

1. Create a `.env` file in the root based on `.env.example`.
2. Start the database locally.
3. Apply migrations: `dotnet ef database update`.
4. Run backend: `dotnet watch run`.
5. For frontend, navigate to `insight-dashboard` and run `npm run dev`.

## Testing

Run backend tests using `dotnet test` inside the `dotnetApp.Tests` directory.
Run frontend e2e tests using `npx playwright test` inside `insight-dashboard`.
