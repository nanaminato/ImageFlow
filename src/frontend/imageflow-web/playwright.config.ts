import { defineConfig } from '@playwright/test';
import path from 'node:path';
process.env['NO_PROXY'] = [process.env['NO_PROXY'], 'localhost', '127.0.0.1', '::1'].filter(Boolean).join(',');
export default defineConfig({
 testDir: './e2e', fullyParallel: false, workers: 1,
 use: { baseURL: 'http://localhost:4200', browserName: 'chromium', launchOptions: process.env['PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH'] ? { executablePath: process.env['PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH'] } : {} },
 webServer: process.env['IMAGEFLOW_E2E_EXTERNAL'] ? [] : [
  { command: 'dotnet run --no-launch-profile --project ../../backend/ImageFlow.Api --urls http://localhost:5080', url: 'http://localhost:5080/health', timeout: 90000, env: { Engine__Provider: 'Mock', Engine__DataDirectory: path.resolve('../../../.cache/e2e', String(Date.now())) } },
  { command: 'npm start -- --host localhost', url: 'http://localhost:4200', timeout: 90000 }
 ]
});


