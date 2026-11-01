import { useQuery } from '@tanstack/react-query'
import { CalendarCheck, CalendarX } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useCatalog, useClock, useCommand, useCustomers } from '@/api/hooks'
import { quotesApi } from '@/api/resources'
import type { QuoteRequest } from '@/api/types'
import { ProblemAlert } from '@/components/ProblemAlert'
import { Button } from '@/components/ui/button'
import { Card, CardBody, CardHeader, PageTitle } from '@/components/ui/card'
import { Field, Input, Select } from '@/components/ui/form'
import { addDays, addHoursToTime } from '@/lib/format'
import { useActor, useDebounced } from '@/lib/hooks'
import { useRole } from '@/context/RoleContext'
import { PriceBreakdownView } from './PriceBreakdownCard'

export function QuoteFormPage() {
  const { role, customerId: roleCustomer } = useRole()
  const actor = useActor()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const clock = useClock()
  const catalog = useCatalog()
  const customers = useCustomers()

  // Reception quotes on behalf of a customer; a customer always quotes for themself.
  const [pickedCustomer, setPickedCustomer] = useState('')
  const customerId = role === 'Cliente' ? roleCustomer : pickedCustomer || customers.data?.[0]?.memberId || ''

  // The calendar can prefill date and start time (click on a free slot).
  const [date, setDate] = useState(params.get('date') ?? '')
  const [start, setStart] = useState(params.get('start') ?? '09:00')
  const [hours, setHours] = useState('4')
  const [attendance, setAttendance] = useState('40')
  const [equipment, setEquipment] = useState<Record<string, string>>({})
  const [catering, setCatering] = useState<string[]>([])

  const effectiveDate = date || (clock.data ? addDays(clock.data.todayLocal, 10) : '')
  const startTime = start.length === 5 ? `${start}:00` : start

  // Only the format is checked here: out-of-range values go to the API so its domain answer is visible (422).
  const request = useMemo<QuoteRequest | null>(() => {
    const durationHours = Number(hours)
    const people = Number(attendance)
    if (!customerId || !effectiveDate || !start || hours === '' || attendance === '') return null
    if (!Number.isInteger(durationHours) || !Number.isInteger(people)) return null
    const extraServices = [
      ...Object.entries(equipment)
        .filter(([, quantity]) => Number(quantity) > 0)
        .map(([code, quantity]) => ({ code, quantity: Number(quantity) })),
      ...catering.map((code) => ({ code })),
    ]
    return {
      customerId,
      date: effectiveDate,
      startTime,
      endTime: addHoursToTime(startTime, durationHours),
      attendance: people,
      extraServices,
    }
  }, [customerId, effectiveDate, start, startTime, hours, attendance, equipment, catering])

  const debounced = useDebounced(request)
  const preview = useQuery({
    queryKey: ['quote-preview', debounced],
    queryFn: () => quotesApi.preview(debounced as QuoteRequest),
    enabled: debounced !== null,
    retry: false,
    placeholderData: (previous) => previous,
  })

  const issue = useCommand(
    (body: QuoteRequest) => quotesApi.issue({ ...body, performedBy: actor }),
    { success: 'Cotización emitida.', onSuccess: () => navigate('/quotes') },
  )

  const assets = catalog.data?.venues[0]?.assets ?? []
  const menu = catalog.data?.cateringMenu ?? []

  return (
    <div>
      <PageTitle
        title="Cotizar"
        description="El precio y la disponibilidad se calculan en la API mientras completas el formulario."
      />
      <div className="grid gap-5 lg:grid-cols-[1fr_380px]">
        <Card>
          <CardHeader title="Datos del evento" />
          <CardBody>
            <form
              className="grid gap-4 sm:grid-cols-2"
              onSubmit={(e) => {
                e.preventDefault()
                if (request) issue.mutate(request)
              }}
            >
              {role !== 'Cliente' && (
                <div className="sm:col-span-2">
                  <Field label="Cliente" htmlFor="q-customer">
                    <Select id="q-customer" value={customerId} onChange={(e) => setPickedCustomer(e.target.value)}>
                      {customers.data?.map((c) => (
                        <option key={c.memberId} value={c.memberId}>
                          {c.fullName}
                        </option>
                      ))}
                    </Select>
                  </Field>
                </div>
              )}
              <Field label="Fecha" htmlFor="q-date">
                <Input id="q-date" type="date" value={effectiveDate} onChange={(e) => setDate(e.target.value)} required />
              </Field>
              <Field label="Hora de inicio" htmlFor="q-start">
                <Input id="q-start" type="time" value={start} onChange={(e) => setStart(e.target.value)} required />
              </Field>
              <Field label="Duración (horas)" htmlFor="q-hours" hint="Entre 1 y 8 horas.">
                <Input id="q-hours" type="number" value={hours} onChange={(e) => setHours(e.target.value)} required />
              </Field>
              <Field label="Aforo (personas)" htmlFor="q-attendance" hint="El salón admite hasta 80 personas.">
                <Input id="q-attendance" type="number" value={attendance} onChange={(e) => setAttendance(e.target.value)} required />
              </Field>

              <fieldset className="sm:col-span-2">
                <legend className="mb-1 text-sm font-medium">Equipos</legend>
                <div className="grid gap-2 sm:grid-cols-2">
                  {assets.map((asset) => (
                    <div key={asset.asset_code} className="flex items-center justify-between gap-2 rounded-md border border-border px-3 py-2">
                      <label htmlFor={`eq-${asset.asset_code}`} className="text-sm">
                        {asset.label}
                        <span className="block text-xs text-muted-foreground">
                          Bs {asset.fee_per_event.toFixed(2)} · stock {asset.units_in_stock}
                        </span>
                      </label>
                      <Input
                        id={`eq-${asset.asset_code}`}
                        type="number"
                        min={0}
                        className="w-20"
                        value={equipment[asset.asset_code] ?? ''}
                        placeholder="0"
                        onChange={(e) => setEquipment((current) => ({ ...current, [asset.asset_code]: e.target.value }))}
                      />
                    </div>
                  ))}
                </div>
              </fieldset>

              <fieldset className="sm:col-span-2">
                <legend className="mb-1 text-sm font-medium">Catering (por persona, según el aforo)</legend>
                <div className="grid gap-2 sm:grid-cols-2">
                  {menu.map((item) => (
                    <label key={item.sku} className="flex cursor-pointer items-center gap-2 rounded-md border border-border px-3 py-2 text-sm">
                      <input
                        type="checkbox"
                        checked={catering.includes(item.sku)}
                        onChange={(e) =>
                          setCatering((current) => (e.target.checked ? [...current, item.sku] : current.filter((s) => s !== item.sku)))
                        }
                      />
                      <span>
                        {item.title}
                        <span className="block text-xs text-muted-foreground">Bs {item.price_per_head.toFixed(2)} por persona</span>
                      </span>
                    </label>
                  ))}
                </div>
              </fieldset>

              <div className="sm:col-span-2">
                <ProblemAlert error={issue.error} />
              </div>
              <div className="sm:col-span-2">
                <Button type="submit" disabled={!request || issue.isPending}>
                  {issue.isPending ? 'Emitiendo…' : 'Emitir cotización'}
                </Button>
              </div>
            </form>
          </CardBody>
        </Card>

        <Card className="self-start">
          <CardHeader title="Desglose en vivo" description="Calculado por la API" />
          <CardBody className="space-y-3">
            {preview.error && <ProblemAlert error={preview.error} />}
            {!preview.error && preview.data && (
              <>
                <PriceBreakdownView price={preview.data.price} extras={preview.data.extraServices} />
                <Availability availability={preview.data.availability} />
              </>
            )}
            {!preview.error && !preview.data && <p className="text-sm text-muted-foreground">Completa el formulario para ver el precio.</p>}
          </CardBody>
        </Card>
      </div>
    </div>
  )
}

function Availability({ availability }: { availability: { isAvailable: boolean; reason: string | null; code: string | null } }) {
  return availability.isAvailable ? (
    <p className="flex items-center gap-2 rounded-md bg-emerald-50 p-2.5 text-sm text-emerald-800">
      <CalendarCheck className="h-4 w-4" /> El salón está disponible en esa franja.
    </p>
  ) : (
    <p className="flex items-start gap-2 rounded-md bg-amber-50 p-2.5 text-sm text-amber-900">
      <CalendarX className="mt-0.5 h-4 w-4 shrink-0" />
      <span>
        {availability.reason}
        {availability.code && <span className="mt-0.5 block font-mono text-xs">{availability.code}</span>}
      </span>
    </p>
  )
}
