import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useClock, useCommand } from '@/api/hooks'
import { quotesApi } from '@/api/resources'
import type { Quote } from '@/api/types'
import { QuoteStatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardBody, CardHeader, Empty, PageTitle } from '@/components/ui/card'
import { useRole } from '@/context/RoleContext'
import { formatDate, formatRange, formatRemaining } from '@/lib/format'
import { useActor } from '@/lib/hooks'
import { PriceBreakdownView } from './PriceBreakdownCard'

export function QuotesListPage() {
  const { role, customerId } = useRole()
  const actor = useActor()
  const clock = useClock()
  const mine = role === 'Cliente'

  const quotes = useQuery({
    queryKey: ['quotes', mine ? customerId : 'all'],
    queryFn: () => quotesApi.list(mine ? { customerId } : {}),
    refetchInterval: 2500,
  })
  const accept = useCommand((id: string) => quotesApi.accept(id, actor), {
    success: 'Cotización aceptada: se creó la solicitud de evento.',
  })

  return (
    <div>
      <PageTitle
        title={mine ? 'Mis cotizaciones' : 'Cotizaciones'}
        description="Una cotización vigente puede aceptarse hasta su vencimiento; al aceptarla nace la solicitud de evento."
        action={
          <Link to="/quotes/new">
            <Button>Nueva cotización</Button>
          </Link>
        }
      />
      {quotes.data?.length === 0 && <Empty>No hay cotizaciones todavía.</Empty>}
      <div className="space-y-4">
        {quotes.data?.map((quote) => (
          <QuoteCard
            key={quote.id}
            quote={quote}
            nowUtc={clock.data?.utcNow}
            showCustomer={!mine}
            accepting={accept.isPending && accept.variables === quote.id}
            onAccept={() => accept.mutate(quote.id)}
          />
        ))}
      </div>
    </div>
  )
}

function QuoteCard({
  quote,
  nowUtc,
  showCustomer,
  accepting,
  onAccept,
}: {
  quote: Quote
  nowUtc?: string
  showCustomer: boolean
  accepting: boolean
  onAccept: () => void
}) {
  // Display only: the API decides at accept time. The clock tells whether the validity already ran out.
  const lapsed = quote.status === 'Issued' && nowUtc !== undefined && new Date(quote.expiresAtUtc) <= new Date(nowUtc)
  const status = lapsed ? 'Expired' : quote.status

  return (
    <Card>
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            {formatDate(quote.date)} · {formatRange(quote.start, quote.end)}
            <QuoteStatusBadge status={status} />
          </span>
        }
        description={`${showCustomer ? `${quote.customerName ?? 'Cliente'} · ` : ''}${quote.attendance} personas`}
        action={
          quote.status === 'Issued' && (
            <div className="flex items-center gap-3">
              <span className="text-sm text-muted-foreground">
                {lapsed || !nowUtc ? 'Vigencia agotada' : `Vence en ${formatRemaining(quote.expiresAtUtc, nowUtc)}`}
              </span>
              <Button disabled={lapsed || accepting} onClick={onAccept}>
                Aceptar
              </Button>
            </div>
          )
        }
      />
      <CardBody className="max-w-md">
        <PriceBreakdownView price={quote.price} extras={quote.extraServices} />
      </CardBody>
    </Card>
  )
}
