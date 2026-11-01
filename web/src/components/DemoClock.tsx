import { Clock, Play, RotateCcw } from 'lucide-react'
import { useClock, useCommand } from '@/api/hooks'
import { demoApi } from '@/api/resources'
import { formatClock } from '@/lib/format'
import { Button } from './ui/button'

/** Moving the demo clock also runs the expiration jobs, so quotes and deposits expire right away on screen. */
async function advanceAndRun(step: { days: number; hours: number }) {
  await demoApi.advance(step.days, step.hours)
  return demoApi.runJobs()
}

export function DemoClock() {
  const clock = useClock()
  const advance = useCommand(advanceAndRun)
  const reset = useCommand(demoApi.reset, { success: 'Reloj restablecido.' })
  const jobs = useCommand(demoApi.runJobs, {
    success: (results) => `Jobs ejecutados: ${results.map((r) => `${r.job} (${r.changed})`).join(', ')}`,
  })
  const busy = advance.isPending || reset.isPending || jobs.isPending

  return (
    <div className="flex flex-wrap items-center gap-2" aria-label="Reloj de demo">
      <div className="flex items-center gap-1.5 rounded-md bg-muted px-2.5 py-1.5 text-sm">
        <Clock className="h-4 w-4 text-primary" />
        <span className="font-medium tabular-nums">{clock.data ? formatClock(clock.data.localNow) : '…'}</span>
        {clock.data && clock.data.offsetHours !== 0 && (
          <span className="text-xs text-amber-700">(+{Math.round(clock.data.offsetHours)} h simuladas)</span>
        )}
      </div>
      <Button size="sm" variant="outline" disabled={busy} onClick={() => advance.mutate({ days: 0, hours: 1 })}>
        +1 h
      </Button>
      <Button size="sm" variant="outline" disabled={busy} onClick={() => advance.mutate({ days: 1, hours: 0 })}>
        +1 día
      </Button>
      <Button size="sm" variant="outline" disabled={busy} onClick={() => advance.mutate({ days: 4, hours: 0 })}>
        +4 días
      </Button>
      <Button size="sm" variant="ghost" disabled={busy} onClick={() => reset.mutate(undefined)}>
        <RotateCcw className="h-3.5 w-3.5" /> Restablecer
      </Button>
      <Button size="sm" variant="ghost" disabled={busy} onClick={() => jobs.mutate(undefined)}>
        <Play className="h-3.5 w-3.5" /> Ejecutar jobs
      </Button>
    </div>
  )
}
