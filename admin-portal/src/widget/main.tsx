import { createRoot } from 'react-dom/client';
import { WidgetApp } from './WidgetApp';

const params = new URLSearchParams(window.location.search);
const widgetId = params.get('id') ?? '';

createRoot(document.getElementById('widget-root')!).render(
  <WidgetApp widgetId={widgetId} />
);
