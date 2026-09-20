# Staff frontend

React + TypeScript frontend for the existing Steam Museum API. Requires Node.js 22.12+ (Node 24 recommended).

## Development

Start and configure the API using the repository README, then run from this directory:

```powershell
npm ci
npm run dev
```

Open `https://localhost:5173`. Development defaults to the API at `https://localhost:7240`. Both must use `localhost` and HTTPS for the API's Secure, SameSite=Strict session cookies. Vite generates a local self-signed development certificate; trust it locally in your browser, and trust the API's ASP.NET development certificate as described in the root README. Never use development certificates in production.

For a different API URL, copy `.env.example` to `.env.local`, edit `VITE_API_URL`, and restart Vite. This value is public build-time configuration; never put secrets in Vite environment variables. The API must allow the exact frontend origin in `Cors:Origins`.

## Included screens

- Secure cookie sign-in, forced temporary password change, voluntary password changes, logout and session-expiry handling. CSRF tokens stay in memory and refresh after authentication changes.
- Availability calendar with weekday selection, individual responses, partial-day times, preferred roles, notes, shift limits and closed-window viewing.
- Published member duties and competence/training history.
- Planner duty creation, draft assignments, publication, cancellation, conflict filtering, member availability and window management.
- Assessor qualification creation/revocation and append-only training records.
- Administrator account creation, activation, replacement access roles, password resets, reference data and audit history.

Role visibility mirrors API policies; the API remains the authority for access control and all roster rules. Dates and duty times remain local calendar strings. Deadline inputs use the browser's local time zone and are sent as UTC. No API exists for candidate eligibility previews, duty editing/deletion or reading another account's roles. Assignment failures surface server explanations, and role replacement explicitly explains that current roles cannot be loaded.

## Production

```powershell
npm ci
npm test
npm run build
```

Deploy `dist/` behind an HTTPS web server on the same origin as the API, routing `/api/*` to ASP.NET and other paths to the static frontend. With no `VITE_API_URL` at build time, production API requests use the same origin. For a separate permitted origin, set `VITE_API_URL` during the build and configure API CORS and same-site hosting. Keep credentials and data out of static files. `npm run preview` is only a local preview server, not a production host.

## Verification

`npm test` covers CSRF rotation, cookie requests, server errors, role restrictions, forced password changes, availability preservation, unavailable-day normalization, time validation and date boundaries. `npm run build` performs strict TypeScript checks and creates a production bundle.

For repeatable browser layout checks without real accounts or a database, build with `VITE_API_URL` unset, then run `node tests/preview.mjs` and open `http://localhost:5180`. This explicitly separate server supplies synthetic records and implements only the preview interactions; it is never loaded by the application or normal development/production servers. It does not test live API authentication or database integration.
