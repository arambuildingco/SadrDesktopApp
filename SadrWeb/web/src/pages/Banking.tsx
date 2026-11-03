import { useMemo, useState } from 'react'
import { ApiError, api, money, qs, type Json } from '../api'
import { DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

export function BankAccounts() {
  const { data, loading, error, reload } = useAsync(() => api.list<Json>('/api/bank-accounts'), [])
  const [editing, setEditing] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const remove = async (row: Json) => {
    if (!confirm(`حساب «${row.name}» حذف شود؟`)) return
    try {
      await api.del(`/api/bank-accounts/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <span className="muted">موجودی حساب‌ها از دفتر تراکنش‌ها محاسبه می‌شود (شروع + واریز − برداشت).</span>
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setEditing({ startBalance: 0 })}>
            + حساب جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data ?? []}
          empty="حساب بانکی ثبت نشده است."
          columns={[
            { key: 'name', label: 'نام حساب' },
            { key: 'bankName', label: 'بانک' },
            { key: 'branchName', label: 'شعبه' },
            { key: 'accountNumber', label: 'شماره حساب' },
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
        <AccountForm
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
            flash('حساب ذخیره شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function AccountForm({ draft, onClose, onSaved }: { draft: Json; onClose: () => void; onSaved: () => void }) {
  const branches = useAsync(() => api.list<Json>('/api/bank-branches'), [])
  const currencies = useOptions('/api/currencies')
  const people = useOptions('/api/people')
  const companies = useOptions('/api/companies')
  const [form, setForm] = useState<Json>({ ...draft })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const body = {
        name: form.name,
        bankBranchId: Number(form.bankBranchId),
        currencyId: Number(form.currencyId),
        accountNumber: form.accountNumber ?? '',
        cardNumber: form.cardNumber ?? '',
        idNumber: form.idNumber ?? '',
        accountType: Number(form.accountType ?? 0),
        description: form.description ?? '',
        startBalance: Number(form.startBalance ?? 0),
        personId: form.personId ? Number(form.personId) : null,
        companyId: form.companyId ? Number(form.companyId) : null,
        signPeople: form.signPeople ?? null
      }
      if (form.id) await api.put(`/api/bank-accounts/${form.id}`, body)
      else await api.post('/api/bank-accounts', body)
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={form.id ? 'ویرایش حساب بانکی' : 'حساب بانکی جدید'}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.name || !form.bankBranchId}>
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
        <FieldShell label="نام حساب" required>
          <input value={String(form.name ?? '')} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </FieldShell>
        <FieldShell label="شعبه بانک" required>
          <select value={String(form.bankBranchId ?? '')} onChange={(e) => setForm({ ...form, bankBranchId: e.target.value })}>
            <option value="">—</option>
            {(branches.data ?? []).map((branch) => (
              <option key={branch.id} value={branch.id}>
                {branch.name}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="واحد پول" required>
          <select value={String(form.currencyId ?? '')} onChange={(e) => setForm({ ...form, currencyId: e.target.value })}>
            <option value="">—</option>
            {currencies.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="شماره حساب">
          <input value={String(form.accountNumber ?? '')} onChange={(e) => setForm({ ...form, accountNumber: e.target.value })} />
        </FieldShell>
        <FieldShell label="شماره کارت">
          <input value={String(form.cardNumber ?? '')} onChange={(e) => setForm({ ...form, cardNumber: e.target.value })} />
        </FieldShell>
        <FieldShell label="شناسه حساب">
          <input value={String(form.idNumber ?? '')} onChange={(e) => setForm({ ...form, idNumber: e.target.value })} />
        </FieldShell>
        <FieldShell label="موجودی اولیه">
          <input type="number" value={String(form.startBalance ?? 0)} onChange={(e) => setForm({ ...form, startBalance: e.target.value })} />
        </FieldShell>
        <FieldShell label="صاحب حساب (شخص)">
          <select value={String(form.personId ?? '')} onChange={(e) => setForm({ ...form, personId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="صاحب حساب (شرکت)">
          <select value={String(form.companyId ?? '')} onChange={(e) => setForm({ ...form, companyId: e.target.value })}>
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

/* ------------------------------------------------------------------ ledger */
export function BankTransactions() {
  const accounts = useOptions('/api/bank-accounts')
  const [accountId, setAccountId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [adding, setAdding] = useState(false)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ bankAccountId: accountId, from: from || undefined, to: to || undefined, pageSize: 400 }), [accountId, from, to])
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/bank-transactions${query}`), [query])

  const remove = async (row: Json) => {
    if (!confirm('این تراکنش حذف شود؟ مانده حساب بازمحاسبه می‌شود.')) return
    try {
      await api.del(`/api/bank-transactions/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <select value={accountId} onChange={(e) => setAccountId(e.target.value)} style={{ maxWidth: 220 }}>
            <option value="">همه حساب‌ها</option>
            {accounts.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} style={{ maxWidth: 150 }} />
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} style={{ maxWidth: 150 }} />
          <span className="spacer" />
          {data?.balance !== null && data?.balance !== undefined && <span>مانده: <strong>{money(data.balance)}</strong></span>}
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setAdding(true)} disabled={!accounts.length}>
            + واریز / برداشت
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data?.items ?? []}
          empty="تراکنشی ثبت نشده است."
          columns={[
            { key: 'dateG', label: 'تاریخ', render: (row: Json) => isoToJalali(row.dateG) },
            { key: 'accountName', label: 'حساب' },
            {
              key: 'kind',
              label: 'نوع',
              render: (row: Json) => <span className={row.kind === 1 ? 'pos' : 'neg'}>{row.kind === 1 ? 'واریز' : 'برداشت'}</span>
            },
            { key: 'amount', label: 'مبلغ', numeric: true, render: (row: Json) => money(row.amount) },
            { key: 'describtion', label: 'توضیح' },
            { key: 'eventKind', label: 'رویداد چک' },
            {
              key: '__actions',
              label: '',
              render: (row: Json) =>
                row.source === 1 ? (
                  <button className="btn sm danger" onClick={() => remove(row)}>
                    حذف
                  </button>
                ) : (
                  <span className="muted">از چک</span>
                )
            }
          ]}
        />
      </div>

      {adding && (
        <LedgerForm
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false)
            reload()
            flash('تراکنش ثبت و مانده حساب به‌روزرسانی شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function LedgerForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const accounts = useOptions('/api/bank-accounts')
  const [form, setForm] = useState<Json>({ bankAccountId: '', kind: 1, amount: '', date: todayJalali(), description: '' })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      await api.post('/api/bank-transactions', {
        bankAccountId: Number(form.bankAccountId),
        kind: Number(form.kind),
        amount: Number(form.amount),
        dateG: jalaliToIso(form.date),
        description: form.description
      })
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title="تراکنش بانکی جدید"
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.bankAccountId || !form.amount}>
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
        <FieldShell label="حساب" required>
          <select value={String(form.bankAccountId)} onChange={(e) => setForm({ ...form, bankAccountId: e.target.value })}>
            <option value="">—</option>
            {accounts.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="نوع" required>
          <select value={String(form.kind)} onChange={(e) => setForm({ ...form, kind: Number(e.target.value) })}>
            <option value={1}>واریز</option>
            <option value={2}>برداشت</option>
          </select>
        </FieldShell>
        <FieldShell label="مبلغ" required>
          <input type="number" value={String(form.amount)} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
        </FieldShell>
        <FieldShell label="تاریخ">
          <input value={String(form.date)} onChange={(e) => setForm({ ...form, date: e.target.value })} />
        </FieldShell>
        <FieldShell label="توضیح">
          <input value={String(form.description)} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}
