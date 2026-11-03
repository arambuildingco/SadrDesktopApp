import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { auth, api, type SessionUser } from './api'

type AuthState = {
  user: SessionUser | null
  login: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: 'acceptTransfers' | 'manageUsers' | 'invoices') => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(() => auth.user)

  const value = useMemo<AuthState>(
    () => ({
      user,
      async login(username, password) {
        const data = await api.post<{ accessToken: string; refreshToken: string; user: SessionUser }>(
          '/api/auth/login',
          { username, password }
        )
        auth.save(data)
        setUser(data.user)
      },
      async logout() {
        try {
          await api.post('/api/auth/logout')
        } catch {
          // signing out locally must work even when the API is unreachable
        }
        auth.clear()
        setUser(null)
      },
      can(permission) {
        const role = user?.role ?? ''
        if (permission === 'acceptTransfers') return role === 'Admin' || role === 'Warehouse'
        if (permission === 'manageUsers') return role === 'Admin'
        return role !== 'Warehouse'
      }
    }),
    [user]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
