import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import { Layout } from '@/components/Layout'
import { useRole } from '@/context/RoleContext'
import type { Role } from '@/context/roles'
import { ApprovalsPage } from '@/features/approvals/ApprovalsPage'
import { EventDetailPage } from '@/features/events/EventDetailPage'
import { EventsListPage } from '@/features/events/EventsListPage'
import { ExternalPanelPage } from '@/features/external-panel/ExternalPanelPage'
import { InvariantsLabPage } from '@/features/invariants/InvariantsLabPage'
import { OperationsPage } from '@/features/operations/OperationsPage'
import { QuoteFormPage } from '@/features/quotes/QuoteFormPage'
import { QuotesListPage } from '@/features/quotes/QuotesListPage'

// FullCalendar is the heaviest dependency: load it only when the calendar is opened.
const CalendarPage = lazy(() => import('@/features/calendar/CalendarPage').then((m) => ({ default: m.CalendarPage })))

const HOME: Record<Role, string> = {
  Cliente: '/quotes/new',
  Recepcion: '/quotes/new',
  Direccion: '/approvals',
  Operaciones: '/operations',
}

function Home() {
  const { role } = useRole()
  return <Navigate to={HOME[role]} replace />
}

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="quotes/new" element={<QuoteFormPage />} />
        <Route path="quotes" element={<QuotesListPage />} />
        <Route path="events" element={<EventsListPage />} />
        <Route path="events/:id" element={<EventDetailPage />} />
        <Route path="approvals" element={<ApprovalsPage />} />
        <Route path="operations" element={<OperationsPage />} />
        <Route
          path="calendar"
          element={
            <Suspense fallback={<p className="text-sm text-muted-foreground">Cargando calendario…</p>}>
              <CalendarPage />
            </Suspense>
          }
        />
        <Route path="external" element={<ExternalPanelPage />} />
        <Route path="invariants" element={<InvariantsLabPage />} />
        <Route path="*" element={<Home />} />
      </Route>
    </Routes>
  )
}
