import { defineConfig, devices } from '@playwright/test'
if (!process.env.PHONESTORE_E2E_SQL || !process.env.PHONESTORE_E2E_MAILBOX || !process.env.PHONESTORE_E2E_IMAGES) throw new Error('Run scripts/test-catalog-e2e.ps1: catalog E2E requires an isolated SQL DB and private mailbox.')
function port(value: string | undefined, fallback: number) { if (value === undefined) return fallback; if (!/^\d{4,5}$/.test(value) || Number(value) < 1024 || Number(value) > 65535) throw new Error('Invalid test port'); return Number(value) }
const apiPort = port(process.env.PHONESTORE_E2E_API_PORT, 5080), webPort = port(process.env.PHONESTORE_E2E_WEB_PORT, 5175)
const configuration = process.env.PHONESTORE_E2E_CONFIGURATION ?? 'Debug'
if (!['Debug', 'Release'].includes(configuration)) throw new Error('Invalid build configuration')
export default defineConfig({
  testDir: './e2e', testMatch: ['catalog.spec.ts', 'inventory.spec.ts', 'storefront.spec.ts', 'cart.spec.ts', 'checkout.spec.ts', 'checkout-ui.spec.ts', 'order-management.spec.ts'], workers: 1, retries: 0, forbidOnly: !!process.env.CI,
  use: { baseURL: `http://127.0.0.1:${webPort}`, trace: 'off', screenshot: 'off', video: 'off' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    { command: `dotnet run --project ../backend/PhoneStore.Api --configuration ${configuration} --no-launch-profile --urls http://127.0.0.1:${apiPort}`, url: `http://127.0.0.1:${apiPort}/api/ready`, reuseExistingServer: false,
      env: { ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__DefaultConnection: process.env.PHONESTORE_E2E_SQL, Auth__MailboxPath: process.env.PHONESTORE_E2E_MAILBOX, Catalog__ImagePath: process.env.PHONESTORE_E2E_IMAGES, Auth__RateLimit__auth: '1000', Auth__RateLimit__csrf: '1000', Auth__RateLimit__checkout: '1000', Auth__RateLimit__quote: '1000', Auth__RateLimit__guest: '1000' }, timeout: 120000 },
    { command: `npm run dev -- --host 127.0.0.1 --port ${webPort}`, url: `http://127.0.0.1:${webPort}`, reuseExistingServer: false, env: { VITE_API_PROXY_TARGET: `http://127.0.0.1:${apiPort}` } },
  ],
})
