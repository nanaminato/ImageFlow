# ImageFlow Web

Angular 22.3.0 + Angular Material frontend for ImageFlow. See the [root README](../../../README.md) for complete setup and backend configuration.

```sh
npm ci
npm start
npm test
npm run build
npx playwright install chromium
npm run test:e2e
```

`npm start` serves localhost:4200 and proxies `/api` to localhost:5080. The backend uses Mock by default. Playwright starts isolated Mock API and frontend servers; stop servers occupying those ports first.

Architecture: `core` owns typed HTTP/SSE access and models; `features/workspace/data-access` owns the NgRx Signal Store; `components` contains the planner, editor, queue and gallery. The root app is only a router shell.
