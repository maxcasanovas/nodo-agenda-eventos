import type { HallEventStatus, QuoteStatus } from '@/api/types'
import { EVENT_STATUS, QUOTE_STATUS } from '@/lib/status'
import { cn } from '@/lib/utils'

function Pill({ label, tone }: { label: string; tone: string }) {
  return <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap', tone)}>{label}</span>
}

export function StatusBadge({ status }: { status: HallEventStatus }) {
  return <Pill {...(EVENT_STATUS[status] ?? { label: status, tone: 'bg-muted' })} />
}

export function QuoteStatusBadge({ status }: { status: QuoteStatus }) {
  return <Pill {...(QUOTE_STATUS[status] ?? { label: status, tone: 'bg-muted' })} />
}
