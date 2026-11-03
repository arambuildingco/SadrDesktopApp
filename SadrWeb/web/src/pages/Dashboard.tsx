import { Link } from 'react-router-dom'
import { api, money, number, type Json } from '../api'
import { Badge, DataTable, useAsync } from '../components/ui'
import { isoToJalali } from '../jalali'

export function Dashboard() {
  const summary = useAsync(() => api.get<Json>('/api/reports/dashboard'), [])
  const balances = useAsync(() => api.get<Json>('/api/reports/balances'), [])
  const invoices = useAsync(() => api.get<Json>('/api/invoices?pageSize=8'), [])
  const transfers = useAsync(() => api.get<Json>('/api/warehouse/transfers?accepted=false&pageSize=6'), [])
  const due = useAsync(() => api.get<Json>('/api/reports/cheques/due?days=30'), [])

  const stats = summary.data

  return (
    <>
      <div className="grid cols-4">
        <div className="stat">
          <div className="label">موجودی کل (بانک + صندوق)</div>
          <div className="value">{money(stats?.totalMoney)}</div>
          <div className="hint">
            بانک {money(stats?.bank)} · صندوق {money(stats?.cash)}
          </div>
        </div>
        <div className="stat">
          <div className="label">فروش این ماه</div>
          <div className="value">{money(stats?.monthSales)}</div>
          <div className="hint">خرید: {money(stats?.monthPurchases)}</div>
        </div>
        <div className="stat">
          <div className="label">حواله‌های در انتظار تأیید</div>
          <div className="value">{number(stats?.pendingTransfers)}</div>
          <Link className="hint" to="/transfers">
            بررسی صف انبار →
          </Link>
        </div>
        <div className="stat">
          <div className="label">چک‌های در جعبه</div>
          <div className="value">{number(stats?.chequesInHand)}</div>
          <div className="hint">سررسید ۳۰ روز: {number(due.data?.dueSoon?.length ?? 0)}</div>
        </div>
      </div>

      <div className="grid cols-2" style={{ marginTop: 16 }}>
        <div className="card">
          <h2>موجودی صندوق‌ها و بانک</h2>
          <DataTable
            loading={balances.loading}
            rows={balances.data?.bankAccounts ?? []}
            empty="حساب بانکی ثبت نشده است."
            columns={[
              { key: 'name', label: 'حساب', render: (row: Json) => <>{row.name} <span className="muted">({row.bankName ?? '—'})</span></> },
              { key: 'balance', label: 'موجودی', numeric: true, render: (row: Json) => money(row.balance) }
            ]}
          />
          <div style={{ marginTop: 12 }}>
            <DataTable
              rows={balances.data?.cashes ?? []}
              empty="صندوقی ثبت نشده است."
              columns={[
                { key: 'name', label: 'صندوق' },
                { key: 'balance', label: 'موجودی', numeric: true, render: (row: Json) => money(row.balance) }
              ]}
            />
          </div>
        </div>

        <div className="card">
          <h2>آخرین فاکتورها</h2>
          <DataTable
            loading={invoices.loading}
            rows={invoices.data?.items ?? []}
            empty="فاکتوری ثبت نشده است."
            columns={[
              {
                key: 'invoiceNumber',
                label: 'شماره',
                render: (row: Json) => <Link to={`/invoices/${row.id}`}>{row.invoiceNumber}</Link>
              },
              { key: 'invoiceDateG', label: 'تاریخ', render: (row: Json) => isoToJalali(row.invoiceDateG) },
              { key: 'partyName', label: 'طرف حساب' },
              { key: 'totalPrice', label: 'مبلغ', numeric: true, render: (row: Json) => money(row.totalPrice) }
            ]}
          />
        </div>
      </div>

      <div className="grid cols-2" style={{ marginTop: 16 }}>
        <div className="card">
          <h2>صف تأیید انبار</h2>
          <DataTable
            loading={transfers.loading}
            rows={transfers.data?.items ?? []}
            empty="حواله در انتظاری نیست."
            columns={[
              { key: 'productName', label: 'کالا' },
              {
                key: 'direction',
                label: 'جهت',
                render: (row: Json) => (
                  <Badge kind={row.direction === 1 ? 'ok' : 'warn'}>{row.direction === 1 ? 'ورود' : 'خروج'}</Badge>
                )
              },
              { key: 'quantity', label: 'تعداد', numeric: true, render: (row: Json) => number(row.quantity, 2) },
              { key: 'invoiceNumber', label: 'فاکتور' }
            ]}
          />
        </div>

        <div className="card">
          <h2>چک‌های نزدیک سررسید</h2>
          <DataTable
            loading={due.loading}
            rows={due.data?.dueSoon ?? []}
            empty="چکی در ۳۰ روز آینده سررسید نمی‌شود."
            columns={[
              { key: 'number', label: 'شماره' },
              { key: 'dateG', label: 'سررسید', render: (row: Json) => isoToJalali(row.dateG) },
              { key: 'recieverFullName', label: 'طرف حساب' },
              { key: 'amount', label: 'مبلغ', numeric: true, render: (row: Json) => money(row.amount) }
            ]}
          />
        </div>
      </div>
    </>
  )
}
