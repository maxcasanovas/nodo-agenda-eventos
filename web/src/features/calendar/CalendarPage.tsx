import type { DateSelectArg, DatesSetArg, EventClickArg, EventInput } from '@fullcalendar/core'
import esLocale from '@fullcalendar/core/locales/es'
import interactionPlugin from '@fullcalendar/interaction'
import FullCalendar from '@fullcalendar/react'
import timeGridPlugin from '@fullcalendar/timegrid'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { POLL_MS, useClock } from '@/api/hooks'
import { hallScheduleApi } from '@/api/resources'
import type { CalendarBlock } from '@/api/types'
import { Card, CardBody, PageTitle } from '@/components/ui/card'
import { useRole } from '@/context/RoleContext'
import { HALL_ID } from '@/lib/hooks'

const AMBER = '#f59e0b'
const GREEN = '#16a34a'

function toEvents(blocks: CalendarBlock[]): EventInput[] {
  return blocks.flatMap((block) => {
    const at = (time: string) => `${block.date}T${time}`
    const color = block.kind === 'Definitive' ? GREEN : AMBER
    return [
      {
        id: block.blockId,
        title: `${block.customerName ?? 'Evento'} · ${block.kind === 'Definitive' ? 'definitivo' : 'provisional'}`,
        start: at(block.start),
        end: at(block.end),
        backgroundColor: color,
        borderColor: color,
        extendedProps: { eventId: block.eventId },
      },
      // Setup and cleaning hour before and after: grey striped background events.
      { id: `${block.blockId}-pre`, start: at(block.bufferStart), end: at(block.start), display: 'background', classNames: ['buffer'] },
      { id: `${block.blockId}-post`, start: at(block.end), end: at(block.bufferEnd), display: 'background', classNames: ['buffer'] },
    ]
  })
}

export function CalendarPage() {
  const navigate = useNavigate()
  const { role } = useRole()
  const clock = useClock()
  const [range, setRange] = useState<{ from: string; to: string } | null>(null)
  const canQuote = role === 'Cliente' || role === 'Recepcion'

  const blocks = useQuery({
    queryKey: ['calendar', range],
    queryFn: () => hallScheduleApi.calendar(HALL_ID, range!.from, range!.to),
    enabled: range !== null,
    refetchInterval: POLL_MS,
    placeholderData: (previous) => previous,
  })
  const events = useMemo(() => toEvents(blocks.data ?? []), [blocks.data])

  const onDatesSet = (arg: DatesSetArg) => {
    const from = arg.startStr.slice(0, 10)
    const to = new Date(arg.end.getTime() - 1).toISOString().slice(0, 10)
    setRange((current) => (current?.from === from && current.to === to ? current : { from, to }))
  }

  const onEventClick = (arg: EventClickArg) => {
    const eventId = arg.event.extendedProps.eventId as string | undefined
    if (eventId) navigate(`/events/${eventId}`)
  }

  const onSelect = (arg: DateSelectArg) => {
    if (!canQuote) return
    const [date, time] = arg.startStr.split('T') as [string, string?]
    navigate(`/quotes/new?date=${date}&start=${(time ?? '09:00').slice(0, 5)}`)
  }

  return (
    <div>
      <PageTitle
        title="Calendario del salón"
        description={
          canQuote
            ? 'Ámbar: provisional · Verde: definitivo · Gris rayado: montaje y limpieza. Haz clic en un hueco libre para cotizar.'
            : 'Ámbar: provisional · Verde: definitivo · Gris rayado: montaje y limpieza.'
        }
      />
      <Card>
        <CardBody>
          <FullCalendar
            plugins={[timeGridPlugin, interactionPlugin]}
            locales={[esLocale]}
            locale="es"
            initialView="timeGridWeek"
            // Follow the demo clock: moving it moves "today" on the calendar.
            now={clock.data?.localNow}
            headerToolbar={{ left: 'prev,next today', center: 'title', right: 'timeGridWeek,timeGridDay' }}
            slotMinTime="07:00:00"
            slotMaxTime="23:00:00"
            allDaySlot={false}
            height="auto"
            nowIndicator
            selectable={canQuote}
            selectMirror
            events={events}
            datesSet={onDatesSet}
            eventClick={onEventClick}
            select={onSelect}
            timeZone="local"
          />
        </CardBody>
      </Card>
    </div>
  )
}
