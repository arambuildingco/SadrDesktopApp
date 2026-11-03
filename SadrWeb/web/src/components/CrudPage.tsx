import { useMemo, useState } from 'react'
import { ApiError, api, qs, type Json } from '../api'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'
import { Badge, DataTable, FieldShell, Modal, useAsync, useFlash, type Column, type Option } from './ui'

export type FieldKind = 'text' | 'number' | 'money' | 'date' | 'textarea' | 'checkbox' | 'select' | 'password'

export type FieldDef = {
  name: string
  label: string
  kind?: FieldKind
  required?: boolean
  options?: Option[]
  /** Fetch the dropdown options from a CRUD collection instead of hard-coding them. */
  optionsUrl?: string
  optionId?: string
  optionLabel?: string
  default?: unknown
  hint?: string
  readOnly?: boolean
}

/**
 * A complete list + create + edit + delete screen for one REST collection.
 *
 * The desktop application solved this with one generic FieldEditorWindow; the web app
 * uses the same idea so every lookup table gets an identical, tested form.
 */
export function CrudPage({
  title,
  endpoint,
  fields,
  columns,
  searchable = true,
  canDelete = true,
  intro,
  extraQuery
}: {
  title: string
  endpoint: string
  fields: FieldDef[]
  columns: Column<Json>[]
  searchable?: boolean
  canDelete?: boolean
  intro?: string
  extraQuery?: Record<string, string | number>
}) {
  const [term, setTerm] = useState('')
  const [editing, setEditing] = useState<Json | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState('')
  const { flash, fail, node } = useFlash()

  const query = useMemo(
    () => qs({ q: term, pageSize: 500, ...(extraQuery ?? {}) }),
    [term, extraQuery]
  )
  const { data, loading, error, reload } = useAsync(() => api.list<Json>(`${endpoint}${query}`), [query])

  const openNew = () => {
    const blank: Json = {}
    fields.forEach((field) => {
      blank[field.name] =
        field.default ?? (field.kind === 'date' ? todayJalali() : field.kind === 'checkbox' ? false : '')
    })
    setFormError('')
    setEditing(blank)
  }

  const openEdit = (row: Json) => {
    const draft: Json = { ...row }
    fields.forEach((field) => {
      if (field.kind === 'date') draft[field.name] = isoToJalali(row[field.name])
    })
    setFormError('')
    setEditing(draft)
  }

  const save = async () => {
    if (!editing) return
    setSaving(true)
    setFormError('')
    try {
      const payload: Json = {}
      fields.forEach((field) => {
        let value = editing[field.name]
        if (field.kind === 'date') value = value ? jalaliToIso(value) : null
        else if (field.kind === 'number' || field.kind === 'money') value = value === '' ? 0 : Number(value)
        else if (field.kind === 'checkbox') value = Boolean(value)
        else if (field.kind === 'select' && value === '') value = null
        payload[field.name] = value
      })

      if (editing.id) await api.put(`${endpoint}/${editing.id}`, payload)
      else await api.post(endpoint, payload)

      setEditing(null)
      flash('ذخیره شد.')
      reload()
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  const remove = async (row: Json) => {
    if (!confirm(`«${row.name ?? row.title ?? row.id}» حذف شود؟`)) return
    try {
      await api.del(`${endpoint}/${row.id}`)
      flash('حذف شد.')
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          {searchable && (
            <input
              placeholder="جستجو…"
              value={term}
              onChange={(event) => setTerm(event.target.value)}
              style={{ maxWidth: 260 }}
            />
          )}
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={openNew}>
            + {title} جدید
          </button>
        </div>
        {intro && <p className="muted" style={{ marginTop: 0 }}>{intro}</p>}
        {error && <div className="card" style={{ borderColor: '#f3c9c9', color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data ?? []}
          columns={[
            ...columns,
            {
              key: '__actions',
              label: '',
              width: '150px',
              render: (row) => (
                <div className="row" onClick={(event) => event.stopPropagation()}>
                  <button className="btn sm" onClick={() => openEdit(row)}>
                    ویرایش
                  </button>
                  {canDelete && (
                    <button className="btn sm danger" onClick={() => remove(row)}>
                      حذف
                    </button>
                  )}
                </div>
              )
            }
          ]}
        />
      </div>

      {editing && (
        <Modal
          title={editing.id ? `ویرایش ${title}` : `${title} جدید`}
          onClose={() => setEditing(null)}
          footer={
            <>
              <button className="btn primary" onClick={save} disabled={saving}>
                {saving ? 'در حال ذخیره…' : 'ذخیره'}
              </button>
              <button className="btn" onClick={() => setEditing(null)}>
                انصراف
              </button>
              {formError && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{formError}</span>}
            </>
          }
        >
          <div className="grid cols-2">
            {fields.map((field) => (
              <FieldInput
                key={field.name}
                field={field}
                value={editing[field.name]}
                onChange={(value) => setEditing({ ...editing, [field.name]: value })}
              />
            ))}
          </div>
        </Modal>
      )}
      {node}
    </>
  )
}

/** One labelled control, covering every field kind the metadata can describe. */
export function FieldInput({
  field,
  value,
  onChange
}: {
  field: FieldDef
  value: unknown
  onChange: (value: unknown) => void
}) {
  const kind = field.kind ?? 'text'

  if (kind === 'checkbox') {
    return (
      <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
        <input type="checkbox" checked={Boolean(value)} onChange={(event) => onChange(event.target.checked)} />
        <span>{field.label}</span>
      </label>
    )
  }

  return (
    <FieldShell label={field.label} required={field.required}>
      {kind === 'textarea' ? (
        <textarea value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} />
      ) : kind === 'select' ? (
        <NativeSelect field={field} value={value} onChange={onChange} />
      ) : (
        <input
          type={kind === 'number' || kind === 'money' ? 'number' : kind === 'password' ? 'password' : 'text'}
          value={String(value ?? '')}
          placeholder={field.kind === 'date' ? '1405/08/12' : undefined}
          readOnly={field.readOnly}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
      {field.hint && <span className="muted" style={{ fontSize: 11.5 }}>{field.hint}</span>}
    </FieldShell>
  )
}

function NativeSelect({
  field,
  value,
  onChange
}: {
  field: FieldDef
  value: unknown
  onChange: (value: unknown) => void
}) {
  const [options, setOptions] = useState<Option[]>(field.options ?? [])

  useAsync(async () => {
    if (!field.optionsUrl || field.options) return []
    const rows = await api.list<Json>(field.optionsUrl)
    const mapped = rows.map((row) => ({
      value: row[field.optionId ?? 'id'],
      label: String(row[field.optionLabel ?? 'name'] ?? `#${row[field.optionId ?? 'id']}`)
    }))
    setOptions(mapped)
    return mapped
  }, [field.optionsUrl])

  return (
    <select value={value === null || value === undefined ? '' : String(value)} onChange={(event) => onChange(event.target.value)}>
      <option value="">— انتخاب کنید —</option>
      {options.map((option) => (
        <option key={String(option.value)} value={String(option.value)}>
          {option.label}
        </option>
      ))}
    </select>
  )
}

export { Badge }
