# DMS web user interface (Vue 3 + TypeScript)

The browser front end of the Delivery Management System. It is served by the C# program
(`program.cs`) from `../wwwroot` and calls the C# web API under `/api`.

| Command | What it does |
|---|---|
| `npm install` | Install the tools (first time only) |
| `npm run dev` | Development server on http://localhost:5173 (start `dotnet run` too) |
| `npm run build` | Type-check with vue-tsc, then build into `../wwwroot` |
| `npm run lint` | ESLint (Vue style guide, typescript-eslint) and Prettier check |
| `npm test` | Vitest unit and component tests |

Layout: `src/api` (typed API client), `src/session` (sign-in state), `src/router` (screens and roles),
`src/validation` (input rules), `src/components` (shared pieces), `src/views` (one folder per business
area). See `../Docs/Web-UI-Design.md`.
