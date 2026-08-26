import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'

export function RouteEffects() {
  const location = useLocation()

  useEffect(() => {
    const title = document.querySelector<HTMLElement>('[data-page-title]')
    if (!title) return

    document.title = `${title.textContent ?? 'Sub2API Report'} | Sub2API Report`
    const frame = requestAnimationFrame(() => title.focus())
    return () => cancelAnimationFrame(frame)
  }, [location.pathname])

  return null
}
