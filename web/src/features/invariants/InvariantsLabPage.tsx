import { useQuery } from '@tanstack/react-query'
import { CheckCircle2, Database, Info, ShieldCheck, XCircle } from 'lucide-react'
import { useState } from 'react'
import { ApiProblem } from '@/api/client'
import { describeError } from '@/api/hooks'
import { demoApi } from '@/api/resources'
import type { InvariantEntry, ProbeResult } from '@/api/types'
import { Button } from '@/components/ui/button'
import { Card, CardBody, PageTitle } from '@/components/ui/card'

type Outcome = { state: 'running' } | { state: 'done'; result: ProbeResult } | { state: 'failed'; message: string }

/** A probe protects the invariant when the domain rejected it, or when the case cannot be attempted by design. */
function isProtected(outcome: Outcome | undefined): boolean {
  return outcome?.state === 'done' && (outcome.result.rejected || !outcome.result.attempted)
}

export function InvariantsLabPage() {
  const catalog = useQuery({ queryKey: ['invariants'], queryFn: demoApi.invariants, staleTime: 5 * 60_000 })
  const [outcomes, setOutcomes] = useState<Record<string, Outcome>>({})
  const [runningAll, setRunningAll] = useState(false)

  const probe = async (id: string) => {
    setOutcomes((current) => ({ ...current, [id]: { state: 'running' } }))
    try {
      const result = await demoApi.probe(id)
      setOutcomes((current) => ({ ...current, [id]: { state: 'done', result } }))
    } catch (error) {
      const message = error instanceof ApiProblem || error instanceof Error ? describeError(error) : 'Falló la sonda.'
      setOutcomes((current) => ({ ...current, [id]: { state: 'failed', message } }))
    }
  }

  const probeAll = async () => {
    setRunningAll(true)
    for (const invariant of catalog.data ?? []) await probe(invariant.id)
    setRunningAll(false)
  }

  const groups = Object.entries(
    (catalog.data ?? []).reduce<Record<string, InvariantEntry[]>>((acc, invariant) => {
      ;(acc[invariant.owner] ??= []).push(invariant)
      return acc
    }, {}),
  )
  const total = catalog.data?.length ?? 0
  const attempted = Object.keys(outcomes).length
  const protectedCount = Object.values(outcomes).filter(isProtected).length

  return (
    <div>
      <PageTitle
        title="Laboratorio de invariantes"
        description="Cada fila intenta violar una regla con el caso de uso real. La prueba se ejecuta en una transacción que siempre se revierte: la demo no cambia."
        action={
          <div className="flex items-center gap-3">
            {attempted > 0 && (
              <span
                aria-live="polite"
                className={`rounded-md px-3 py-1.5 text-sm font-semibold ${protectedCount === total && attempted === total ? 'bg-emerald-100 text-emerald-800' : 'bg-muted'}`}
              >
                {protectedCount}/{total} protegidas
              </span>
            )}
            <Button disabled={runningAll || total === 0} onClick={() => void probeAll()}>
              <ShieldCheck className="h-4 w-4" /> {runningAll ? 'Probando…' : 'Probar todas'}
            </Button>
          </div>
        }
      />
      <div className="space-y-5">
        {groups.map(([owner, invariants]) => (
          <Card key={owner}>
            <div className="border-b border-border bg-muted/50 px-4 py-2 text-sm font-semibold">{owner}</div>
            <div className="divide-y divide-border">
              {invariants.map((invariant) => (
                <Row key={invariant.id} invariant={invariant} outcome={outcomes[invariant.id]} onProbe={() => void probe(invariant.id)} />
              ))}
            </div>
          </Card>
        ))}
      </div>
    </div>
  )
}

function Row({ invariant, outcome, onProbe }: { invariant: InvariantEntry; outcome?: Outcome; onProbe: () => void }) {
  return (
    <CardBody className="grid gap-3 md:grid-cols-[1fr_auto]">
      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="rounded bg-slate-800 px-1.5 py-0.5 font-mono text-xs text-white">{invariant.id}</span>
          <span className="font-mono text-xs text-muted-foreground">{invariant.code}</span>
          {invariant.databaseEnforced && (
            <span title="Reforzada además en la base de datos (CHECK / EXCLUDE / índice único)" className="flex items-center gap-1 text-xs text-sky-700">
              <Database className="h-3.5 w-3.5" /> BD
            </span>
          )}
        </div>
        <p className="text-sm">{invariant.description}</p>
        <Result outcome={outcome} />
      </div>
      <div className="flex items-start">
        <Button variant="outline" size="sm" disabled={outcome?.state === 'running'} onClick={onProbe}>
          Intentar violar
        </Button>
      </div>
    </CardBody>
  )
}

function Result({ outcome }: { outcome?: Outcome }) {
  if (!outcome) return null
  if (outcome.state === 'running') return <p className="text-sm text-muted-foreground">Probando…</p>
  if (outcome.state === 'failed') {
    return (
      <p className="flex gap-2 rounded-md bg-red-50 p-2 text-sm text-red-900">
        <XCircle className="mt-0.5 h-4 w-4 shrink-0" /> {outcome.message}
      </p>
    )
  }
  const { result } = outcome
  if (result.rejected) {
    return (
      <div className="rounded-md bg-emerald-50 p-2 text-sm text-emerald-900">
        <p className="flex items-center gap-2 font-medium">
          <CheckCircle2 className="h-4 w-4" /> Rechazada por el dominio
          <span className="font-mono text-xs font-normal">
            HTTP {result.statusCode} · {result.code}
          </span>
        </p>
        <p className="mt-1 text-xs text-emerald-800">Intento: {result.attempt}</p>
        <p className="mt-0.5">{result.message}</p>
      </div>
    )
  }
  return (
    <div className="rounded-md bg-sky-50 p-2 text-sm text-sky-900">
      <p className="flex items-center gap-2 font-medium">
        <Info className="h-4 w-4" /> {result.attempted ? 'No fue rechazada' : 'Protegida por diseño'}
      </p>
      <p className="mt-0.5">{result.message}</p>
    </div>
  )
}
