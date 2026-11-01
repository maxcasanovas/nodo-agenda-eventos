import { useQuery } from '@tanstack/react-query'
import { CheckCircle2, Circle } from 'lucide-react'
import { Link } from 'react-router'
import { POLL_MS, useCommand } from '@/api/hooks'
import { externalApi, hallEventsApi } from '@/api/resources'
import type { HallEventListItem } from '@/api/types'
import { ProblemAlert } from '@/components/ProblemAlert'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardBody, CardHeader, Empty, PageTitle } from '@/components/ui/card'
import { formatDate, formatRange } from '@/lib/format'

export function OperationsPage() {
  const today = useQuery({ queryKey: ['today'], queryFn: hallEventsApi.today, refetchInterval: POLL_MS })

  return (
    <div>
      <PageTitle
        title="Eventos de hoy"
        description="Confirmados y en curso. Iniciar y finalizar dependen del horario: la API lo valida y explica si todavía no corresponde."
      />
      {today.data?.length === 0 && <Empty>No hay eventos para hoy. Avanza el reloj de demo hasta el día de un evento confirmado.</Empty>}
      <div className="space-y-4">
        {today.data?.map((event) => (
          <TodayCard key={event.id} event={event} />
        ))}
      </div>
    </div>
  )
}

function TodayCard({ event }: { event: HallEventListItem }) {
  const detail = useQuery({ queryKey: ['hall-event', event.id], queryFn: () => hallEventsApi.get(event.id), refetchInterval: POLL_MS })
  const credentials = useQuery({ queryKey: ['credentials'], queryFn: externalApi.credentials, refetchInterval: POLL_MS })
  const orders = useQuery({ queryKey: ['catering-orders'], queryFn: externalApi.cateringOrders, refetchInterval: POLL_MS })

  const start = useCommand(() => hallEventsApi.start(event.id), { success: 'Uso del salón iniciado.' })
  const complete = useCommand(() => hallEventsApi.complete(event.id), { success: 'Evento finalizado: se liquida el saldo restante.' })

  const equipment = detail.data?.extraServices.filter((s) => s.type === 'OwnEquipment') ?? []
  const cateringLines = detail.data?.extraServices.filter((s) => s.type === 'ExternalCatering') ?? []
  const credential = credentials.data?.find((c) => c.eventRef === event.id && c.status === 'ACTIVE')
  const order = orders.data?.find((o) => o.eventRef === event.id)

  return (
    <Card>
      <CardHeader
        title={
          <Link to={`/events/${event.id}`} className="hover:underline">
            {event.customerName} · {formatDate(event.date)} · {formatRange(event.start, event.end)}
          </Link>
        }
        description={`${event.attendance} personas`}
        action={<StatusBadge status={event.status} />}
      />
      <CardBody className="space-y-4">
        <ul className="space-y-1.5 text-sm">
          <Check done={equipment.length > 0 || detail.data?.extraServicesAssigned === true}>
            Equipos asignados: {equipment.length > 0 ? equipment.map((e) => `${e.description} × ${e.quantity}`).join(', ') : 'ninguno solicitado'}
          </Check>
          <Check done={cateringLines.length === 0 || !!order}>
            Catering:{' '}
            {cateringLines.length === 0
              ? 'no solicitado'
              : order
                ? `orden enviada (${order.lines.map((l) => `${l.title} × ${l.headcount}`).join(', ')})`
                : 'esperando la orden del proveedor'}
          </Check>
          <Check done={!!credential}>
            Credencial de acceso: {credential ? <span className="font-mono">PIN {credential.pin} · aforo {credential.capacity}</span> : 'pendiente'}
          </Check>
        </ul>
        <ProblemAlert error={start.error ?? complete.error} />
        <div className="flex gap-2">
          <Button disabled={event.status !== 'Confirmed' || start.isPending} onClick={() => start.mutate(undefined)}>
            Iniciar uso del salón
          </Button>
          <Button variant="warning" disabled={event.status !== 'InProgress' || complete.isPending} onClick={() => complete.mutate(undefined)}>
            Finalizar evento
          </Button>
        </div>
      </CardBody>
    </Card>
  )
}

function Check({ done, children }: { done: boolean; children: React.ReactNode }) {
  return (
    <li className="flex items-start gap-2">
      {done ? <CheckCircle2 className="mt-0.5 h-4 w-4 text-emerald-600" /> : <Circle className="mt-0.5 h-4 w-4 text-muted-foreground" />}
      <span>{children}</span>
    </li>
  )
}
