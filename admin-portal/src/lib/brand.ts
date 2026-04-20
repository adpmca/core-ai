export const APP_NAME = import.meta.env.VITE_APP_NAME ?? "Diva AI";
export const APP_SLUG = import.meta.env.VITE_APP_SLUG ?? "diva";

export function storageKey(key: string): string {
  return `${APP_SLUG}_${key}`;
}
