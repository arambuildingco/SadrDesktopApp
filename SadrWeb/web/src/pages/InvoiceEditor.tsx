import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ApiError, api, money, number, type Json } from '../api'
import { DataTable, FieldShell, useAsync, useFlash, useOptions } from '../components/ui'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

type Line = {
  key: string
  id?: number
  productId: number | ''
  unitId: number | ''
  amount: number
  unitPrice: number
  extraPercent: number
  discountPercent: number
  discountAmount: number
}

const invoiceTypes = [
  { value: 0, label: 'فاکتور فروش' },
  { value: 1, label: 'فاکتور خرید' },
  { value: 2, label: 'پیش‌فاکتور' }
]
const invoiceStatuses = [
  { value: 0, label: 'پیش‌نویس' },
  { value: 1, label: 'صادر شده' },
  { value: 2, label: 'تأیید شده' },
  { value: 3, label: 'لغو شده' }
]

/** Same arithmetic as the desktop grid: price × qty × (1 + extra) × (1 − discount) − amount. */
const lineTotal = (line: Line) =>
  Math.round(line.unitPrice * line.amount * (1 + line.extraPercent / 100) * (1 - line.discountPercent / 100) - line.discountAmount)

const lineDiscount = (line: Line) => Math.round(line.unitPrice * line.amount * (line.discountPercent / 100) + line.discountAmount)

export function InvoiceEditor() {
  const { id } = useParams()
  const navigate = useNavigate()
  const isNew = !id || id === 'new'
  const invoiceId = isNew ? null : Number(id)
  const { flash, fail, node } = useFlash()

  const parties = useOptions('/api/customers-providers')
  const products = useAsync(() => api.list<Json>('/api/products?pageSize=500'), [])
  const units = useOptions('/api/units')
  const currencies = useOptions('/api/currencies')
  const accounts = useAsync(() => api.list<Json>('/api/bank-accounts'), [])
  const cashes = useAsync(() => api.list<Json>('/api/cashes'), [])

  const [header, setHeader] = useState<Json>({
    invoiceType: 0,
    status: 1,
    invoiceNumber: '',
    code: '',
    description: '',
    partyId: '',
    date: todayJalali()
  })
  const [lines, setLines] = useState<Line[]>([blankLine()])
  const [savedId, setSavedId] = useState<number | null>(invoiceId)
  const [money_, setMoney] = useState<Json>({ type: 1, system: 2, bankAccountId: '', cashId: '', value: '', dateG: todayJalali(), description: '' })
  const [error, setError] = useState('')

  const existing = useAsync(
    () => (invoiceId ? api.get<Json>(`/api/invoices/${invoiceId}`) : Promise.resolve(null)),
    [invoiceId]
  )

  useEffect(() => {
    const payload = existing.data
    if (!payload) return
    const invoice = payload.invoice
    setHeader({
      invoiceType: invoice.invoiceType,
      status: invoice.status,
      invoiceNumber: invoice.invoiceNumber,
      code: invoice.code ?? '',
      description: invoice.description ?? '',
      partyId: invoice.customerId ?? invoice.providerId ?? '',
      date: isoToJalali(invoice.invoiceDateG)
    })
    const rows: Line[] = (payload.lines ?? []).map((line: Json) => ({
      key: `l${line.id}`,
      id: line.id,
      productId: line.productId,
      unitId: line.unitId,
      amount: line.amount,
      unitPrice: line.unitPriceAmount ?? 0,
      extraPercent: line.extraPersentage ?? 0,
      discountPercent: line.discountPersentage ?? 0,
      discountAmount: (line.totalDiscount ?? 0) - Math.round(((line.unitPriceAmount ?? 0) * line.amount * (line.discountPersentage ?? 0)) / 100)
    }))
    setLines(rows.length ? rows : [blankLine()])
  }, [existing.data])

  const productById = useMemo(() => {
    const map = new Map<number, Json>()
    ;(products.data ?? []).forEach((product) => map.set(product.id, product))
    return map
  }, [products.data])

  const totals = useMemo(
    () => ({ total: lines.reduce((sum, line) => sum + lineTotal(line), 0), discount: lines.reduce((sum, line) => sum + lineDiscount(line), 0) }),
    [lines]
  )

  const pickProduct = (key: string, productId: string) => {
    const product = productById.get(Number(productId))
    setLines((rows) =>
      rows.map((row) =>
        row.key === key
          ? {
              ...row,
              productId: Number(productId),
              unitId: product?.mainUnitId ?? row.unitId,
              unitPrice: product?.lastPrice ?? row.unitPrice
            }
          : row
      )
    )
  }

  const save = async () => {
    setError('')
    const payloadLines = lines
      .filter((line) => line.productId !== '' && line.amount > 0)
      .map((line) => ({
        id: line.id ?? null,
        productId: Number(line.productId),
        unitId: Number(line.unitId || 0),
        unitPrice: Number(line.unitPrice),
        amount: Number(line.amount),
        extraPercent: Number(line.extraPercent),
        discountPercent: Number(line.discountPercent),
        discountAmount: Number(line.discountAmount),
        currencyId: currencies[0]?.value ?? null,
        priceId: null
      }))

    if (payloadLines.length === 0) {
      setError('حداقل یک ردیف کالا لازم است.')
      return
    }

    try {
      const body = {
        invoiceType: Number(header.invoiceType),
        status: Number(header.status),
        invoiceNumber: header.invoiceNumber || null,
        code: header.code || null,
        description: header.description ?? '',
        invoiceDateG: jalaliToIso(header.date),
        partyId: header.partyId === '' ? null : Number(header.partyId),
        lines: payloadLines
      }

      if (savedId) {
        await api.put(`/api/invoices/${savedId}`, body)
        flash('فاکتور به‌روزرسانی شد.')
      } else {
        const created = await api.post<Json>('/api/invoices', body)
        setSavedId(created.id)
        setHeader({ ...header, invoiceNumber: created.invoiceNumber })
        flash(`فاکتور ${created.invoiceNumber} ذخیره شد.` + (created.warnings?.length ? ` هشدار موجودی: ${created.warnings.join(' | ')}` : ''))
      }
      existing.reload()
      if (!isNew) navigate(`/invoices/${savedId}`)
      else navigate(`/invoices/${savedId}`, { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  const setStatus = async (status: number) => {
    if (!savedId) return
    await api.post(`/api/invoices/${savedId}/status?status=${status}`)
    setHeader({ ...header, status })
    flash('وضعیت فاکتور تغییر کرد.')
  }

  const remove = async () => {
    if (!savedId || !confirm('این فاکتور حذف شود؟')) return
    await api.del(`/api/invoices/${savedId}`)
    navigate('/invoices')
  }

  const pay = async () => {
    if (!savedId) return
    try {
      const isCash = Number(money_.system) === 1
      await api.post(`/api/invoices/${savedId}/money`, {
        type: Number(money_.type),
        transActionSystem: Number(money_.system),
        bankAccountId: isCash ? null : Number(money_.bankAccountId),
        cashId: isCash ? Number(money_.cashId) : null,
        value: Number(money_.value),
        dateG: jalaliToIso(money_.dateG),
        description: money_.description
      })
      flash('تراکنش مالی ثبت شد.')
      setMoney({ ...money_, value: '', description: '' })
      existing.reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  const moneyRows = existing.data?.money ?? []
  const paid = moneyRows.reduce((sum: number, row: Json) => sum + row.value, 0)

  return (
    <>
      <div className="card">
        <div className="grid cols-4">
          <FieldShell label="نوع فاکتور" required>
            <select value={String(header.invoiceType)} onChange={(e) => setHeader({ ...header, invoiceType: Number(e.target.value) })}>
              {invoiceTypes.map((type) => (
                <option key={type.value} value={type.value}>
                  {type.label}
                </option>
              ))}
            </select>
          </FieldShell>
          <FieldShell label="تاریخ">
            <input value={String(header.date)} onChange={(e) => setHeader({ ...header, date: e.target.value })} placeholder="1405/08/12" />
          </FieldShell>
          <FieldShell label="شماره فاکتور" hint="خالی = شماره خودکار">
            <input value={String(header.invoiceNumber ?? '')} onChange={(e) => setHeader({ ...header, invoiceNumber: e.target.value })} />
          </FieldShell>
          <FieldShell label="کد">
            <input value={String(header.code ?? '')} onChange={(e) => setHeader({ ...header, code: e.target.value })} />
          </FieldShell>
          <FieldShell label={Number(header.invoiceType) === 1 ? 'تأمین‌کننده' : 'مشتری'} required>
            <select value={String(header.partyId)} onChange={(e) => setHeader({ ...header, partyId: e.target.value })}>
              <option value="">— انتخاب کنید —</option>
              {parties.map((party) => (
                <option key={party.value} value={party.value}>
                  {party.label}
                </option>
              ))}
            </select>
          </FieldShell>
          <FieldShell label="وضعیت">
            <select value={String(header.status)} onChange={(e) => setHeader({ ...header, status: Number(e.target.value) })}>
              {invoiceStatuses.map((status) => (
                <option key={status.value} value={status.value}>
                  {status.label}
                </option>
              ))}
            </select>
          </FieldShell>
          <FieldShell label="توضیحات">
            <input value={String(header.description ?? '')} onChange={(e) => setHeader({ ...header, description: e.target.value })} />
          </FieldShell>
        </div>
      </div>

      <div className="card">
        <h2 className="section">ردیف‌های کالا</h2>
        <div className="table-wrap" style={{ maxHeight: 'none' }}>
          <table className="data">
            <thead>
              <tr>
                <th style={{ width: 44 }}>#</th>
                <th>کالا</th>
                <th style={{ width: 90 }}>تعداد</th>
                <th style={{ width: 120 }}>مبلغ واحد</th>
                <th style={{ width: 80 }}>اضافه ٪</th>
                <th style={{ width: 80 }}>تخفیف ٪</th>
                <th style={{ width: 110 }}>تخفیف مبلغ</th>
                <th style={{ width: 120 }}>جمع</th>
                <th style={{ width: 60 }} />
              </tr>
            </thead>
            <tbody>
              {lines.map((line, index) => (
                <tr key={line.key}>
                  <td>{index + 1}</td>
                  <td>
                    <select value={String(line.productId)} onChange={(e) => pickProduct(line.key, e.target.value)}>
                      <option value="">— انتخاب کالا —</option>
                      {(products.data ?? []).map((product) => (
                        <option key={product.id} value={product.id}>
                          {product.name}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input type="number" value={line.amount} onChange={(e) => setLines((rows) => rows.map((row) => (row.key === line.key ? { ...row, amount: Number(e.target.value) } : row)))} />
                  </td>
                  <td>
                    <input type="number" value={line.unitPrice} onChange={(e) => setLines((rows) => rows.map((row) => (row.key === line.key ? { ...row, unitPrice: Number(e.target.value) } : row)))} />
                  </td>
                  <td>
                    <input type="number" value={line.extraPercent} onChange={(e) => setLines((rows) => rows.map((row) => (row.key === line.key ? { ...row, extraPercent: Number(e.target.value) } : row)))} />
                  </td>
                  <td>
                    <input type="number" value={line.discountPercent} onChange={(e) => setLines((rows) => rows.map((row) => (row.key === line.key ? { ...row, discountPercent: Number(e.target.value) } : row)))} />
                  </td>
                  <td>
                    <input type="number" value={line.discountAmount} onChange={(e) => setLines((rows) => rows.map((row) => (row.key === line.key ? { ...row, discountAmount: Number(e.target.value) } : row)))} />
                  </td>
                  <td className="num">{money(lineTotal(line))}</td>
                  <td>
                    <button className="btn sm ghost" onClick={() => setLines((rows) => rows.filter((row) => row.key !== line.key))} disabled={lines.length === 1}>
                      ✕
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="row" style={{ marginTop: 12 }}>
          <button className="btn" onClick={() => setLines([...lines, blankLine()])}>
            + ردیف
          </button>
          <span className="spacer" />
          <span>جمع کل: <strong>{money(totals.total)}</strong></span>
          <span className="muted">تخفیف: {money(totals.discount)}</span>
        </div>

        <hr className="sep" />
        <div className="row">
          <button className="btn primary" onClick={save}>
            {savedId ? 'ذخیره تغییرات' : 'ثبت فاکتور'}
          </button>
          {savedId && (
            <>
              <button className="btn" onClick={() => setStatus(2)}>
                تأیید فاکتور
              </button>
              <button className="btn" onClick={() => setStatus(3)}>
                لغو فاکتور
              </button>
              <button className="btn danger" onClick={remove}>
                حذف
              </button>
            </>
          )}
          <span className="spacer" />
          <button className="btn" onClick={() => navigate('/invoices')}>
            بازگشت به فهرست
          </button>
        </div>
        {error && <p style={{ color: 'var(--danger)' }}>{error}</p>}
        {!units.length && <p className="muted">در حال بارگذاری واحدها…</p>}
      </div>

      {savedId && (
        <div className="grid cols-2">
          <div className="card">
            <h2 className="section">ثبت وجه این فاکتور</h2>
            <div className="grid cols-2">
              <FieldShell label="نوع">
                <select value={String(money_.type)} onChange={(e) => setMoney({ ...money_, type: Number(e.target.value) })}>
                  <option value={1}>دریافت</option>
                  <option value={2}>پرداخت</option>
                </select>
              </FieldShell>
              <FieldShell label="محل وجه">
                <select value={String(money_.system)} onChange={(e) => setMoney({ ...money_, system: Number(e.target.value) })}>
                  <option value={2}>حساب بانکی</option>
                  <option value={1}>صندوق</option>
                </select>
              </FieldShell>
              {Number(money_.system) === 2 ? (
                <FieldShell label="حساب بانکی">
                  <select value={String(money_.bankAccountId)} onChange={(e) => setMoney({ ...money_, bankAccountId: e.target.value })}>
                    <option value="">—</option>
                    {(accounts.data ?? []).map((account) => (
                      <option key={account.id} value={account.id}>
                        {account.name}
                      </option>
                    ))}
                  </select>
                </FieldShell>
              ) : (
                <FieldShell label="صندوق">
                  <select value={String(money_.cashId)} onChange={(e) => setMoney({ ...money_, cashId: e.target.value })}>
                    <option value="">—</option>
                    {(cashes.data ?? []).map((cash) => (
                      <option key={cash.id} value={cash.id}>
                        {cash.name}
                      </option>
                    ))}
                  </select>
                </FieldShell>
              )}
              <FieldShell label="مبلغ">
                <input type="number" value={String(money_.value)} onChange={(e) => setMoney({ ...money_, value: e.target.value })} />
              </FieldShell>
              <FieldShell label="تاریخ">
                <input value={String(money_.dateG)} onChange={(e) => setMoney({ ...money_, dateG: e.target.value })} />
              </FieldShell>
              <FieldShell label="توضیح">
                <input value={String(money_.description)} onChange={(e) => setMoney({ ...money_, description: e.target.value })} />
              </FieldShell>
            </div>
            <button className="btn primary" style={{ marginTop: 12 }} onClick={pay} disabled={!money_.value}>
              ثبت تراکنش
            </button>
          </div>

          <div className="card">
            <h2 className="section">پرداخت‌های ثبت‌شده</h2>
            <p className="muted">
              جمع فاکتور {money(totals.total)} · پرداخت شده {money(paid)} · مانده {money(totals.total - paid)}
            </p>
            <DataTable
              rows={moneyRows}
              empty="تراکنشی ثبت نشده است."
              columns={[
                { key: 'dateG', label: 'تاریخ', render: (row: Json) => isoToJalali(row.dateG) },
                { key: 'type', label: 'نوع', render: (row: Json) => (row.type === 1 ? 'دریافت' : row.type === 2 ? 'پرداخت' : 'انتقال') },
                { key: 'value', label: 'مبلغ', numeric: true, render: (row: Json) => money(row.value) }
              ]}
            />
            <div style={{ marginTop: 12 }}>
              <h2 className="section">حواله‌های انبار این فاکتور</h2>
              <DataTable
                rows={existing.data?.transfers ?? []}
                empty="حواله‌ای برای این فاکتور ثبت نشده است."
                columns={[
                  { key: 'direction', label: 'جهت', render: (row: Json) => (row.direction === 1 ? 'ورود' : 'خروج') },
                  { key: 'quantity', label: 'تعداد', numeric: true, render: (row: Json) => number(row.quantity, 2) },
                  { key: 'accepted', label: 'وضعیت', render: (row: Json) => (row.accepted ? 'تأیید شده' : 'در انتظار') }
                ]}
              />
            </div>
          </div>
        </div>
      )}
      {node}
    </>
  )
}

let counter = 0
function blankLine(): Line {
  counter += 1
  return {
    key: `n${Date.now()}_${counter}`,
    productId: '',
    unitId: '',
    amount: 1,
    unitPrice: 0,
    extraPercent: 0,
    discountPercent: 0,
    discountAmount: 0
  }
}
