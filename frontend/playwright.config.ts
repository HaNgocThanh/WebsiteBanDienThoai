import { defineConfig, devices } from '@playwright/test'
export default defineConfig({
  testDir: './e2e',
  testMatch: 'health.spec.ts',
  forbidOnly: !!process.env.CI,
  retries: 0,
  use: { baseURL: 'http://127.0.0.1:5175', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../backend/PhoneStore.Api --no-launch-profile --urls http://127.0.0.1:5080',
      url: 'http://127.0.0.1:5080/api/health',
      reuseExistingServer: false,
      env: { ASPNETCORE_ENVIRONMENT: 'Testing', ConnectionStrings__DefaultConnection: 'Server=localhost;Database=PhoneStore_Test_Browser;Integrated Security=True;Encrypt=True' },
      timeout: 120000,
    },
    { command: 'npm run dev -- --host 127.0.0.1 --port 5175', url: 'http://127.0.0.1:5175', reuseExistingServer: false },
  ],
})
