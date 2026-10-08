import { createContext, useContext } from 'react'
import type { Session } from '../services/auth'
import type { ApiError } from '../services/errors'

export interface AuthState { status: 'loading' | 'anonymous' | 'authenticated' | 'error'; user?: Session; error?: ApiError }
export const AuthContext = createContext<(AuthState & { refresh: () => Promise<Session | null>; clear: () => void }) | null>(null)
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('AuthProvider required')
  return context
}
