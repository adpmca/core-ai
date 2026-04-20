export interface WidgetTheme {
  background: string;
  surface: string;
  border: string;
  primary: string;
  primaryText: string;
  text: string;
  textMuted: string;
  fontFamily: string;
  fontSize: string;
  agentBubbleBg: string;
  agentBubbleText: string;
  headerBg: string;
  headerText: string;
  inputBg: string;
  inputBorder: string;
  inputText: string;
  launcherSize: number;
  preset?: string;
}

export interface WidgetInitResponse {
  widgetId: string;
  agentId: string;
  agentName: string;
  hasSso: boolean;
  allowAnonymous: boolean;
  welcomeMessage?: string;
  placeholderText?: string;
  theme: WidgetTheme;
  respectSystemTheme: boolean;
  showBranding: boolean;
}

export interface AgentStreamChunk {
  type:
    | 'text_delta'
    | 'final_response'
    | 'error'
    | 'done'
    | 'iteration_start'
    | 'continuation_start'
    | 'tool_call'
    | 'tool_result';
  content?: string;
  delta?: string;
  sessionId?: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'agent';
  content: string;
  streaming?: boolean;
}

export const LIGHT_PRESET: WidgetTheme = {
  background: '#ffffff',
  surface: '#f9fafb',
  border: '#e5e7eb',
  primary: '#6366f1',
  primaryText: '#ffffff',
  text: '#111827',
  textMuted: '#6b7280',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '14px',
  agentBubbleBg: '#f3f4f6',
  agentBubbleText: '#111827',
  headerBg: '#6366f1',
  headerText: '#ffffff',
  inputBg: '#ffffff',
  inputBorder: '#d1d5db',
  inputText: '#111827',
  launcherSize: 56,
  preset: 'light',
};

export const DARK_PRESET: WidgetTheme = {
  background: '#1f2937',
  surface: '#111827',
  border: '#374151',
  primary: '#818cf8',
  primaryText: '#ffffff',
  text: '#f9fafb',
  textMuted: '#9ca3af',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '14px',
  agentBubbleBg: '#374151',
  agentBubbleText: '#f9fafb',
  headerBg: '#111827',
  headerText: '#f9fafb',
  inputBg: '#1f2937',
  inputBorder: '#4b5563',
  inputText: '#f9fafb',
  launcherSize: 56,
  preset: 'dark',
};
