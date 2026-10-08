import { defineConfig, devices } from '@playwright/test'
if (!process.env.PHONESTORE_E2E_SQL || !process.env.PHONESTORE_E2E_MAILBOX) throw new Error('Run scripts/test-auth-e2e.ps1: auth E2E requires an isolated SQL DB and private mailbox.')
export default defineConfig({
  testDir: './e2e', testMatch: 'auth.spec.ts', workers: 1, retries: 0, forbidOnly: !!process.env.CI,
  use: { baseURL: 'http://127.0.0.1:5175', trace: 'off', screenshot: 'off', video: 'off' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    { command: 'dotnet run --project ../backend/PhoneStore.Api --no-launch-profile --urls http://127.0.0.1:5080', url: 'http://127.0.0.1:5080/api/ready', reuseExistingServer: false,
      env: { ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__DefaultConnection: process.env.PHONESTORE_E2E_SQL, Auth__MailboxPath: process.env.PHONESTORE_E2E_MAILBOX, Auth__RateLimit__auth: '1000', Auth__RateLimit__csrf: '1000' }, timeout: 120000 },
    { command: 'npm run dev -- --host 127.0.0.1 --port 5175', url: 'http://127.0.0.1:5175', reuseExistingServer: false },
  ],
})
