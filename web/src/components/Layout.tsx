import {
  Calendar,
  CheckSquare,
  ClipboardList,
  FlaskConical,
  Hammer,
  Network,
  PlusCircle,
  Receipt,
  Workflow,
  type LucideIcon,
} from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { useRole } from '@/context/RoleContext'
import type { Role } from '@/context/roles'
import { cn } from '@/lib/utils'
import { DemoClock } from './DemoClock'
import { RoleSelector } from './RoleSelector'

interface NavItem {
  to: string
  label: string
  icon: LucideIcon
}

const CALENDAR: NavItem = { to: '/calendar', label: 'Calendario del salón', icon: Calendar }
const EXTERNAL: NavItem = { to: '/external', label: 'Sistemas externos', icon: Network }
const LAB: NavItem = { to: '/invariants', label: 'Laboratorio de invariantes', icon: FlaskConical }

const NAV: Record<Role, NavItem[]> = {
  Cliente: [
    { to: '/quotes/new', label: 'Cotizar', icon: PlusCircle },
    { to: '/quotes', label: 'Mis cotizaciones', icon: Receipt },
    { to: '/events', label: 'Mis eventos', icon: ClipboardList },
  ],
  Recepcion: [
    { to: '/quotes/new', label: 'Cotizar por un cliente', icon: PlusCircle },
    { to: '/quotes', label: 'Cotizaciones', icon: Receipt },
    { to: '/events', label: 'Eventos', icon: ClipboardList },
  ],
  Direccion: [
    { to: '/approvals', label: 'Bandeja de aprobaciones', icon: CheckSquare },
    { to: '/events', label: 'Todos los eventos', icon: ClipboardList },
  ],
  Operaciones: [{ to: '/operations', label: 'Eventos de hoy', icon: Hammer }],
}

export function Layout() {
  const { role } = useRole()
  const items = [...NAV[role], CALENDAR, EXTERNAL, LAB]

  return (
    <div className="flex min-h-full flex-col">
      <header className="border-b border-border bg-card px-4 py-2.5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Workflow className="h-5 w-5 text-primary" />
            <span className="text-base font-semibold">Nodo · Agenda y Eventos</span>
          </div>
          <RoleSelector />
          <DemoClock />
        </div>
      </header>
      <div className="flex flex-1">
        <nav aria-label="Navegación principal" className="w-56 shrink-0 border-r border-border bg-card p-3">
          <ul className="space-y-1">
            {items.map(({ to, label, icon: Icon }) => (
              <li key={to}>
                <NavLink
                  to={to}
                  end={to === '/quotes'}
                  className={({ isActive }) =>
                    cn(
                      'flex items-center gap-2 rounded-md px-2.5 py-2 text-sm hover:bg-muted',
                      isActive && 'bg-primary/10 font-medium text-primary',
                    )
                  }
                >
                  <Icon className="h-4 w-4" />
                  {label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <main className="min-w-0 flex-1 p-5">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
