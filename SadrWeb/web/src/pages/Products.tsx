import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, api, money, number, qs, type Json } from '../api'
import { CrudPage, FieldInput, type FieldDef } from '../components/CrudPage'
import { AuthedImage, DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

/** Product list: stock, category and price, with the same filters the desktop grid has. */
export function Products() {
  const [term, setTerm] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [openId, setOpenId] = useState<number | null>(null)
  const [creating, setCreating] = useState(false)
  const { flash, fail, node } = useFlash()

  const categories = useOptions('/api/product-categories')
  const query = useMemo(() => qs({ q: term, categoryId, pageSize: 500 }), [term, categoryId])
  const { data, loading, error, reload } = useAsync(() => api.get<Json>(`/api/products${query}`), [query])

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <input placeholder="جستجوی نام یا کد کالا…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 260 }} />
          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} style={{ maxWidth: 220 }}>
            <option value="">همه دسته‌بندی‌ها</option>
            {categories.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setCreating(true)}>
            + کالای جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data?.items ?? []}
          onRowClick={(row) => setOpenId(row.id)}
          empty="کالایی تعریف نشده است."
          columns={[
            { key: 'name', label: 'نام کالا' },
            { key: 'code', label: 'کد' },
            { key: 'categoryName', label: 'دسته‌بندی' },
            { key: 'unitName', label: 'واحد' },
            { key: 'initialQuantity', label: 'موجودی اولیه', numeric: true, render: (row: Json) => number(row.initialQuantity, 2) },
            {
              key: 'stock',
              label: 'موجودی',
              numeric: true,
              render: (row: Json) => (
                <span className={row.stock < 0 ? 'neg' : ''}>{number(row.stock, 2)}</span>
              )
            },
            { key: 'lastPrice', label: 'آخرین قیمت', numeric: true, render: (row: Json) => money(row.lastPrice) }
          ]}
        />
      </div>

      {openId !== null && (
        <ProductFile
          productId={openId}
          onClose={() => setOpenId(null)}
          onSaved={() => {
            flash('کالا ذخیره شد.')
            reload()
          }}
          onError={fail}
        />
      )}
      {creating && (
        <CreateProduct
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false)
            reload()
            flash('کالا ایجاد شد.')
          }}
        />
      )}
      {node}
    </>
  )
}

const productFields = (categories: { value: any; label: string }[], units: { value: any; label: string }[], brands: { value: any; label: string }[]): FieldDef[] => [
  { name: 'name', label: 'نام کالا', required: true },
  { name: 'code', label: 'کد کالا', hint: 'خالی بگذارید تا خودکار ساخته شود.' },
  { name: 'mainCategoryId', label: 'دسته‌بندی اصلی', kind: 'select', options: categories, required: true },
  { name: 'mainUnitId', label: 'واحد شمارش', kind: 'select', options: units, required: true },
  { name: 'brandId', label: 'برند', kind: 'select', options: brands },
  { name: 'initialQuantity', label: 'موجودی اولیه', kind: 'number' },
  { name: 'description', label: 'توضیحات', kind: 'textarea' }
]

function CreateProduct({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const categories = useOptions('/api/product-categories')
  const units = useOptions('/api/units')
  const brands = useOptions('/api/brands')
  const fields = productFields(categories, units, brands)
  const [draft, setDraft] = useState<Json>({ initialQuantity: 0 })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const payload: Json = { ...draft }
      ;['mainCategoryId', 'mainUnitId', 'brandId'].forEach((key) => {
        payload[key] = payload[key] ? Number(payload[key]) : key === 'brandId' ? null : 0
      })
      payload.initialQuantity = Number(payload.initialQuantity ?? 0)
      await api.post('/api/products', payload)
      onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title="کالای جدید"
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save}>
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
        {fields.map((field) => (
          <FieldInput key={field.name} field={field} value={draft[field.name]} onChange={(value) => setDraft({ ...draft, [field.name]: value })} />
        ))}
      </div>
      <p className="muted" style={{ fontSize: 12 }}>
        موجودی همیشه در یک انبار نگهداری می‌شود؛ اگر هیچ انباری نباشد، «انبار پیش‌فرش» خودکار ساخته می‌شود.
      </p>
    </Modal>
  )
}

/** The product file: identity, images, attributes and price history in one dialog. */
function ProductFile({
  productId,
  onClose,
  onSaved,
  onError
}: {
  productId: number
  onClose: () => void
  onSaved: () => void
  onError: (message: string) => void
}) {
  const [tab, setTab] = useState<'main' | 'images' | 'attribs' | 'prices'>('main')
  const detail = useAsync(() => api.get<Json>(`/api/products/${productId}`), [productId])
  const categories = useOptions('/api/product-categories')
  const units = useOptions('/api/units')
  const brands = useOptions('/api/brands')
  const fields = productFields(categories, units, brands)
  const [draft, setDraft] = useState<Json | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (detail.data?.product) setDraft({ ...detail.data.product })
  }, [detail.data])

  const save = async () => {
    if (!draft) return
    try {
      await api.put(`/api/products/${productId}`, {
        ...draft,
        mainCategoryId: Number(draft.mainCategoryId),
        mainUnitId: Number(draft.mainUnitId),
        brandId: draft.brandId ? Number(draft.brandId) : null,
        initialQuantity: Number(draft.initialQuantity ?? 0)
      })
      onSaved()
      detail.reload()
    } catch (err) {
      onError(err instanceof ApiError ? err.message : String(err))
    }
  }

  const upload = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    const form = new FormData()
    Array.from(files).forEach((file) => form.append('files', file))
    try {
      await api.upload(`/api/products/${productId}/images`, form)
      detail.reload()
    } catch (err) {
      onError(err instanceof ApiError ? err.message : String(err))
    } finally {
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  const removeImage = async (productFileId: number) => {
    if (!confirm('این تصویر از کالا حذف شود؟ (فایل اصلی در سرور می‌ماند)')) return
    await api.del(`/api/products/${productId}/images/${productFileId}`)
    detail.reload()
  }

  return (
    <Modal title={`پرونده کالا — ${detail.data?.product?.name ?? ''}`} onClose={onClose} wide>
      <div className="tabs">
        <button className={tab === 'main' ? 'active' : ''} onClick={() => setTab('main')}>
          مشخصات
        </button>
        <button className={tab === 'images' ? 'active' : ''} onClick={() => setTab('images')}>
          تصاویر ({detail.data?.images?.length ?? 0})
        </button>
        <button className={tab === 'attribs' ? 'active' : ''} onClick={() => setTab('attribs')}>
          ویژگی‌ها
        </button>
        <button className={tab === 'prices' ? 'active' : ''} onClick={() => setTab('prices')}>
          قیمت‌ها
        </button>
      </div>

      {tab === 'main' && draft && (
        <>
          <div className="grid cols-2">
            {fields.map((field) => (
              <FieldInput key={field.name} field={field} value={draft[field.name]} onChange={(value) => setDraft({ ...draft, [field.name]: value })} />
            ))}
          </div>
          <div className="row" style={{ marginTop: 14 }}>
            <button className="btn primary" onClick={save}>
              ذخیره تغییرات
            </button>
            <span className="muted">
              موجودی فعلی: <strong>{number(detail.data?.stock, 2)}</strong> (اولیه {number(draft.initialQuantity, 2)})
            </span>
          </div>
        </>
      )}

      {tab === 'images' && (
        <>
          <div className="row" style={{ marginBottom: 12 }}>
            <input ref={fileInput} type="file" accept="image/*" multiple onChange={(e) => upload(e.target.files)} />
            <span className="muted">حداکثر ۸ مگابایت برای هر تصویر.</span>
          </div>
          <div className="thumbs">
            {(detail.data?.images ?? []).map((image: Json) => (
              <div className="thumb" key={image.productFileId}>
                <AuthedImage src={image.url} alt={image.originalName} />
                <div className="cap" title={image.originalName}>
                  {image.originalName}
                </div>
                <div style={{ padding: '0 6px 6px' }}>
                  <button className="btn sm danger" onClick={() => removeImage(image.productFileId)}>
                    حذف
                  </button>
                </div>
              </div>
            ))}
            {!detail.data?.images?.length && <div className="muted">تصویری برای این کالا ثبت نشده است.</div>}
          </div>
        </>
      )}

      {tab === 'attribs' && <AttributesTab productId={productId} categoryId={detail.data?.product?.mainCategoryId} rows={detail.data?.attributes ?? []} onSaved={detail.reload} />}

      {tab === 'prices' && <PricesTab productId={productId} rows={detail.data?.prices ?? []} onSaved={detail.reload} />}
    </Modal>
  )
}

function AttributesTab({
  productId,
  categoryId,
  rows,
  onSaved
}: {
  productId: number
  categoryId?: number
  rows: Json[]
  onSaved: () => void
}) {
  const categoryAttribs = useAsync(
    () => (categoryId ? api.list<Json>(`/api/category-attributes?categoryId=${categoryId}`) : Promise.resolve([])),
    [categoryId]
  )
  const definitions = useOptions('/api/attributes')
  const [values, setValues] = useState<Record<number, string>>({})

  useEffect(() => {
    const map: Record<number, string> = {}
    rows.forEach((row) => (map[row.categoryAttribId] = row.value ?? ''))
    setValues(map)
  }, [rows])

  const save = async () => {
    const payload = (categoryAttribs.data ?? []).map((row: Json) => ({
      attributeId: row.attributeId,
      categoryAttributeId: row.id,
      value: values[row.id] ?? ''
    }))
    await api.put(`/api/products/${productId}/attributes`, payload)
    onSaved()
  }

  return (
    <>
      {!categoryAttribs.data?.length && (
        <p className="muted">برای دسته‌بندی این کالا هنوز ویژگی‌ای تعریف نشده است.</p>
      )}
      <div className="grid cols-2">
        {(categoryAttribs.data ?? []).map((row: Json) => (
          <FieldShell key={row.id} label={definitions.find((d) => d.value === row.attributeId)?.label ?? `ویژگی ${row.attributeId}`} required={row.isForced}>
            <input value={values[row.id] ?? ''} onChange={(e) => setValues({ ...values, [row.id]: e.target.value })} />
          </FieldShell>
        ))}
      </div>
      {!!categoryAttribs.data?.length && (
        <button className="btn primary" style={{ marginTop: 14 }} onClick={save}>
          ذخیره ویژگی‌ها
        </button>
      )}
    </>
  )
}

function PricesTab({ productId, rows, onSaved }: { productId: number; rows: Json[]; onSaved: () => void }) {
  const units = useOptions('/api/units')
  const currencies = useOptions('/api/currencies')
  const [draft, setDraft] = useState<Json>({ unitId: '', currencyId: '', priceAmount: '', defaultDiscountPersentage: 0, setDate: todayJalali(), endDate: todayJalali() })
  const [error, setError] = useState('')

  const add = async () => {
    try {
      await api.post('/api/prices', {
        productId,
        unitId: Number(draft.unitId),
        currencyId: Number(draft.currencyId),
        priceAmount: Number(draft.priceAmount),
        defaultDiscountPersentage: Number(draft.defaultDiscountPersentage ?? 0),
        setDateG: jalaliToIso(draft.setDate),
        endDateG: jalaliToIso(draft.endDate) || jalaliToIso(draft.setDate),
        status: 0
      })
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <DataTable
        rows={rows}
        empty="قیمتی ثبت نشده است."
        columns={[
          { key: 'priceAmount', label: 'قیمت', numeric: true, render: (row: Json) => money(row.priceAmount) },
          { key: 'defaultDiscountPersentage', label: 'تخفیف پیش‌فرض ٪', numeric: true },
          { key: 'setDateG', label: 'از تاریخ', render: (row: Json) => isoToJalali(row.setDateG) },
          { key: 'endDateG', label: 'تا تاریخ', render: (row: Json) => isoToJalali(row.endDateG) }
        ]}
      />
      <hr className="sep" />
      <h2 className="section">افزودن قیمت</h2>
      <div className="grid cols-4">
        <FieldShell label="واحد" required>
          <select value={String(draft.unitId)} onChange={(e) => setDraft({ ...draft, unitId: e.target.value })}>
            <option value="">—</option>
            {units.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="واحد پول" required>
          <select value={String(draft.currencyId)} onChange={(e) => setDraft({ ...draft, currencyId: e.target.value })}>
            <option value="">—</option>
            {currencies.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="مبلغ" required>
          <input type="number" value={String(draft.priceAmount)} onChange={(e) => setDraft({ ...draft, priceAmount: e.target.value })} />
        </FieldShell>
        <FieldShell label="تخفیف پیش‌فرض ٪">
          <input type="number" value={String(draft.defaultDiscountPersentage)} onChange={(e) => setDraft({ ...draft, defaultDiscountPersentage: e.target.value })} />
        </FieldShell>
        <FieldShell label="از تاریخ">
          <input value={String(draft.setDate)} onChange={(e) => setDraft({ ...draft, setDate: e.target.value })} />
        </FieldShell>
        <FieldShell label="تا تاریخ">
          <input value={String(draft.endDate)} onChange={(e) => setDraft({ ...draft, endDate: e.target.value })} />
        </FieldShell>
      </div>
      <div className="row" style={{ marginTop: 12 }}>
        <button className="btn primary" onClick={add} disabled={!draft.unitId || !draft.currencyId || !draft.priceAmount}>
          افزودن قیمت
        </button>
        {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
      </div>
    </>
  )
}

/** Kept for the router: generic brands/units/... pages are configured in Lookups.tsx. */
export { CrudPage }
