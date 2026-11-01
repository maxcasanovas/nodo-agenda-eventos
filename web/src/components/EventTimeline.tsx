import type { HistoryEntry } from '@/api/types'
import { formatInstant } from '@/lib/format'
import { StatusBadge } from './StatusBadge'

export function EventTimeline({ history }: { history: HistoryEntry[] }) {
  return (
    <ol className="relative space-y-4 border-l border-border pl-5">
      {history.map((entry, index) => (
        <li key={`${entry.status}-${index}`} className="relative">
          <span className="absolute -left-[26px] top-1.5 h-2.5 w-2.5 rounded-full bg-primary ring-4 ring-card" />
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={entry.status} />
            <span className="text-xs text-muted-foreground">
              {formatInstant(entry.occurredAtUtc)} · {entry.actor}
            </span>
          </div>
          <p className="mt-1 text-sm">{entry.note}</p>
        </li>
      ))}
    </ol>
  )
}
