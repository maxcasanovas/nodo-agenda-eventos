import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { POLL_MS } from '@/api/hooks'
import { hallEventsApi } from '@/api/resources'
import type { HallEventStatus } from '@/api/types'
import { Money } from '@/components/Money'
import { StatusBadge } from '@/components/StatusBadge'
import { eventStatusLabel } from '@/lib/status'
import { Card, Empty, PageTitle } from '@/components/ui/card'
import { Select } from '@/components/ui/form'
import { useRole } from '@/context/RoleContext'
import { formatDate, formatRange } from '@/lib/format'

const STATUSES: HallEventStatus[] = [
  'Requested',
  'ApprovedByManagement',
  'Confirmed',
  'InProgress',
  'Completed',
  'Rejected',
  'Expired',
  'Cancelled',
]

export function EventsListPage() {
  const { role, customerId } = useRole()
  const mine = role === 'Cliente'
  const [status, setStatus] = useState<HallEventStatus | ''>('')

  const events = useQuery({
    queryKey: ['hall-events', mine ? customerId : 'all', status],
    queryFn: () => hallEventsApi.list({ status, customerId: mine ? customerId : undefined }),
    refetchInterval: POLL_MS,
  })

  return (
    <div>
      <PageTitle title={mine ? 'Mis eventos' : 'Eventos'} description="El estado se actualiza solo: refleja la consistencia eventual entre módulos." />
      <div className="mb-4 flex items-center gap-2">
        <label htmlFor="status-filter" className="text-sm text-muted-foreground">
          Estado
        </label>
        <Select id="status-filter" className="w-60" value={status} onChange={(e) => setStatus(e.target.value as HallEventStatus | '')}>
          <option value="">Todos</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {eventStatusLabel(s)}
            </option>
          ))}
        </Select>
      </div>
      {events.data?.length === 0 && <Empty>No hay eventos con ese filtro.</Empty>}
      {events.data && events.data.length > 0 && (
        <Card className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="border-b border-border text-left text-xs text-muted-foreground uppercase">
              <tr>
                <th className="px-4 py-2">Fecha</th>
                <th className="px-4 py-2">Horario</th>
                {!mine && <th className="px-4 py-2">Cliente</th>}
                <th className="px-4 py-2">Personas</th>
                <th className="px-4 py-2 text-right">Total</th>
                <th className="px-4 py-2 text-right">Depósito</th>
                <th className="px-4 py-2">Estado</th>
              </tr>
            </thead>
            <tbody>
              {events.data.map((event) => (
                <tr key={event.id} className="border-b border-border last:border-0 hover:bg-muted/50">
                  <td className="px-4 py-2">
                    <Link className="font-medium text-primary hover:underline" to={`/events/${event.id}`}>
                      {formatDate(event.date)}
                    </Link>
                  </td>
                  <td className="px-4 py-2 tabular-nums">{formatRange(event.start, event.end)}</td>
                  {!mine && <td className="px-4 py-2">{event.customerName}</td>}
                  <td className="px-4 py-2">{event.attendance}</td>
                  <td className="px-4 py-2 text-right">
                    <Money value={event.total} />
                  </td>
                  <td className="px-4 py-2 text-right text-muted-foreground">
                    <Money value={event.depositAccredited} /> / <Money value={event.depositRequired} />
                  </td>
                  <td className="px-4 py-2">
                    <StatusBadge status={event.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </div>
  )
}
