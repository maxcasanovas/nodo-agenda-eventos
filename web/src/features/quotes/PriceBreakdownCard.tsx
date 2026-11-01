import type { ExtraServiceLine, PriceBreakdown } from '@/api/types'
import { Money } from '@/components/Money'

/** Renders the price exactly as the API computed it: the front does no pricing. */
export function PriceBreakdownView({ price, extras }: { price: PriceBreakdown; extras: ExtraServiceLine[] }) {
  return (
    <dl className="space-y-1.5 text-sm">
      <Row label={`Tarifa base × ${price.hours} h`}>
        <Money value={price.baseHourlyRate * price.hours} />
      </Row>
      <Row label="Factor degresivo por horas">
        <span className="tabular-nums">× {price.rateFactor}</span>
      </Row>
      <Row label="Alquiler del salón">
        <Money value={price.hallRental} />
      </Row>
      {price.membershipDiscount > 0 && (
        <Row label={`Descuento de membresía (${price.membershipDiscountPercentage} %)`}>
          <span className="text-emerald-700">
            − <Money value={price.membershipDiscount} />
          </span>
        </Row>
      )}
      {extras.map((extra) => (
        <Row key={`${extra.type}-${extra.code}`} label={`${extra.name ?? extra.description ?? extra.code} × ${extra.quantity}`}>
          <Money value={extra.lineTotal} />
        </Row>
      ))}
      <div className="mt-2 flex items-center justify-between border-t border-border pt-2 text-base font-semibold">
        <dt>Total</dt>
        <dd>
          <Money value={price.total} />
        </dd>
      </div>
    </dl>
  )
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}
