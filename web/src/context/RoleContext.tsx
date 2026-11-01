import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { ROLE_LABEL, type Role } from './roles'

interface RoleState {
  role: Role
  customerId: string
  setRole: (role: Role) => void
  setCustomerId: (customerId: string) => void
}

const STORAGE_KEY = 'nodo.role'
// Ana, a customer with an active membership: the protagonist of the demo script.
const DEFAULT_CUSTOMER = '22222222-0000-0000-0000-000000000001'

function load(): { role: Role; customerId: string } {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      const parsed = JSON.parse(raw) as { role?: Role; customerId?: string }
      if (parsed.role && parsed.role in ROLE_LABEL) return { role: parsed.role, customerId: parsed.customerId ?? DEFAULT_CUSTOMER }
    }
  } catch {
    // storage unavailable or corrupt: start from defaults
  }
  return { role: 'Cliente', customerId: DEFAULT_CUSTOMER }
}

const RoleContext = createContext<RoleState | null>(null)

export function RoleProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState(load)

  const persist = useCallback((next: { role: Role; customerId: string }) => {
    setState(next)
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
    } catch {
      // not persisted, still works for this session
    }
  }, [])

  const value = useMemo<RoleState>(
    () => ({
      ...state,
      setRole: (role) => persist({ ...state, role }),
      setCustomerId: (customerId) => persist({ ...state, customerId }),
    }),
    [state, persist],
  )

  return <RoleContext.Provider value={value}>{children}</RoleContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useRole(): RoleState {
  const context = useContext(RoleContext)
  if (!context) throw new Error('useRole must be used inside RoleProvider')
  return context
}
