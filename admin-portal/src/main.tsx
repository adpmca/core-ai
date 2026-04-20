import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { ThemeProvider } from './components/theme-provider.tsx'
import { Toaster } from 'sonner'

async function bootstrap() {
  if (import.meta.env.VITE_MOCK === "true") {
    const { worker } = await import("./mocks/browser");
    await worker.start({ onUnhandledRequest: "bypass" });
    console.info("[MSW] Sandbox mode active — API calls are intercepted");
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <ThemeProvider>
        <App />
        <Toaster richColors position="bottom-right" />
      </ThemeProvider>
    </StrictMode>,
  );
}

bootstrap();
