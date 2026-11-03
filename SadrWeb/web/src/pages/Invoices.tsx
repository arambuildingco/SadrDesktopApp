import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError, api, money, qs, type Json } from '../api'
import { Badge, DataTable, useAsync, useFlash } from '../components/ui'
import { isoToJalali } from '../jalali'

const typeLabels: Record<number, string> = { 0: 'فروش', 1: 'خرید', 2: 'پیش‌فاکتور' }
const statusLabels: Record<number, string> = { 0: 'پیش‌نویس', 1: 'صادر شده', 2: 'تأیید شده', 3: 'لغو شده' }
const statusKind = (status: number) => (status === 2 ? 'ok' : status === 3 ? 'bad' : status === 1 ? 'info' : 'warn')

export function Invoices() {
  const navigate = useNavigate()
  const [type, setType] = useState('')
  const [status, setStatus] = useState('')
  const [term, setTerm] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const { fail, node } = useFlash()

  const query = useMemo(
    () => qs({ type, status, q: term, from: from || undefined, to: to || undefined, pageSize: 300 }),
    [type, status, term, from, to]
  )
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/invoices${query}`), [query])

  const remove = async (row: Json) => {
    if (!confirm(`فاکتور ${row.invoiceNumber} حذف شود؟`)) return
    try {
      await api.del(`/api/invoices/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  const confirmInvoice = async (row: Json) => {
    await api.post(`/api/invoices/${row.id}/status?status=2`)
    reload()
  }

  return (
    <div className="card">
      <div className="toolbar">
        <input placeholder="شماره یا توضیحات…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 220 }} />
        <select value={type} onChange={(e) => setType(e.target.value)} style={{ maxWidth: 150 }}>
          <option value="">همه انواع</option>
          <option value="0">فروش</option>
          <option value="1">خرید</option>
          <option value="2">پیش‌فاکتور</option>
        </select>
        <select value={status} onChange={(e) => setStatus(e.target.value)} style={{ maxWidth: 150 }}>
          <option value="">همه وضعیت‌ها</option>
          {Object.entries(statusLabels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} style={{ maxWidth: 150 }} title="از تاریخ (میلادی)" />
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} style={{ maxWidth: 150 }} title="تا تاریخ (میلادی)" />
        <span className="spacer" />
        <button className="btn" onClick={reload}>
          بازخوانی
        </button>
        <button className="btn primary" onClick={() => navigate('/invoices/new')}>
          + فاکتور جدید
        </button>
      </div>

      {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}

      <DataTable
        loading={loading}
        rows={data?.items ?? []}
        empty="فاکتوری با این فیلترها یافت نشد."
        columns={[
          {
            key: 'invoiceNumber',
            label: 'شماره',
            render: (row: Json) => <Link to={`/invoices/${row.id}`}>{row.invoiceNumber}</Link>
          },
          { key: 'invoiceDateG', label: 'تاریخ', render: (row: Json) => isoToJalali(row.invoiceDateG) },
          { key: 'invoiceType', label: 'نوع', render: (row: Json) => typeLabels[row.invoiceType] ?? '—' },
          { key: 'partyName', label: 'طرف حساب' },
          { key: 'totalPrice', label: 'مبلغ کل', numeric: true, render: (row: Json) => money(row.totalPrice) },
          { key: 'totalDiscount', label: 'تخفیف', numeric: true, render: (row: Json) => money(row.totalDiscount) },
          { key: 'paidAmount', label: 'پرداخت', numeric: true, render: (row: Json) => money(row.paidAmount) },
          {
            key: 'remaining',
            label: 'مانده',
            numeric: true,
            render: (row: Json) => {
              const remaining = row.totalPrice - row.paidAmount
              return <span className={remaining > 0 ? 'neg' : 'pos'}>{money(remaining)}</span>
            }
          },
          {
            key: 'status',
            label: 'وضعیت',
            render: (row: Json) => <Badge kind={statusKind(row.status)}>{statusLabels[row.status] ?? '—'}</Badge>
          },
          {
            key: '__actions',
            label: '',
            render: (row: Json) => (
              <div className="row">
                <button className="btn sm" onClick={() => navigate(`/invoices/${row.id}`)}>
                  ویرایش
                </button>
                {row.status !== 2 && (
                  <button className="btn sm" onClick={() => confirmInvoice(row)}>
                    تأیید
                  </button>
                )}
                <button className="btn sm danger" onClick={() => remove(row)}>
                  حذف
                </button>
              </div>
            )
          }
        ]}
      />
      {node}
    </div>
  )
}
