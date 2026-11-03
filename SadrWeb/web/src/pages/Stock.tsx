import { useMemo, useState } from 'react'
import { api, number, qs, type Json } from '../api'
import { DataTable, useAsync, useOptions } from '../components/ui'

/** Stock report: opening + accepted movements, plus what pending transfers would make it. */
export function Stock() {
  const [warehouseId, setWarehouseId] = useState('')
  const [term, setTerm] = useState('')
  const warehouses = useOptions('/api/warehouses')

  const query = useMemo(() => qs({ warehouseId, q: term }), [warehouseId, term])
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/warehouse/stock${query}`), [query])

  return (
    <div className="card">
      <div className="grid cols-4" style={{ marginBottom: 14 }}>
        <div className="stat">
          <div className="label">تعداد کالا</div>
          <div className="value">{number(data?.totals?.products)}</div>
        </div>
        <div className="stat">
          <div className="label">جمع موجودی</div>
          <div className="value">{number(data?.totals?.stock, 2)}</div>
        </div>
        <div className="stat">
          <div className="label">با احتساب در انتظارها</div>
          <div className="value">{number(data?.totals?.projected, 2)}</div>
        </div>
        <div className="stat">
          <div className="label">کالاهای منفی</div>
          <div className="value" style={{ color: data?.totals?.negative ? 'var(--danger)' : undefined }}>
            {number(data?.totals?.negative)}
          </div>
        </div>
      </div>

      <div className="toolbar">
        <input placeholder="جستجوی کالا…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 240 }} />
        <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} style={{ maxWidth: 200 }}>
          <option value="">همه انبارها</option>
          {warehouses.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <span className="spacer" />
        <button className="btn" onClick={reload}>
          بازخوانی
        </button>
      </div>

      {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}

      <DataTable
        loading={loading}
        rows={data?.items ?? []}
        empty="کالایی ثبت نشده است."
        columns={[
          { key: 'name', label: 'کالا' },
          { key: 'code', label: 'کد' },
          { key: 'unit', label: 'واحد' },
          { key: 'opening', label: 'موجودی اولیه', numeric: true, render: (row: Json) => number(row.opening, 2) },
          {
            key: 'stock',
            label: 'موجودی',
            numeric: true,
            render: (row: Json) => <span className={row.stock < 0 ? 'neg' : ''}>{number(row.stock, 2)}</span>
          },
          { key: 'pending', label: 'در انتظار', numeric: true, render: (row: Json) => number(row.pending, 2) },
          { key: 'projected', label: 'پیش‌بینی', numeric: true, render: (row: Json) => number(row.projected, 2) }
        ]}
      />
    </div>
  )
}
