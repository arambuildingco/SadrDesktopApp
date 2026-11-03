import { useMemo, useState } from 'react'
import { ApiError, api, qs, type Json } from '../api'
import { DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'

/** People: the same form the desktop app has, including the optional detail account link. */
export function People() {
  const [term, setTerm] = useState('')
  const [editing, setEditing] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ q: term }), [term])
  const { data, loading, error, reload } = useAsync(() => api.list<Json>(`/api/people${query}`), [query])

  const remove = async (row: Json) => {
    if (!confirm(`«${row.fullName}» حذف شود؟`)) return
    try {
      await api.del(`/api/people/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <input placeholder="نام، کد یا تلفن…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 240 }} />
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setEditing({ isActive: true })}>
            + شخص جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data ?? []}
          empty="شخصی ثبت نشده است."
          columns={[
            { key: 'fullName', label: 'نام' },
            { key: 'code', label: 'کد ملی / کد' },
            { key: 'phoneNumber', label: 'تلفن' },
            { key: 'email', label: 'ایمیل' },
            { key: 'address', label: 'آدرس' },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <div className="row">
                  <button className="btn sm" onClick={async () => setEditing(await loadPerson(row.id))}>
                    ویرایش
                  </button>
                  <button className="btn sm danger" onClick={() => remove(row)}>
                    حذف
                  </button>
                </div>
              )
            }
          ]}
        />
      </div>

      {editing && (
        <PersonForm
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
            flash('شخص ذخیره شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

async function loadPerson(id: number): Promise<Json> {
  const payload = await api.get<Json>(`/api/people/${id}`)
  return { ...payload.person, subsidiaryAccountId: payload.subsidiaryAccountId }
}

function PersonForm({ draft, onClose, onSaved }: { draft: Json; onClose: () => void; onSaved: () => void }) {
  const subsidiaries = useOptions('/api/subsidiary-accounts')
  const [form, setForm] = useState<Json>({ ...draft })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const body = {
        firstName: form.firstName ?? '',
        middleName: form.middleName ?? '',
        lastName: form.lastName ?? '',
        code: form.code ?? '',
        title: form.title ?? '',
        fatherName: form.fatherName ?? '',
        gender: form.gender ?? null,
        birthDate: form.birthDate ?? '',
        certNumber: form.certNumber ?? '',
        phoneNumber: form.phoneNumber ?? '',
        email: form.email ?? '',
        address: form.address ?? '',
        idInformation: form.idInformation ?? '',
        isActive: form.isActive ?? true
      }
      const suffix = form.subsidiaryAccountId ? `?subsidiaryAccountId=${form.subsidiaryAccountId}` : ''
      if (form.id) await api.put(`/api/people/${form.id}${suffix}`, body)
      else await api.post('/api/people', body)
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={form.id ? 'ویرایش شخص' : 'شخص جدید'}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.firstName && !form.lastName}>
            ذخیره
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <div className="grid cols-2">
        <FieldShell label="نام" required>
          <input value={String(form.firstName ?? '')} onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
        </FieldShell>
        <FieldShell label="نام خانوادگی">
          <input value={String(form.lastName ?? '')} onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
        </FieldShell>
        <FieldShell label="عنوان">
          <input value={String(form.title ?? '')} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        </FieldShell>
        <FieldShell label="کد ملی / کد" hint="کد تکراری مجاز نیست؛ ارقام فارسی هم پذیرفته می‌شود.">
          <input value={String(form.code ?? '')} onChange={(e) => setForm({ ...form, code: e.target.value })} />
        </FieldShell>
        <FieldShell label="نام پدر">
          <input value={String(form.fatherName ?? '')} onChange={(e) => setForm({ ...form, fatherName: e.target.value })} />
        </FieldShell>
        <FieldShell label="تلفن">
          <input value={String(form.phoneNumber ?? '')} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} />
        </FieldShell>
        <FieldShell label="ایمیل">
          <input value={String(form.email ?? '')} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </FieldShell>
        <FieldShell label="حساب معین (اختیاری)" hint="با انتخاب حساب معین، حساب تفصیلی خودکار ساخته می‌شود.">
          <select value={String(form.subsidiaryAccountId ?? '')} onChange={(e) => setForm({ ...form, subsidiaryAccountId: e.target.value })}>
            <option value="">— بدون حساب معین —</option>
            {subsidiaries.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="آدرس">
          <textarea value={String(form.address ?? '')} onChange={(e) => setForm({ ...form, address: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}

/* ------------------------------------------------------------------ companies */
export function Companies() {
  const [term, setTerm] = useState('')
  const [editing, setEditing] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ q: term }), [term])
  const { data, loading, error, reload } = useAsync(() => api.list<Json>(`/api/companies${query}`), [query])

  const remove = async (row: Json) => {
    if (!confirm(`«${row.fullName}» حذف شود؟`)) return
    try {
      await api.del(`/api/companies/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <input placeholder="نام، کد یا شناسه ملی…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 240 }} />
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setEditing({})}>
            + شرکت جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data ?? []}
          empty="شرکتی ثبت نشده است."
          columns={[
            { key: 'fullName', label: 'نام شرکت' },
            { key: 'code', label: 'کد' },
            { key: 'nationalId', label: 'شناسه ملی' },
            { key: 'tel', label: 'تلفن' },
            { key: 'email', label: 'ایمیل' },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <div className="row">
                  <button
                    className="btn sm"
                    onClick={async () => {
                      const payload = await api.get<Json>(`/api/companies/${row.id}`)
                      setEditing({ ...payload.company, subsidiaryAccountId: payload.subsidiaryAccountId })
                    }}
                  >
                    ویرایش
                  </button>
                  <button className="btn sm danger" onClick={() => remove(row)}>
                    حذف
                  </button>
                </div>
              )
            }
          ]}
        />
      </div>

      {editing && (
        <CompanyForm
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
            flash('شرکت ذخیره شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function CompanyForm({ draft, onClose, onSaved }: { draft: Json; onClose: () => void; onSaved: () => void }) {
  const people = useOptions('/api/people')
  const subsidiaries = useOptions('/api/subsidiary-accounts')
  const [form, setForm] = useState<Json>({ ...draft })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const body = {
        fullName: form.fullName ?? '',
        code: form.code ?? '',
        nationalId: form.nationalId ?? '',
        registerId: form.registerId ?? '',
        address: form.address ?? '',
        tel: form.tel ?? '',
        email: form.email ?? '',
        managerId: form.managerId ? Number(form.managerId) : null,
        description: form.description ?? ''
      }
      const suffix = form.subsidiaryAccountId ? `?subsidiaryAccountId=${form.subsidiaryAccountId}` : ''
      if (form.id) await api.put(`/api/companies/${form.id}${suffix}`, body)
      else await api.post('/api/companies', body)
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={form.id ? 'ویرایش شرکت' : 'شرکت جدید'}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.fullName}>
            ذخیره
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <div className="grid cols-2">
        <FieldShell label="نام شرکت" required>
          <input value={String(form.fullName ?? '')} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
        </FieldShell>
        <FieldShell label="کد">
          <input value={String(form.code ?? '')} onChange={(e) => setForm({ ...form, code: e.target.value })} />
        </FieldShell>
        <FieldShell label="شناسه ملی" hint="۱۱ رقم؛ اعتبارسنجی خودکار.">
          <input value={String(form.nationalId ?? '')} onChange={(e) => setForm({ ...form, nationalId: e.target.value })} />
        </FieldShell>
        <FieldShell label="شماره ثبت">
          <input value={String(form.registerId ?? '')} onChange={(e) => setForm({ ...form, registerId: e.target.value })} />
        </FieldShell>
        <FieldShell label="تلفن">
          <input value={String(form.tel ?? '')} onChange={(e) => setForm({ ...form, tel: e.target.value })} />
        </FieldShell>
        <FieldShell label="ایمیل">
          <input value={String(form.email ?? '')} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </FieldShell>
        <FieldShell label="مدیرعامل">
          <select value={String(form.managerId ?? '')} onChange={(e) => setForm({ ...form, managerId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="حساب معین">
          <select value={String(form.subsidiaryAccountId ?? '')} onChange={(e) => setForm({ ...form, subsidiaryAccountId: e.target.value })}>
            <option value="">— بدون حساب معین —</option>
            {subsidiaries.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="آدرس">
          <textarea value={String(form.address ?? '')} onChange={(e) => setForm({ ...form, address: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}
