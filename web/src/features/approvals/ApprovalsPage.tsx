import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { POLL_MS, useClock, useCommand } from '@/api/hooks'
import { hallEventsApi } from '@/api/resources'
import type { HallEventListItem } from '@/api/types'
import { Money } from '@/components/Money'
import { ProblemAlert } from '@/components/ProblemAlert'
import { Button } from '@/components/ui/button'
import { Card, CardBody, CardHeader, Empty, PageTitle } from '@/components/ui/card'
import { Modal } from '@/components/ui/dialog'
import { Field, Input, Textarea } from '@/components/ui/form'
import { formatDate, formatRange, formatRemaining } from '@/lib/format'
import { useActor } from '@/lib/hooks'

export function ApprovalsPage() {
  const clock = useClock()
  const pending = useQuery({ queryKey: ['pending-approvals'], queryFn: hallEventsApi.pendingApprovals, refetchInterval: POLL_MS })

  return (
    <div>
      <PageTitle
        title="Bandeja de aprobaciones"
        description="Solicitudes que esperan decisión, ordenadas por plazo. Si no se responden dentro del día, vencen."
      />
      {pending.data?.length === 0 && <Empty>No hay solicitudes pendientes.</Empty>}
      <div className="space-y-4">
        {pending.data
          ?.slice()
          .sort((a, b) => a.decisionDeadlineUtc.localeCompare(b.decisionDeadlineUtc))
          .map((event) => (
            <ApprovalCard key={event.id} event={event} nowUtc={clock.data?.utcNow} />
          ))}
      </div>
    </div>
  )
}

function ApprovalCard({ event, nowUtc }: { event: HallEventListItem; nowUtc?: string }) {
  const actor = useActor()
  const [percentage, setPercentage] = useState(30)
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')

  const approve = useCommand((pct: number) => hallEventsApi.approve(event.id, pct, actor), {
    success: 'Solicitud aprobada: el cliente ya puede pagar el depósito.',
  })
  const reject = useCommand((text: string) => hallEventsApi.reject(event.id, text, actor), {
    success: 'Solicitud rechazada.',
    onSuccess: () => setRejecting(false),
  })

  return (
    <Card>
      <CardHeader
        title={
          <Link to={`/events/${event.id}`} className="hover:underline">
            {event.customerName} · {formatDate(event.date)} · {formatRange(event.start, event.end)}
          </Link>
        }
        description={`${event.attendance} personas`}
        action={
          <span className="rounded-md bg-amber-100 px-2 py-1 text-xs font-medium text-amber-900">
            Responder en {nowUtc ? formatRemaining(event.decisionDeadlineUtc, nowUtc) : '…'}
          </span>
        }
      />
      <CardBody className="space-y-4">
        <p className="text-sm">
          Total del evento: <Money value={event.total} className="font-semibold" />
        </p>
        <div>
          <label htmlFor={`pct-${event.id}`} className="mb-1 block text-sm font-medium">
            Depósito de garantía: {percentage} % ≈ <Money value={(event.total * percentage) / 100} />
          </label>
          <div className="flex items-center gap-3">
            <input
              id={`pct-${event.id}`}
              type="range"
              min={20}
              max={50}
              value={Math.min(50, Math.max(20, percentage))}
              onChange={(e) => setPercentage(Number(e.target.value))}
              className="w-full accent-teal-700"
            />
            {/* The slider suggests the 20–50 % range, but any number can be typed: the domain decides (INV-02). */}
            <Input
              aria-label="Porcentaje del depósito"
              type="number"
              className="w-20"
              value={percentage}
              onChange={(e) => setPercentage(Number(e.target.value))}
            />
          </div>
          <div className="flex justify-between text-xs text-muted-foreground">
            <span>20 %</span>
            <span>50 %</span>
          </div>
        </div>
        <ProblemAlert error={approve.error} />
        <div className="flex gap-2">
          <Button disabled={approve.isPending} onClick={() => approve.mutate(percentage)}>
            Aprobar con {percentage} %
          </Button>
          <Button variant="danger" onClick={() => setRejecting(true)}>
            Rechazar
          </Button>
        </div>
      </CardBody>

      <Modal open={rejecting} onOpenChange={setRejecting} title="Rechazar solicitud" description="El motivo es obligatorio y se le muestra al cliente.">
        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault()
            reject.mutate(reason)
          }}
        >
          <Field label="Motivo del rechazo" htmlFor={`reason-${event.id}`}>
            <Textarea id={`reason-${event.id}`} value={reason} onChange={(e) => setReason(e.target.value)} />
          </Field>
          <ProblemAlert error={reject.error} />
          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={() => setRejecting(false)}>
              Volver
            </Button>
            <Button type="submit" variant="danger" disabled={reject.isPending}>
              Rechazar solicitud
            </Button>
          </div>
        </form>
      </Modal>
    </Card>
  )
}
