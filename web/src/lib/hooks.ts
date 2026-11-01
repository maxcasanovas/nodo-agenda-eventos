import { useEffect, useState } from 'react'
import { useCustomers } from '@/api/hooks'
import { useRole } from '@/context/RoleContext'
import { ROLE_ACTOR } from '@/context/roles'

// The single hall of the prototype (seeded with a fixed id).
export const HALL_ID = '11111111-1111-1111-1111-111111111111'

export function useDebounced<T>(value: T, delayMs = 400): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}

/** Name that signs the actions of the current role (the selected customer, or the staff member of the role). */
export function useActor(): string {
  const { role, customerId } = useRole()
  const customers = useCustomers()
  if (role === 'Cliente') return customers.data?.find((c) => c.memberId === customerId)?.fullName ?? 'Cliente'
  return ROLE_ACTOR[role]
}
