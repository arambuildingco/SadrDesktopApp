import { useMemo, useState } from 'react'
import { ApiError, api, money, qs, type Json } from '../api'
import { Badge, DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

const directionLabels: Record<number, string> = { 1: 'دریافتی', 2: 'پرداختی' }
const statusLabels: Record<number, string> = {
  1: 'در جعبه',
  2: 'واگذار به بانک',
  3: 'وصول شده',
  4: 'برگشت خورده',
  5: 'خرج شده',
  6: 'ابطال شده'
}
const statusKind = (status: number) =>
  status === 3 ? 'ok' : status === 4 ? 'bad' : status === 6 ? 'warn' : status === 5 ? 'info' : 'info'

export function Cheques() {
  const [direction, setDirection] = useState('')
  const [status, setStatus] = useState('')
  const [term, setTerm] = useState('')
  const [editing, setEditing] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ direction, status, q: term }), [direction, status, term])
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/cheques${query}`), [query])

  const transition = async (row: Json, next: number) => {
    if (!confirm(`وضعیت چک ${row.number} به «${statusLabels[next]}» تغییر کند؟`)) return
    try {
      await api.post(`/api/cheques/${row.id}/status`, { status: next })
      flash(`چک ${statusLabels[next]} شد؛ مانده حساب به‌روزرسانی شد.`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <input placeholder="شماره چک یا طرف حساب…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 230 }} />
          <select value={direction} onChange={(e) => setDirection(e.target.value)} style={{ maxWidth: 150 }}>
            <option value="">همه جهت‌ها</option>
            <option value="1">دریافتی</option>
            <option value="2">پرداختی</option>
          </select>
          <select value={status} onChange={(e) => setStatus(e.target.value)} style={{ maxWidth: 170 }}>
            <option value="">همه وضعیت‌ها</option>
            {Object.entries(statusLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
          <span className="spacer" />
          <span className="muted">
            دریافتی در جعبه: {money(data?.totals?.receivedInHand)} · پرداختی: {money(data?.totals?.issuedInHand)}
          </span>
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setEditing({ direction: 1, status: 1, date: todayJalali(), amount: 0 })}>
            + چک جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}

        <DataTable
          loading={loading}
          rows={data?.items ?? []}
          empty="چکی ثبت نشده است."
          columns={[
            { key: 'number', label: 'شماره' },
            { key: 'dateG', label: 'سررسید', render: (row: Json) => isoToJalali(row.dateG) },
            { key: 'direction', label: 'جهت', render: (row: Json) => directionLabels[row.direction] ?? '—' },
            { key: 'recieverFullName', label: 'طرف حساب' },
            { key: 'accountName', label: 'حساب' },
            { key: 'amount', label: 'مبلغ', numeric: true, render: (row: Json) => money(row.amount) },
            { key: 'invoiceNumber', label: 'فاکتور' },
            {
              key: 'status',
              label: 'وضعیت',
              render: (row: Json) => <Badge kind={statusKind(row.status)}>{statusLabels[row.status] ?? '—'}</Badge>
            },
            {
              key: 'days',
              label: 'مانده روز',
              numeric: true,
              render: (row: Json) => (
                <span className={row.daysToDue < 0 ? 'neg' : undefined}>{row.daysToDue}</span>
              )
            },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <div className="row">
                  {row.status !== 3 && (
                    <button className="btn sm" onClick={() => transition(row, 3)}>
                      وصول
                    </button>
                  )}
                  {row.status !== 4 && (
                    <button className="btn sm" onClick={() => transition(row, 4)}>
                      برگشت
                    </button>
                  )}
                  {row.direction === 2 && row.status !== 5 && (
                    <button className="btn sm" onClick={() => transition(row, 5)}>
                      خرج شد
                    </button>
                  )}
                  <button className="btn sm" onClick={() => setEditing({ ...row, date: isoToJalali(row.dateG) })}>
                    ویرایش
                  </button>
                </div>
              )
            }
          ]}
        />
      </div>

      {editing && (
        <ChequeForm
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
            flash('چک ذخیره شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function ChequeForm({ draft, onClose, onSaved }: { draft: Json; onClose: () => void; onSaved: () => void }) {
  const accounts = useOptions('/api/bank-accounts')
  const parties = useOptions('/api/customers-providers')
  const [form, setForm] = useState<Json>({ ...draft })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const body = {
        bankAccountId: Number(form.bankAccountId),
        dateG: jalaliToIso(form.date),
        number: form.number ?? '',
        serialNumber: form.serialNumber ?? '',
        recieverFullName: form.recieverFullName ?? '',
        recieverCode: form.recieverCode ?? '',
        description: form.describtion ?? '',
        amount: Number(form.amount ?? 0),
        invoiceId: form.invoiceId ? Number(form.invoiceId) : null,
        personId: form.personId ? Number(form.personId) : null,
        direction: Number(form.direction),
        status: Number(form.status)
      }
      if (form.id) await api.put(`/api/cheques/${form.id}`, body)
      else await api.post('/api/cheques', body)
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={form.id ? `ویرایش چک ${form.number ?? ''}` : 'چک جدید'}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.bankAccountId || !form.amount}>
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
        <FieldShell label="حساب بانکی" required>
          <select value={String(form.bankAccountId ?? '')} onChange={(e) => setForm({ ...form, bankAccountId: e.target.value })}>
            <option value="">—</option>
            {accounts.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="جهت" required>
          <select value={String(form.direction)} onChange={(e) => setForm({ ...form, direction: Number(e.target.value) })}>
            <option value={1}>دریافتی (از مشتری)</option>
            <option value={2}>پرداختی (به تأمین‌کننده)</option>
          </select>
        </FieldShell>
        <FieldShell label="شماره چک">
          <input value={String(form.number ?? '')} onChange={(e) => setForm({ ...form, number: e.target.value })} />
        </FieldShell>
        <FieldShell label="شماره سریال">
          <input value={String(form.serialNumber ?? '')} onChange={(e) => setForm({ ...form, serialNumber: e.target.value })} />
        </FieldShell>
        <FieldShell label="تاریخ سررسید">
          <input value={String(form.date ?? '')} onChange={(e) => setForm({ ...form, date: e.target.value })} placeholder="1405/08/12" />
        </FieldShell>
        <FieldShell label="مبلغ" required>
          <input type="number" value={String(form.amount ?? 0)} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
        </FieldShell>
        <FieldShell label="نام طرف حساب">
          <input value={String(form.recieverFullName ?? '')} onChange={(e) => setForm({ ...form, recieverFullName: e.target.value })} />
        </FieldShell>
        <FieldShell label="کد طرف حساب">
          <input value={String(form.recieverCode ?? '')} onChange={(e) => setForm({ ...form, recieverCode: e.target.value })} />
        </FieldShell>
        <FieldShell label="طرف حساب (اختیاری)">
          <select value={String(form.personId ?? '')} onChange={(e) => setForm({ ...form, personId: e.target.value })}>
            <option value="">—</option>
            {parties.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="وضعیت">
          <select value={String(form.status)} onChange={(e) => setForm({ ...form, status: Number(e.target.value) })}>
            {Object.entries(statusLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="توضیحات">
          <input value={String(form.describtion ?? '')} onChange={(e) => setForm({ ...form, describtion: e.target.value })} />
        </FieldShell>
      </div>
      <p className="muted" style={{ fontSize: 12, marginBottom: 0 }}>
        چک «وصول شده» به حساب بانکی واریز و «خرج شده» از آن برداشت می‌شود؛ «برگشت خورده» اثر قبلی را برمی‌گرداند.
      </p>
    </Modal>
  )
}
