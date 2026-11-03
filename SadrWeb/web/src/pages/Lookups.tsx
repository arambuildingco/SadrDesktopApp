import type { Json } from '../api'
import { CrudPage, type FieldDef } from '../components/CrudPage'

/** Shared field fragments so every lookup form looks and behaves the same. */
const name: FieldDef = { name: 'name', label: 'نام', required: true }
const code: FieldDef = { name: 'code', label: 'کد', hint: 'کد تکراری مجاز نیست.' }
const description: FieldDef = { name: 'description', label: 'توضیحات', kind: 'textarea' }

const accountTypes = [
  { value: 0, label: 'بدهکار' },
  { value: 1, label: 'بستانکار' },
  { value: 2, label: 'جهت‌دار (هر دو)' }
]

const cols = (...keys: [string, string][]) =>
  keys.map(([key, label]) => ({ key, label, render: (row: Json) => String(row[key] ?? '—') }))

/* ------------------------------------------------------------------ base lookups */
export function Brands() {
  return (
    <CrudPage
      title="برند"
      endpoint="/api/brands"
      fields={[name, code, description]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['description', 'توضیحات'])}
    />
  )
}

export function Units() {
  return (
    <CrudPage
      title="واحد شمارش"
      endpoint="/api/units"
      fields={[
        name,
        description,
        { name: 'parentUnitId', label: 'واحد بالاتر', kind: 'select', optionsUrl: '/api/units' },
        { name: 'parentPercentRel', label: 'نسبت به واحد بالاتر', kind: 'number' }
      ]}
      columns={cols(['name', 'نام'], ['description', 'توضیحات'], ['parentPercentRel', 'نسبت'])}
    />
  )
}

export function Currencies() {
  return (
    <CrudPage
      title="واحد پول"
      endpoint="/api/currencies"
      fields={[name, code, description]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['description', 'توضیحات'])}
    />
  )
}

export function Roles() {
  return (
    <CrudPage
      title="نقش"
      endpoint="/api/roles"
      fields={[name, description, { name: 'isActive', label: 'فعال', kind: 'checkbox', default: true }]}
      columns={cols(['name', 'نام'], ['description', 'توضیحات'], ['isActive', 'فعال'])}
    />
  )
}

export function Warehouses() {
  return (
    <CrudPage
      title="انبار"
      endpoint="/api/warehouses"
      intro="موجودی کالاها همیشه در یک انبار نگهداری می‌شود؛ اگر همه انبارها حذف شوند، «انبار پیش‌فرش» خودکار ساخته می‌شود."
      fields={[
        name,
        code,
        { name: 'description', label: 'توضیحات' },
        { name: 'address', label: 'آدرس' },
        { name: 'tel', label: 'تلفن' },
        { name: 'active', label: 'فعال', kind: 'checkbox', default: true }
      ]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['address', 'آدرس'], ['tel', 'تلفن'], ['active', 'فعال'])}
    />
  )
}

export function Banks() {
  return (
    <CrudPage
      title="بانک"
      endpoint="/api/banks"
      fields={[name, code, description]}
      columns={cols(['name', 'نام'], ['code', 'کد'])}
    />
  )
}

export function BankBranches() {
  return (
    <CrudPage
      title="شعبه بانک"
      endpoint="/api/bank-branches"
      fields={[
        name,
        code,
        { name: 'bankId', label: 'بانک', kind: 'select', optionsUrl: '/api/banks', required: true },
        { name: 'managerPersonId', label: 'مدیر شعبه', kind: 'select', optionsUrl: '/api/people', optionLabel: 'fullName' },
        { name: 'address', label: 'آدرس' },
        { name: 'tel', label: 'تلفن' }
      ]}
      columns={cols(['name', 'نام شعبه'], ['code', 'کد'], ['address', 'آدرس'], ['tel', 'تلفن'])}
    />
  )
}

export function Attributes() {
  return (
    <CrudPage
      title="ویژگی کالا"
      endpoint="/api/attributes"
      fields={[name, { name: 'description', label: 'توضیحات', kind: 'textarea' }]}
      columns={cols(['name', 'نام'], ['description', 'توضیحات'])}
    />
  )
}

export function ProductCategories() {
  return (
    <CrudPage
      title="دسته‌بندی کالا"
      endpoint="/api/product-categories"
      fields={[name, code, description, { name: 'parentId', label: 'دسته‌بندی بالاتر', kind: 'select', optionsUrl: '/api/product-categories' }]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['description', 'توضیحات'])}
      intro="ویژگی‌های هر دسته‌بندی از تب ویژگی‌های کالا و از API /api/category-attributes مدیریت می‌شود."
    />
  )
}

export function ProjectCategories() {
  return (
    <CrudPage
      title="دسته‌بندی پروژه"
      endpoint="/api/project-categories"
      fields={[name, code, description, { name: 'parentId', label: 'دسته‌بندی بالاتر', kind: 'select', optionsUrl: '/api/project-categories' }]}
      columns={cols(['name', 'نام'], ['code', 'کد'])}
    />
  )
}

export function CustomersProviders() {
  return (
    <CrudPage
      title="مشتری / تأمین‌کننده"
      endpoint="/api/customers-providers"
      intro="ردیف‌های اشخاص و شرکت‌ها به‌طور خودکار در این جدول ساخته می‌شوند تا بتوانند طرف حساب فاکتور باشند."
      fields={[
        name,
        code,
        description,
        { name: 'personId', label: 'شخص مرتبط', kind: 'select', optionsUrl: '/api/people', optionLabel: 'fullName' },
        { name: 'companyId', label: 'شرکت مرتبط', kind: 'select', optionsUrl: '/api/companies', optionLabel: 'fullName' }
      ]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['description', 'توضیحات'])}
    />
  )
}

/* ------------------------------------------------------------------ accounting */
export function AccountGroups() {
  return (
    <CrudPage
      title="گروه حساب"
      endpoint="/api/account-groups"
      fields={[name, code, { name: 'accountType', label: 'ماهیت حساب', kind: 'select', options: accountTypes, required: true }, { name: 'description', label: 'توضیحات' }]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['accountType', 'نوع'])}
    />
  )
}

export function GeneralAccounts() {
  return (
    <CrudPage
      title="حساب کل"
      endpoint="/api/general-accounts"
      fields={[
        name,
        code,
        { name: 'accountGroupId', label: 'گروه حساب', kind: 'select', optionsUrl: '/api/account-groups', required: true },
        { name: 'accountType', label: 'ماهیت حساب', kind: 'select', options: accountTypes, required: true },
        { name: 'description', label: 'توضیحات' }
      ]}
      columns={cols(['name', 'نام'], ['code', 'کد'])}
    />
  )
}

export function SubsidiaryAccounts() {
  return (
    <CrudPage
      title="حساب معین"
      endpoint="/api/subsidiary-accounts"
      fields={[
        name,
        code,
        { name: 'generalAccountId', label: 'حساب کل', kind: 'select', optionsUrl: '/api/general-accounts', required: true },
        { name: 'accountType', label: 'ماهیت حساب', kind: 'select', options: accountTypes, required: true },
        { name: 'description', label: 'توضیحات' }
      ]}
      columns={cols(['name', 'نام'], ['code', 'کد'])}
    />
  )
}

export function DetailAccounts() {
  return (
    <CrudPage
      title="حساب تفصیلی"
      endpoint="/api/detail-accounts"
      fields={[
        name,
        code,
        { name: 'subSidiaryAccountId', label: 'حساب معین', kind: 'select', optionsUrl: '/api/subsidiary-accounts', required: true },
        { name: 'accountType', label: 'ماهیت حساب', kind: 'select', options: accountTypes, required: true },
        { name: 'description', label: 'توضیحات' }
      ]}
      columns={cols(['name', 'نام'], ['code', 'کد'], ['description', 'توضیحات'])}
    />
  )
}

/* ------------------------------------------------------------------ prices & print */
export function Prices() {
  return (
    <CrudPage
      title="قیمت"
      endpoint="/api/prices"
      searchable={false}
      intro="قیمت‌ها را می‌توان از پرونده هر کالا هم ثبت کرد. هر قیمت به کالا، واحد و واحد پول وابسته است."
      fields={[
        { name: 'productId', label: 'کالا', kind: 'select', optionsUrl: '/api/products?pageSize=500', required: true },
        { name: 'unitId', label: 'واحد', kind: 'select', optionsUrl: '/api/units', required: true },
        { name: 'currencyId', label: 'واحد پول', kind: 'select', optionsUrl: '/api/currencies', required: true },
        { name: 'priceAmount', label: 'مبلغ', kind: 'money', required: true },
        { name: 'defaultDiscountPersentage', label: 'تخفیف پیش‌فرض ٪', kind: 'number' },
        { name: 'setDateG', label: 'از تاریخ', kind: 'date' },
        { name: 'endDateG', label: 'تا تاریخ', kind: 'date' },
        { name: 'status', label: 'وضعیت', kind: 'select', options: [{ value: 0, label: 'فعال' }, { value: 1, label: 'غیرفعال' }] }
      ]}
      columns={cols(['priceAmount', 'مبلغ'], ['defaultDiscountPersentage', 'تخفیف ٪'], ['status', 'وضعیت'])}
      canDelete
    />
  )
}

export function PrintSettings() {
  return (
    <CrudPage
      title="قالب چاپ"
      endpoint="/api/invoice-print-settings"
      fields={[
        name,
        {
          name: 'layout',
          label: 'چیدمان',
          kind: 'select',
          options: [
            { value: 0, label: 'کلاسیک رسمی' },
            { value: 1, label: 'ساده و مدرن' },
            { value: 2, label: 'کامپکت' }
          ],
          required: true
        },
        { name: 'isDefault', label: 'قالب پیش‌فرض', kind: 'checkbox' },
        { name: 'titleOverride', label: 'عنوان چاپی' },
        { name: 'footerNote', label: 'یادداشت پاصفحه' },
        { name: 'showDiscountColumn', label: 'نمایش ستون تخفیف', kind: 'checkbox', default: true }
      ]}
      columns={cols(['name', 'نام'], ['layout', 'چیدمان'], ['titleOverride', 'عنوان'], ['isDefault', 'پیش‌فرض'])}
    />
  )
}
