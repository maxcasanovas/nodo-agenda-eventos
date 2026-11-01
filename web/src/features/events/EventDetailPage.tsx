import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { POLL_MS, useCommand } from '@/api/hooks'
import { hallEventsApi, hallScheduleApi } from '@/api/resources'
import type { HallEvent } from '@/api/types'
import { EventTimeline } from '@/components/EventTimeline'
import { Money } from '@/components/Money'
import { ProblemAlert } from '@/components/ProblemAlert'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardBody, CardHeader, PageTitle } from '@/components/ui/card'
import { Modal } from '@/components/ui/dialog'
import { Field, Input, Textarea } from '@/components/ui/form'
import { useRole } from '@/context/RoleContext'
import { durationHours, formatDate, formatInstant, formatRange, addHoursToTime } from '@/lib/format'
import { useActor } from '@/lib/hooks'

export function EventDetailPage() {
  const { id = '' } = useParams()
  const { role } = useRole()
  const [cancelOpen, setCancelOpen] = useState(false)
  const [rescheduleOpen, setRescheduleOpen] = useState(false)

  const event = useQuery({ queryKey: ['hall-event', id], queryFn: () => hallEventsApi.get(id), refetchInterval: POLL_MS })
  const credit = useQuery({
    queryKey: ['credit', event.data?.customerId],
    queryFn: () => hallEventsApi.credit(event.data!.customerId),
    enabled: !!event.data,
    refetchInterval: POLL_MS,
  })

  if (event.error) return <ProblemAlert error={event.error} />
  if (!event.data) return <p className="text-sm text-muted-foreground">Cargando…</p>
  const data = event.data
  const canManage = role === 'Cliente' || role === 'Recepcion'

  return (
    <div>
      <PageTitle
        title={`${formatDate(data.date)} · ${formatRange(data.start, data.end)}`}
        description={`${data.customerName ?? 'Cliente'} · ${data.attendance} personas`}
        action={
          <div className="flex items-center gap-2">
            <StatusBadge status={data.status} />
            {canManage && (data.status === 'Requested' || data.status === 'ApprovedByManagement' || data.status === 'Confirmed') && (
              <Button variant="danger" onClick={() => setCancelOpen(true)}>
                Cancelar
              </Button>
            )}
            {canManage && data.status === 'Cancelled' && (
              <Button onClick={() => setRescheduleOpen(true)}>Reprogramar</Button>
            )}
          </div>
        }
      />
      <Link to="/events" className="mb-3 inline-block text-sm text-primary hover:underline">
        ← Volver a la lista
      </Link>

      <div className="grid gap-5 lg:grid-cols-2">
        <Card>
          <CardHeader title="Línea de tiempo" />
          <CardBody>
            <EventTimeline history={data.history} />
          </CardBody>
        </Card>

        <div className="space-y-5">
          <DepositCard event={data} />
          <Card>
            <CardHeader title="Servicios extra" />
            <CardBody>
              {data.extraServices.length === 0 ? (
                <p className="text-sm text-muted-foreground">Sin servicios extra.</p>
              ) : (
                <ul className="space-y-1 text-sm">
                  {data.extraServices.map((service) => (
                    <li key={`${service.type}-${service.code}`} className="flex justify-between">
                      <span>
                        {service.description} × {service.quantity}
                        <span className="ml-2 text-xs text-muted-foreground">
                          {service.type === 'ExternalCatering' ? 'catering externo' : 'equipo propio'}
                        </span>
                      </span>
                      <Money value={service.lineTotal} />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Saldo a favor del cliente" description="Las cancelaciones no devuelven dinero: se convierten en saldo." />
            <CardBody>
              <Money value={credit.data?.balance ?? 0} className="text-xl font-semibold" />
              {credit.data && credit.data.movements.length > 0 && (
                <ul className="mt-2 space-y-1 text-xs text-muted-foreground">
                  {credit.data.movements.map((m, index) => (
                    <li key={index}>
                      {formatInstant(m.occurredAtUtc)} · {m.type === 'Granted' ? '+' : '−'}
                      <Money value={m.amount} /> · {m.reason}
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>
      </div>

      <CancelDialog event={data} open={cancelOpen} onOpenChange={setCancelOpen} />
      <RescheduleDialog event={data} open={rescheduleOpen} onOpenChange={setRescheduleOpen} />
    </div>
  )
}

function DepositCard({ event }: { event: HallEvent }) {
  const deposit = event.deposit
  const percent = deposit && deposit.required > 0 ? Math.min(100, (deposit.accredited / deposit.required) * 100) : 0
  return (
    <Card>
      <CardHeader title="Depósito de garantía" description={deposit ? `${deposit.percentage} % del total` : 'Se define cuando Dirección aprueba'} />
      <CardBody className="space-y-2">
        {deposit ? (
          <>
            <div
              role="progressbar"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={Math.round(percent)}
              aria-label="Depósito acreditado"
              className="h-3 overflow-hidden rounded-full bg-muted"
            >
              <div className="h-full bg-primary transition-all" style={{ width: `${percent}%` }} />
            </div>
            <div className="flex justify-between text-sm">
              <span>
                Acreditado <Money value={deposit.accredited} />
              </span>
              <span>
                Requerido <Money value={deposit.required} />
              </span>
            </div>
            {!deposit.isCovered && event.depositDeadlineUtc && (
              <p className="text-xs text-amber-700">Plazo para cubrirlo: {formatInstant(event.depositDeadlineUtc)}</p>
            )}
          </>
        ) : (
          <p className="text-sm text-muted-foreground">
            Total del evento: <Money value={event.total} />
          </p>
        )}
      </CardBody>
    </Card>
  )
}

function CancelDialog({ event, open, onOpenChange }: { event: HallEvent; open: boolean; onOpenChange: (open: boolean) => void }) {
  const actor = useActor()
  const [reason, setReason] = useState('')
  const cancel = useCommand((text: string) => hallEventsApi.cancel(event.id, text, actor), {
    success: 'Evento cancelado: lo acreditado quedó como saldo a favor.',
    onSuccess: () => onOpenChange(false),
  })

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Cancelar evento"
      description="No hay reembolso en dinero: lo acreditado pasa íntegro a saldo a favor del cliente, que puede usarse para reprogramar."
    >
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault()
          cancel.mutate(reason)
        }}
      >
        <Field label="Motivo" htmlFor="cancel-reason">
          <Textarea id="cancel-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>
        <ProblemAlert error={cancel.error} />
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Volver
          </Button>
          <Button type="submit" variant="danger" disabled={cancel.isPending}>
            Confirmar cancelación
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function RescheduleDialog({ event, open, onOpenChange }: { event: HallEvent; open: boolean; onOpenChange: (open: boolean) => void }) {
  const actor = useActor()
  const [date, setDate] = useState('')
  const [start, setStart] = useState(event.start.slice(0, 5))
  const startTime = `${start}:00`
  const length = durationHours(event.start, event.end)

  // The event keeps its duration (the API enforces it); we only ask the calendar if the new slot is free.
  const availability = useQuery({
    queryKey: ['reschedule-availability', event.hallId, date, startTime, length],
    queryFn: () => hallScheduleApi.availability(event.hallId, date, startTime, addHoursToTime(startTime, length)),
    enabled: open && date !== '' && start !== '',
    retry: false,
  })
  const reschedule = useCommand((args: { date: string; start: string }) => hallEventsApi.reschedule(event.id, args.date, args.start, actor), {
    success: (result) =>
      result.status === 'Confirmed'
        ? 'Reprogramado: el saldo a favor cubrió el depósito y el evento quedó confirmado.'
        : 'Reprogramado: falta cubrir el resto del depósito.',
    onSuccess: () => onOpenChange(false),
  })

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Reprogramar con saldo a favor"
      description={`Se mantiene la duración (${length} h) y se aplica el saldo a favor del cliente al depósito.`}
    >
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault()
          reschedule.mutate({ date, start: startTime })
        }}
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Nueva fecha" htmlFor="rs-date">
            <Input id="rs-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
          </Field>
          <Field label="Nueva hora de inicio" htmlFor="rs-start">
            <Input id="rs-start" type="time" value={start} onChange={(e) => setStart(e.target.value)} required />
          </Field>
        </div>
        {availability.data && (
          <p className={availability.data.isAvailable ? 'text-sm text-emerald-700' : 'text-sm text-amber-800'}>
            {availability.data.isAvailable ? 'El salón está libre en esa franja.' : availability.data.reason}
          </p>
        )}
        <ProblemAlert error={reschedule.error} />
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Volver
          </Button>
          <Button type="submit" disabled={reschedule.isPending}>
            Reprogramar
          </Button>
        </div>
      </form>
    </Modal>
  )
}
