import { beforeEach } from 'vitest'

// Node 25 exposes its own localStorage; use real DOM Storage in component tests.
// Browser persistence and cross-tab behavior are checked separately with Chromium.
const dom = (globalThis as typeof globalThis & { jsdom: { window: Window } }).jsdom
const storage = dom.window.localStorage
Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: storage })
Object.defineProperty(globalThis, 'sessionStorage', { configurable: true, value: dom.window.sessionStorage })
beforeEach(() => storage.clear())
beforeEach(() => dom.window.sessionStorage.clear())
