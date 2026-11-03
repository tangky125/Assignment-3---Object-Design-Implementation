# How to build and run the Delivery Management System

## What you need

- **.NET 8 SDK** – https://dotnet.microsoft.com/download (check: `dotnet --version`)
- **Node.js 20 or 22** (includes npm) – only to change or rebuild the user interface (check: `node --version`)

## Run it (the built UI is already in `wwwroot/`)

```bash
dotnet run
```

Open **http://localhost:5080** in a browser. Stop the server with Ctrl+C.
Demo accounts are listed on the Help page (password `demo123`).

## Rebuild the user interface after changing it

```bash
cd WebUi
npm install          # first time only
npm run build        # type-checks (vue-tsc) and builds into ../wwwroot
cd ..
dotnet run
```

## Work on the UI with instant reload

Run the server in one terminal (`dotnet run`) and, in a second terminal:

```bash
cd WebUi
npm run dev          # http://localhost:5173 - API calls go to the server on port 5080
```

## Checks (evidence for the report)

| Command (folder) | Shows |
|---|---|
| `dotnet build` (project folder) | C# compiles: **Build succeeded** |
| `dotnet format --verify-no-changes` (project folder) | C# follows the coding standard |
| `npm run build` (WebUi) | TypeScript type-checks and the UI compiles |
| `npm run lint` (WebUi) | Vue style guide + typescript-eslint + Prettier, no problems |
| `npm test` (WebUi) | 17 automated UI tests pass |
