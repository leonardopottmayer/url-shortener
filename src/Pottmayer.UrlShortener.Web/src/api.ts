import axios from 'axios'

// Same-origin: the Vite dev server proxies /api to the gateway (see vite.config.ts).
const api = axios.create({ headers: { 'Content-Type': 'application/json' } })

export interface ShortenResponse {
  code: string
  shortUrl: string
}

export async function shorten(longUrl: string, customAlias?: string): Promise<ShortenResponse> {
  const body: Record<string, string> = { longUrl }
  if (customAlias) body.customAlias = customAlias
  const { data } = await api.post<ShortenResponse>('/api/urls', body)
  return data
}

export interface Stats {
  code: string
  totalClicks: number
}

export async function getStats(code: string): Promise<Stats> {
  const { data } = await api.get<Stats>(`/api/stats/${encodeURIComponent(code)}`)
  return data
}
