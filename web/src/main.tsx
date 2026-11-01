import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { Toaster } from 'sonner'
import { App } from './App'
import { RoleProvider } from './context/RoleContext'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RoleProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
        {/* Bottom: the demo clock lives in the header and must stay clickable while a toast is showing. */}
        <Toaster position="bottom-right" richColors closeButton />
      </RoleProvider>
    </QueryClientProvider>
  </StrictMode>,
)
