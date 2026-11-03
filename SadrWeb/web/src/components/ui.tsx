import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { ApiError, api, auth, type Json } from '../api'

/* ------------------------------------------------------------------ async data */
export function useAsync<T>(loader: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)

  const reload = useCallback(() => setTick((value) => value + 1), [])

  useEffect(() => {
    let alive = true
    setLoading(true)
    loader()
      .then((result) => {
        if (alive) {
          setData(result)
          setError(null)
        }
      })
      .catch((err: unknown) => {
        if (alive) setError(err instanceof ApiError ? err.message : String(err))
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  return { data, error, loading, reload, setData }
}

/* ------------------------------------------------------------------ options */
export type Option = { value: number | string; label: string }

/** Loads a CRUD collection into dropdown options (id/label fields configurable). */
export function useOptions(url: string | null, idField = 'id', labelField = 'name') {
  const [options, setOptions] = useState<Option[]>([])
  useEffect(() => {
    if (!url) return
    let alive = true
    api
      .list<Json>(url)
      .then((rows) => {
        if (!alive) return
        setOptions(rows.map((row) => ({ value: row[idField], label: String(row[labelField] ?? `#${row[idField]}`) })))
      })
      .catch(() => setOptions([]))
    return () => {
      alive = false
    }
  }, [url, idField, labelField])
  return options
}

/* ------------------------------------------------------------------ table */
export type Column<T> = {
  key: string
  label: string
  render?: (row: T) => ReactNode
  numeric?: boolean
  width?: string
}

export function DataTable<T extends { id?: number | string }>({
  columns,
  rows,
  loading,
  empty = 'موردی ثبت نشده است.',
  onRowClick,
  selectedId
}: {
  columns: Column<T>[]
  rows: T[]
  loading?: boolean
  empty?: string
  onRowClick?: (row: T) => void
  selectedId?: number | string | null
}) {
  if (loading) return <div className="loading">در حال بارگذاری…</div>
  if (!rows.length) return <div className="empty">{empty}</div>

  return (
    <div className="table-wrap">
      <table className="data">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.key} style={column.width ? { width: column.width } : undefined}>
                {column.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr
              key={String(row.id ?? index)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              style={{
                cursor: onRowClick ? 'pointer' : undefined,
                background: selectedId !== undefined && selectedId === row.id ? '#eef4ff' : undefined
              }}
            >
              {columns.map((column) => (
                <td key={column.key} className={column.numeric ? 'num' : undefined}>
                  {column.render ? column.render(row) : String((row as Json)[column.key] ?? '')}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/* ------------------------------------------------------------------ modal */
export function Modal({
  title,
  children,
  footer,
  onClose,
  wide
}: {
  title: string
  children: ReactNode
  footer?: ReactNode
  onClose: () => void
  wide?: boolean
}) {
  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [onClose])

  return (
    <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div className={`modal${wide ? ' wide' : ''}`} role="dialog" aria-modal="true">
        <header>
          <h3>{title}</h3>
          <button className="btn ghost sm" onClick={onClose} aria-label="بستن">
            ✕
          </button>
        </header>
        <div className="body">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ toast + confirm */
export function useFlash() {
  const [message, setMessage] = useState<{ text: string; kind: 'ok' | 'bad' } | null>(null)
  useEffect(() => {
    if (!message) return
    const timer = setTimeout(() => setMessage(null), 4000)
    return () => clearTimeout(timer)
  }, [message])
  return {
    flash: (text: string) => setMessage({ text, kind: 'ok' }),
    fail: (text: string) => setMessage({ text, kind: 'bad' }),
    node: message ? (
      <div
        className="card"
        style={{
          position: 'fixed',
          bottom: 18,
          left: 18,
          zIndex: 60,
          padding: '10px 14px',
          borderColor: message.kind === 'ok' ? '#bfe6d6' : '#f3c9c9',
          color: message.kind === 'ok' ? 'var(--success)' : 'var(--danger)'
        }}
      >
        {message.text}
      </div>
    ) : null
  }
}

/* ------------------------------------------------------------------ authenticated images */
/**
 * Stored files are protected by the API's bearer token, so a plain <img src> would get a
 * 401. This loads the bytes with the token and shows them from a blob URL, revoking it when
 * the component unmounts or the source changes.
 */
export function AuthedImage({ src, alt, className }: { src: string; alt?: string; className?: string }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    let created: string | null = null
    fetch(src, { headers: auth.token ? { Authorization: `Bearer ${auth.token}` } : {} })
      .then((response) => (response.ok ? response.blob() : null))
      .then((blob) => {
        if (!alive || !blob) return
        created = URL.createObjectURL(blob)
        setObjectUrl(created)
      })
      .catch(() => undefined)

    return () => {
      alive = false
      if (created) URL.revokeObjectURL(created)
    }
  }, [src])

  if (!objectUrl) return <div className={className} style={{ background: 'var(--surface-2)' }} />
  return <img src={objectUrl} alt={alt ?? ''} className={className} />
}

/* ------------------------------------------------------------------ small bits */
export function FieldShell({
  label,
  required,
  hint,
  children
}: {
  label: string
  required?: boolean
  hint?: string
  children: ReactNode
}) {
  return (
    <label className="field">
      <span>
        {label}
        {required && <span className="req">*</span>}
      </span>
      {children}
      {hint && <span className="muted" style={{ fontSize: 11.5 }}>{hint}</span>}
    </label>
  )
}

export function Badge({ kind = 'info', children }: { kind?: 'info' | 'ok' | 'warn' | 'bad'; children: ReactNode }) {
  return <span className={`badge ${kind}`}>{children}</span>
}
