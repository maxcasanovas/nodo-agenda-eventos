import { useCustomers } from '@/api/hooks'
import { useRole } from '@/context/RoleContext'
import { ROLE_LABEL, type Role } from '@/context/roles'
import { Select } from './ui/form'

export function RoleSelector() {
  const { role, setRole, customerId, setCustomerId } = useRole()
  const customers = useCustomers()

  return (
    <div className="flex flex-wrap items-center gap-2">
      <label htmlFor="role-select" className="text-sm text-muted-foreground">
        Rol
      </label>
      <Select id="role-select" className="w-36" value={role} onChange={(e) => setRole(e.target.value as Role)}>
        {(Object.keys(ROLE_LABEL) as Role[]).map((r) => (
          <option key={r} value={r}>
            {ROLE_LABEL[r]}
          </option>
        ))}
      </Select>
      {role === 'Cliente' && (
        <>
          <label htmlFor="customer-select" className="sr-only">
            Cliente
          </label>
          <Select id="customer-select" className="w-52" value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
            {customers.data?.map((c) => (
              <option key={c.memberId} value={c.memberId}>
                {c.fullName}
              </option>
            ))}
          </Select>
        </>
      )}
    </div>
  )
}
