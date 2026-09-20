import { useCallback, useState } from 'react'

export interface MyLink {
  code: string
  longUrl: string
  shortUrl: string
  createdAt: string
}

const STORAGE_KEY = 'url-shortener.my-links'

function read(): MyLink[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as MyLink[]) : []
  } catch {
    return []
  }
}

function write(links: MyLink[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(links))
  } catch {
    // Private mode / disabled storage: keep working in-memory for this session.
  }
}

/** Per-browser list of created links (no accounts yet — localStorage only). */
export function useMyLinks() {
  const [links, setLinks] = useState<MyLink[]>(read)

  const addLink = useCallback((link: MyLink) => {
    setLinks((current) => {
      const next = [link, ...current.filter((l) => l.code !== link.code)]
      write(next)
      return next
    })
  }, [])

  const removeLink = useCallback((code: string) => {
    setLinks((current) => {
      const next = current.filter((l) => l.code !== code)
      write(next)
      return next
    })
  }, [])

  return { links, addLink, removeLink }
}
