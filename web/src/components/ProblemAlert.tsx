import { AlertTriangle } from 'lucide-react'
import { ApiProblem } from '@/api/client'
import { describeError } from '@/api/hooks'

/** Shows a failed API call inline: the domain's Spanish message plus its stable code. */
export function ProblemAlert({ error }: { error: unknown }) {
  if (!error) return null
  const code = error instanceof ApiProblem ? error.code : null
  const status = error instanceof ApiProblem ? error.status : null
  return (
    <div role="alert" className="flex gap-2 rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
      <div>
        <p>{describeError(error)}</p>
        {(code || status) && (
          <p className="mt-1 font-mono text-xs text-red-700">
            {status && `HTTP ${status}`} {code}
          </p>
        )}
      </div>
    </div>
  )
}
