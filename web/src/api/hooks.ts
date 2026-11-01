import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ApiProblem } from './client'
import { demoApi, externalApi } from './resources'

/** Event state changes through the outbox, so views that show it poll (eventual consistency made visible). */
export const POLL_MS = 2500

export function useClock() {
  return useQuery({ queryKey: ['clock'], queryFn: demoApi.clock, refetchInterval: 5000 })
}

export function useCustomers() {
  return useQuery({ queryKey: ['customers'], queryFn: externalApi.customers, staleTime: 60_000 })
}

export function useCatalog() {
  return useQuery({ queryKey: ['catalog'], queryFn: externalApi.catalog, staleTime: 60_000 })
}

export function describeError(error: unknown): string {
  if (error instanceof ApiProblem) return error.message
  if (error instanceof Error) return error.message
  return 'Ocurrió un error inesperado.'
}

/**
 * A command against the API. Failures show the domain's Spanish message in a toast (and are returned as `error`
 * so forms can show them inline); success refreshes every query so each view reflects the new state.
 */
export function useCommand<TArgs, TResult>(
  action: (args: TArgs) => Promise<TResult>,
  options: { success?: string | ((result: TResult) => string); onSuccess?: (result: TResult) => void } = {},
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: action,
    onSuccess: (result) => {
      const message = typeof options.success === 'function' ? options.success(result) : options.success
      if (message) toast.success(message)
      void queryClient.invalidateQueries()
      options.onSuccess?.(result)
    },
    onError: (error) => {
      const code = error instanceof ApiProblem && error.code ? ` (${error.code})` : ''
      toast.error(`${describeError(error)}${code}`)
    },
  })
}
