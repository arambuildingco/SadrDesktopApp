import { useMemo, useState } from 'react'
import { ApiError, api, number, qs, type Json } from '../api'
import { Badge, DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'
import { useAuth } from '../auth'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

/** Warehouse queue: every movement starts pending and only a keeper confirms it. */
export function Transfers() {
  const { can } = useAuth()
  const isKeeper = can('acceptTransfers')
  const [direction, setDirection] = useState('')
  const [accepted, setAccepted] = useState('false')
  const [creating, setCreating] = useState(false)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ direction, accepted, pageSize: 300 }), [direction, accepted])
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/warehouse/transfers${query}`), [query])

  const setAccept = async (row: Json, value: boolean) => {
    try {
      await api.post(`/api/warehouse/transfers/${row.id}/accept?accept=${value}`)
      flash(value ? 'حواله تأیید شد و موجودی به‌روزرسانی شد.' : 'تأیید حواله لغو شد.')
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <select value={direction} onChange={(e) => setDirection(e.target.value)} style={{ maxWidth: 150 }}>
            <option value="">همه جهت‌ها</option>
            <option value="1">ورود</option>
            <option value="2">خروج</option>
          </select>
          <select value={accepted} onChange={(e) => setAccepted(e.target.value)} style={{ maxWidth: 170 }}>
            <option value="false">در انتظار تأیید</option>
            <option value="true">تأیید شده</option>
            <option value="">همه</option>
          </select>
          <span className="spacer" />
          {!isKeeper && <span className="muted">تأیید حواله فقط توسط انباردار یا مدیر انجام می‌شود.</span>}
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setCreating(true)}>
            + حواله جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}

        <DataTable
          loading={loading}
          rows={data?.items ?? []}
          empty="حواله‌ای با این فیلترها نیست."
          columns={[
            { key: 'productName', label: 'کالا' },
            {
              key: 'direction',
              label: 'جهت',
              render: (row: Json) => <Badge kind={row.direction === 1 ? 'ok' : 'warn'}>{row.direction === 1 ? 'ورود' : 'خروج'}</Badge>
            },
            { key: 'quantity', label: 'تعداد', numeric: true, render: (row: Json) => number(row.quantity, 2) },
            { key: 'transferDateG', label: 'تاریخ انجام', render: (row: Json) => isoToJalali(row.transferDateG) },
            { key: 'warehouseName', label: 'انبار' },
            { key: 'invoiceNumber', label: 'فاکتور' },
            {
              key: 'accepted',
              label: 'وضعیت',
              render: (row: Json) => <Badge kind={row.accepted ? 'ok' : 'warn'}>{row.accepted ? 'تأیید شده' : 'در انتظار'}</Badge>
            },
            {
              key: '__actions',
              label: '',
              render: (row: Json) =>
                isKeeper ? (
                  <div className="row">
                    {row.accepted ? (
                      <button className="btn sm" onClick={() => setAccept(row, false)}>
                        لغو تأیید
                      </button>
                    ) : (
                      <button className="btn sm primary" onClick={() => setAccept(row, true)}>
                        تأیید
                      </button>
                    )}
                  </div>
                ) : null
            }
          ]}
        />
      </div>

      {creating && (
        <TransferForm
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            reload()
            flash('حواله ثبت شد و در صف تأیید قرار گرفت.')
          }}
        />
      )}
      {node}
    </>
  )
}

function TransferForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const products = useOptions('/api/products?pageSize=500')
  const warehouses = useOptions('/api/warehouses')
  const people = useOptions('/api/people')
  const [form, setForm] = useState<Json>({ direction: 1, quantity: 1, date: todayJalali(), productId: '', wareHouseId: '', personId: '', describtion: '' })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      await api.post('/api/warehouse/transfers', {
        productId: Number(form.productId),
        direction: Number(form.direction),
        quantity: Number(form.quantity),
        transferDateG: jalaliToIso(form.date),
        wareHouseId: form.wareHouseId ? Number(form.wareHouseId) : null,
        personId: form.personId ? Number(form.personId) : null,
        description: form.describtion
      })
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title="حواله انبار جدید"
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.productId}>
            ثبت حواله
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <div className="grid cols-2">
        <FieldShell label="نوع حواله" required>
          <select value={String(form.direction)} onChange={(e) => setForm({ ...form, direction: Number(e.target.value) })}>
            <option value={1}>ورود (به انبار)</option>
            <option value={2}>خروج (از انبار)</option>
          </select>
        </FieldShell>
        <FieldShell label="کالا" required>
          <select value={String(form.productId)} onChange={(e) => setForm({ ...form, productId: e.target.value })}>
            <option value="">—</option>
            {products.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="تعداد" required>
          <input type="number" value={String(form.quantity)} onChange={(e) => setForm({ ...form, quantity: e.target.value })} />
        </FieldShell>
        <FieldShell label="تاریخ انجام واقعی">
          <input value={String(form.date)} onChange={(e) => setForm({ ...form, date: e.target.value })} />
        </FieldShell>
        <FieldShell label="انبار">
          <select value={String(form.wareHouseId)} onChange={(e) => setForm({ ...form, wareHouseId: e.target.value })}>
            <option value="">— پیش‌فرض —</option>
            {warehouses.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="شخص مرتبط">
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
          <textarea value={String(form.describtion)} onChange={(e) => setForm({ ...form, describtion: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}
