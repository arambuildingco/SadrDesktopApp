import { useMemo, useState } from 'react'
import { ApiError, api, money, qs, type Json } from '../api'
import { DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

/* ------------------------------------------------------------------ cash boxes */
export function Cashes() {
  const { data, loading, error, reload } = useAsync(() => api.get<Json>('/api/cashes'), [])
  const [editing, setEditing] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const remove = async (row: Json) => {
    if (!confirm(`صندوق «${row.name}» حذف شود؟`)) return
    try {
      await api.del(`/api/cashes/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <span className="muted">جمع موجودی صندوق‌ها: <strong>{money(data?.totalBalance)}</strong></span>
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setEditing({ startBalance: 0 })}>
            + صندوق جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data?.items ?? []}
          empty="صندوقی ثبت نشده است."
          columns={[
            { key: 'name', label: 'نام صندوق' },
            { key: 'code', label: 'کد' },
            { key: 'ownerName', label: 'مسئول' },
            { key: 'startBalance', label: 'موجودی اولیه', numeric: true, render: (row: Json) => money(row.startBalance) },
            {
              key: 'currentBalance',
              label: 'موجودی فعلی',
              numeric: true,
              render: (row: Json) => <strong>{money(row.currentBalance)}</strong>
            },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <div className="row">
                  <button className="btn sm" onClick={() => setEditing(row)}>
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
        <CashForm
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
            flash('صندوق ذخیره شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function CashForm({ draft, onClose, onSaved }: { draft: Json; onClose: () => void; onSaved: () => void }) {
  const people = useOptions('/api/people')
  const companies = useOptions('/api/companies')
  const [form, setForm] = useState<Json>({ ...draft })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const body = {
        name: form.name,
        code: form.code ?? null,
        description: form.description ?? null,
        startBalance: Number(form.startBalance ?? 0),
        status: Number(form.status ?? 1),
        responcePersonId: Number(form.responcePersonId),
        ownerPersonId: form.ownerPersonId ? Number(form.ownerPersonId) : null,
        ownerCompanyId: form.ownerCompanyId ? Number(form.ownerCompanyId) : null,
        signPeople: form.signPeople ?? null
      }
      if (form.id) await api.put(`/api/cashes/${form.id}`, body)
      else await api.post('/api/cashes', body)
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={form.id ? 'ویرایش صندوق' : 'صندوق جدید'}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.name || !form.responcePersonId}>
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
        <FieldShell label="نام صندوق" required>
          <input value={String(form.name ?? '')} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </FieldShell>
        <FieldShell label="کد صندوق">
          <input value={String(form.code ?? '')} onChange={(e) => setForm({ ...form, code: e.target.value })} />
        </FieldShell>
        <FieldShell label="مسئول صندوق" required>
          <select value={String(form.responcePersonId ?? '')} onChange={(e) => setForm({ ...form, responcePersonId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="موجودی اولیه">
          <input type="number" value={String(form.startBalance ?? 0)} onChange={(e) => setForm({ ...form, startBalance: e.target.value })} />
        </FieldShell>
        <FieldShell label="مالک (شخص)">
          <select value={String(form.ownerPersonId ?? '')} onChange={(e) => setForm({ ...form, ownerPersonId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="مالک (شرکت)">
          <select value={String(form.ownerCompanyId ?? '')} onChange={(e) => setForm({ ...form, ownerCompanyId: e.target.value })}>
            <option value="">—</option>
            {companies.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="توضیحات">
          <input value={String(form.description ?? '')} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}

/* ------------------------------------------------------------------ money movements */
export function MoneyTransactions() {
  const [type, setType] = useState('')
  const [system, setSystem] = useState('')
  const [adding, setAdding] = useState(false)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ type, system, pageSize: 400 }), [type, system])
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/account-transactions${query}`), [query])

  const remove = async (row: Json) => {
    if (!confirm('این تراکنش مالی حذف شود؟ مانده صندوق/حساب بازمحاسبه می‌شود.')) return
    try {
      await api.del(`/api/account-transactions/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <select value={type} onChange={(e) => setType(e.target.value)} style={{ maxWidth: 150 }}>
            <option value="">همه انواع</option>
            <option value="1">دریافت</option>
            <option value="2">پرداخت</option>
            <option value="3">انتقال</option>
          </select>
          <select value={system} onChange={(e) => setSystem(e.target.value)} style={{ maxWidth: 150 }}>
            <option value="">صندوق و بانک</option>
            <option value="1">صندوق</option>
            <option value="2">بانک</option>
          </select>
          <span className="spacer" />
          <span className="muted">
            دریافت {money(data?.received)} · پرداخت {money(data?.paid)} · خالص {money(data?.net)}
          </span>
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setAdding(true)}>
            + تراکنش مالی
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data?.items ?? []}
          empty="تراکنشی ثبت نشده است."
          columns={[
            { key: 'dateG', label: 'تاریخ', render: (row: Json) => isoToJalali(row.dateG) },
            {
              key: 'type',
              label: 'نوع',
              render: (row: Json) =>
                row.type === 1 ? <span className="pos">دریافت</span> : row.type === 2 ? <span className="neg">پرداخت</span> : 'انتقال'
            },
            { key: 'system', label: 'محل', render: (row: Json) => row.cashName || row.bankAccountName || '—' },
            { key: 'value', label: 'مبلغ', numeric: true, render: (row: Json) => money(row.value) },
            { key: 'invoiceNumber', label: 'فاکتور' },
            { key: 'personName', label: 'شخص' },
            { key: 'description', label: 'توضیح' },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <button className="btn sm danger" onClick={() => remove(row)}>
                  حذف
                </button>
              )
            }
          ]}
        />
      </div>

      {adding && (
        <TransactionForm
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false)
            reload()
            flash('تراکنش مالی ثبت شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function TransactionForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const accounts = useOptions('/api/bank-accounts')
  const cashes = useAsync(() => api.list<Json>('/api/cashes'), [])
  const people = useOptions('/api/people')
  const [form, setForm] = useState<Json>({ type: 1, system: 2, value: '', date: todayJalali(), description: '', bankAccountId: '', cashId: '', personId: '' })
  const [error, setError] = useState('')
  const isCash = Number(form.system) === 1

  const save = async () => {
    try {
      await api.post('/api/account-transactions', {
        type: Number(form.type),
        transActionSystem: Number(form.system),
        bankAccountId: isCash ? null : Number(form.bankAccountId),
        cashId: isCash ? Number(form.cashId) : null,
        value: Number(form.value),
        dateG: jalaliToIso(form.date),
        description: form.description,
        personId: form.personId ? Number(form.personId) : null
      })
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title="تراکنش مالی جدید"
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.value || (isCash ? !form.cashId : !form.bankAccountId)}>
            ثبت
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <div className="grid cols-2">
        <FieldShell label="نوع" required>
          <select value={String(form.type)} onChange={(e) => setForm({ ...form, type: Number(e.target.value) })}>
            <option value={1}>دریافت</option>
            <option value={2}>پرداخت</option>
            <option value={3}>انتقال</option>
          </select>
        </FieldShell>
        <FieldShell label="محل وجه" required>
          <select value={String(form.system)} onChange={(e) => setForm({ ...form, system: Number(e.target.value) })}>
            <option value={2}>حساب بانکی</option>
            <option value={1}>صندوق</option>
          </select>
        </FieldShell>
        {isCash ? (
          <FieldShell label="صندوق" required>
            <select value={String(form.cashId)} onChange={(e) => setForm({ ...form, cashId: e.target.value })}>
              <option value="">—</option>
              {(cashes.data ?? []).map((cash) => (
                <option key={cash.id} value={cash.id}>
                  {cash.name}
                </option>
              ))}
            </select>
          </FieldShell>
        ) : (
          <FieldShell label="حساب بانکی" required>
            <select value={String(form.bankAccountId)} onChange={(e) => setForm({ ...form, bankAccountId: e.target.value })}>
              <option value="">—</option>
              {accounts.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </FieldShell>
        )}
        <FieldShell label="مبلغ" required>
          <input type="number" value={String(form.value)} onChange={(e) => setForm({ ...form, value: e.target.value })} />
        </FieldShell>
        <FieldShell label="تاریخ">
          <input value={String(form.date)} onChange={(e) => setForm({ ...form, date: e.target.value })} />
        </FieldShell>
        <FieldShell label="شخص (اختیاری)">
          <select value={String(form.personId)} onChange={(e) => setForm({ ...form, personId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="توضیحات">
          <input value={String(form.description)} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}
