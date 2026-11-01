import { Tabs as TabsPrimitive } from 'radix-ui'
import type { ReactNode } from 'react'

export interface TabItem {
  value: string
  label: string
  content: ReactNode
}

export function Tabs({ items, defaultValue }: { items: TabItem[]; defaultValue?: string }) {
  return (
    <TabsPrimitive.Root defaultValue={defaultValue ?? items[0]?.value}>
      <TabsPrimitive.List className="mb-4 flex flex-wrap gap-1 border-b border-border">
        {items.map((item) => (
          <TabsPrimitive.Trigger
            key={item.value}
            value={item.value}
            className="-mb-px cursor-pointer border-b-2 border-transparent px-3 py-2 text-sm font-medium text-muted-foreground hover:text-foreground data-[state=active]:border-primary data-[state=active]:text-foreground"
          >
            {item.label}
          </TabsPrimitive.Trigger>
        ))}
      </TabsPrimitive.List>
      {items.map((item) => (
        <TabsPrimitive.Content key={item.value} value={item.value} className="focus-visible:outline-none">
          {item.content}
        </TabsPrimitive.Content>
      ))}
    </TabsPrimitive.Root>
  )
}
