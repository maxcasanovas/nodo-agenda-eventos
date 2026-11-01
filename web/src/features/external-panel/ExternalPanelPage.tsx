import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { POLL_MS, useCommand } from '@/api/hooks'
import { demoApi, externalApi } from '@/api/resources'
import type { PaymentLink } from '@/api/types'
import { Money } from '@/components/Money'
import { Button } from '@/components/ui/button'
import { Card, CardBody, Empty, PageTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/form'
import { Tabs } from '@/components/ui/tabs'
import { formatBs, formatBsFromCents, formatInstant } from '@/lib/format'

export function ExternalPanelPage() {
  return (
    <div>
      <PageTitle
        title="Sistemas externos"
        description="Contextos simulados que reaccionan a los eventos de integración. Sus efectos aparecen uno o dos segundos después del comando."
      />
      <Tabs
        items={[
          { value: 'gateway', label: 'Pasarela', content: <GatewayTab /> },
          { value: 'notifications', label: 'Notificaciones', content: <NotificationsTab /> },
          { value: 'access', label: 'Control de acceso', content: <AccessTab /> },
          { value: 'billing', label: 'Facturación', content: <BillingTab /> },
          { value: 'catering', label: 'Catering', content: <CateringTab /> },
          { value: 'events', label: 'Eventos de integración', content: <IntegrationEventsTab /> },
        ]}
      />
    </div>
  )
}

// ---- Pasarela ----

const LINK_TONE: Record<string, string> = {
  PENDING: 'bg-sky-100 text-sky-800',
  PARTIALLY_PAID: 'bg-amber-100 text-amber-800',
  PAID: 'bg-emerald-100 text-emerald-800',
  DECLINED: 'bg-red-100 text-red-800',
}

function GatewayTab() {
  const links = useQuery({ queryKey: ['payment-links'], queryFn: externalApi.paymentLinks, refetchInterval: POLL_MS })
  const sorted = links.data?.slice().sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
  if (sorted?.length === 0) return <Empty>No hay links de pago. Aparecen cuando Dirección aprueba una solicitud.</Empty>
  return (
    <div className="space-y-3">
      {sorted?.map((link) => (
        <PaymentLinkCard key={link.linkId} link={link} />
      ))}
    </div>
  )
}

function PaymentLinkCard({ link }: { link: PaymentLink }) {
  const remainingCents = link.amountCents - link.paidCents
  const [amount, setAmount] = useState('')
  const open = remainingCents > 0 && link.status !== 'PAID'

  const pay = useCommand((args: { amount: number | null; decline: boolean }) => externalApi.pay(link.linkId, args.amount, args.decline), {
    success: (result) => {
      const ack = result.webhookResponse as { outcome?: string; eventStatus?: string } | null
      return `Webhook ${result.webhookStatusCode}: ${ack?.outcome ?? 'sin respuesta'}${ack?.eventStatus ? ` · evento ${ack.eventStatus}` : ''}`
    },
    onSuccess: () => setAmount(''),
  })

  return (
    <Card>
      <CardBody className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <p className="font-medium">{link.description}</p>
            <p className="font-mono text-xs text-muted-foreground">
              {link.linkId} · merchant_ref {link.merchantRef}
            </p>
          </div>
          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${LINK_TONE[link.status] ?? 'bg-muted'}`}>{link.status}</span>
        </div>
        <p className="text-sm">
          Pagado {formatBsFromCents(link.paidCents)} de {formatBsFromCents(link.amountCents)}
        </p>
        {open && (
          <div className="flex flex-wrap items-end gap-2">
            <div>
              <label htmlFor={`amount-${link.linkId}`} className="mb-1 block text-xs text-muted-foreground">
                Monto (Bs) — vacío para pagar el resto
              </label>
              <Input
                id={`amount-${link.linkId}`}
                type="number"
                step="0.01"
                className="w-44"
                placeholder={(remainingCents / 100).toFixed(2)}
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
              />
            </div>
            <Button disabled={pay.isPending} onClick={() => pay.mutate({ amount: amount === '' ? null : Number(amount), decline: false })}>
              Pagar
            </Button>
            <Button variant="danger" disabled={pay.isPending} onClick={() => pay.mutate({ amount: amount === '' ? null : Number(amount), decline: true })}>
              Rechazar pago
            </Button>
          </div>
        )}
        {link.attempts && link.attempts.length > 0 && (
          <ul className="space-y-0.5 text-xs text-muted-foreground">
            {link.attempts.map((attempt) => (
              <li key={attempt.txnId} className="font-mono">
                {formatInstant(attempt.atUtc)} · {attempt.txnId} · {formatBsFromCents(attempt.amountCents)} · {attempt.status}
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  )
}

// ---- Tablas simples ----

function Table({ head, children }: { head: string[]; children: React.ReactNode }) {
  return (
    <Card className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="border-b border-border text-left text-xs text-muted-foreground uppercase">
          <tr>
            {head.map((h) => (
              <th key={h} className="px-3 py-2">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </Card>
  )
}

function NotificationsTab() {
  const items = useQuery({ queryKey: ['notifications'], queryFn: externalApi.notifications, refetchInterval: POLL_MS })
  if (items.data?.length === 0) return <Empty>Sin notificaciones.</Empty>
  return (
    <Table head={['Hora', 'Para', 'Canal', 'Asunto', 'Mensaje']}>
      {items.data
        ?.slice()
        .sort((a, b) => b.sentAt.localeCompare(a.sentAt))
        .map((n) => (
          <tr key={n.id} className="border-b border-border align-top last:border-0">
            <td className="px-3 py-2 whitespace-nowrap">{formatInstant(n.sentAt)}</td>
            <td className="px-3 py-2">{n.to}</td>
            <td className="px-3 py-2">{n.channel}</td>
            <td className="px-3 py-2 font-medium">{n.subject}</td>
            <td className="px-3 py-2">{n.body}</td>
          </tr>
        ))}
    </Table>
  )
}

function AccessTab() {
  const items = useQuery({ queryKey: ['credentials'], queryFn: externalApi.credentials, refetchInterval: POLL_MS })
  if (items.data?.length === 0) return <Empty>Sin credenciales: se emiten al confirmarse un evento.</Empty>
  return (
    <div className="grid gap-3 md:grid-cols-2">
      {items.data?.map((c) => (
        <Card key={c.credentialId} className={c.status === 'REVOKED' ? 'opacity-60' : undefined}>
          <CardBody className="space-y-1 text-sm">
            <div className="flex items-center justify-between">
              <span className="font-mono text-xs text-muted-foreground">evento {c.eventRef.slice(0, 8)}</span>
              <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${c.status === 'ACTIVE' ? 'bg-emerald-100 text-emerald-800' : 'bg-red-100 text-red-800'}`}>
                {c.status === 'ACTIVE' ? 'Activa' : 'Revocada'}
              </span>
            </div>
            <p className="text-2xl font-semibold tracking-widest tabular-nums">PIN {c.pin}</p>
            <p>Aforo permitido: {c.capacity}</p>
            <p className="text-muted-foreground">
              Válida {formatInstant(c.validFromUtc)} → {formatInstant(c.validUntilUtc)}
            </p>
            <p className="rounded bg-muted p-2 font-mono text-xs break-all">{c.qrText}</p>
          </CardBody>
        </Card>
      ))}
    </div>
  )
}

function BillingTab() {
  const items = useQuery({ queryKey: ['ledger'], queryFn: externalApi.ledger, refetchInterval: POLL_MS })
  if (items.data?.length === 0) return <Empty>El libro está vacío.</Empty>
  return (
    <Table head={['Hora', 'Movimiento', 'Cuenta', 'Referencia', 'Monto', 'Nota']}>
      {items.data
        ?.slice()
        .sort((a, b) => b.atUtc.localeCompare(a.atUtc))
        .map((entry) => (
          <tr key={entry.entryId} className="border-b border-border align-top last:border-0">
            <td className="px-3 py-2 whitespace-nowrap">{formatInstant(entry.atUtc)}</td>
            <td className="px-3 py-2 font-medium">{entry.kind}</td>
            <td className="px-3 py-2 font-mono text-xs">{entry.account.slice(0, 8)}</td>
            <td className="px-3 py-2 font-mono text-xs">{entry.reference.slice(0, 8)}</td>
            <td className="px-3 py-2 text-right whitespace-nowrap">{formatBs(entry.amount)}</td>
            <td className="px-3 py-2">{entry.note}</td>
          </tr>
        ))}
    </Table>
  )
}

function CateringTab() {
  const items = useQuery({ queryKey: ['catering-orders'], queryFn: externalApi.cateringOrders, refetchInterval: POLL_MS })
  if (items.data?.length === 0) return <Empty>Sin órdenes de catering.</Empty>
  return (
    <div className="grid gap-3 md:grid-cols-2">
      {items.data?.map((order) => (
        <Card key={order.orderId}>
          <CardBody className="space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="font-mono text-xs text-muted-foreground">evento {order.eventRef.slice(0, 8)}</span>
              <span className="text-xs">{order.status}</span>
            </div>
            <ul>
              {order.lines.map((line) => (
                <li key={line.sku} className="flex justify-between">
                  <span>
                    {line.title} × {line.headcount}
                  </span>
                  <Money value={line.headcount * line.pricePerHead} />
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      ))}
    </div>
  )
}

function IntegrationEventsTab() {
  const items = useQuery({ queryKey: ['integration-events'], queryFn: () => demoApi.integrationEvents(100), refetchInterval: POLL_MS })
  if (items.data?.length === 0) return <Empty>Aún no se publicó ningún evento de integración.</Empty>
  return (
    <div className="space-y-2">
      {items.data?.map((entry) => (
        <details key={entry.id} className="rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="flex cursor-pointer flex-wrap items-center gap-x-3 gap-y-1">
            <span className="font-medium">{entry.type.split('.').pop()?.replace('IntegrationEvent', '')}</span>
            <span className="rounded bg-muted px-1.5 py-0.5 text-xs">{entry.module}</span>
            <span className="text-xs text-muted-foreground">{formatInstant(entry.occurredOnUtc)}</span>
            <span className={`text-xs ${entry.processedOnUtc ? 'text-emerald-700' : 'text-amber-700'}`}>
              {entry.processedOnUtc ? 'entregado' : 'pendiente'}
            </span>
            {entry.error && <span className="text-xs text-red-700">error: {entry.error}</span>}
          </summary>
          <pre className="mt-2 overflow-x-auto rounded bg-muted p-2 text-xs">{JSON.stringify(entry.content, null, 2)}</pre>
        </details>
      ))}
    </div>
  )
}
