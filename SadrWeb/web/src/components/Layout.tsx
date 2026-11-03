import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import { useFlash } from './ui'

type NavItem = { to: string; label: string }
type NavGroup = { title: string; items: NavItem[] }

const groups: NavGroup[] = [
  {
    title: 'عملیات',
    items: [
      { to: '/', label: '🏠 داشبورد' },
      { to: '/invoices', label: '🧾 فاکتورها' },
      { to: '/products', label: '📦 کالاها' },
      { to: '/stock', label: '📊 موجودی انبار' },
      { to: '/transfers', label: '🔄 حواله‌های انبار' }
    ]
  },
  {
    title: 'مالی',
    items: [
      { to: '/cheques', label: '💳 چک‌ها' },
      { to: '/bank-accounts', label: '🏦 حساب‌های بانکی' },
      { to: '/bank-transactions', label: '📒 دفتر بانکی' },
      { to: '/cashes', label: '💰 صندوق‌ها' },
      { to: '/money', label: '💵 تراکنش‌های مالی' }
    ]
  },
  {
    title: 'طرف‌های حساب',
    items: [
      { to: '/people', label: '👤 اشخاص' },
      { to: '/companies', label: '🏢 شرکت‌ها' },
      { to: '/customers-providers', label: '🤝 مشتریان و تأمین‌کنندگان' }
    ]
  },
  {
    title: 'پروژه‌ها',
    items: [
      { to: '/projects', label: '📁 پروژه‌ها' },
      { to: '/project-categories', label: '🗂 دسته‌بندی پروژه' }
    ]
  },
  {
    title: 'حسابداری',
    items: [
      { to: '/accounting', label: '🧮 ساختار حساب‌ها' },
      { to: '/account-groups', label: 'گروه‌های حساب' },
      { to: '/general-accounts', label: 'حساب‌های کل' },
      { to: '/subsidiary-accounts', label: 'حساب‌های معین' },
      { to: '/detail-accounts', label: 'حساب‌های تفصیلی' }
    ]
  },
  {
    title: 'تعریف‌های پایه',
    items: [
      { to: '/product-categories', label: 'دسته‌بندی کالا' },
      { to: '/brands', label: 'برندها' },
      { to: '/units', label: 'واحدهای شمارش' },
      { to: '/currencies', label: 'واحدهای پول' },
      { to: '/warehouses', label: 'انبارها' },
      { to: '/attributes', label: 'ویژگی‌های کالا' },
      { to: '/banks', label: 'بانک‌ها' },
      { to: '/bank-branches', label: 'شعب بانک' },
      { to: '/prices', label: 'قیمت‌ها' },
      { to: '/print-settings', label: 'قالب‌های چاپ' },
      { to: '/roles', label: 'نقش‌ها' }
    ]
  },
  {
    title: 'سیستم',
    items: [
      { to: '/users', label: '🔐 کاربران' },
      { to: '/settings', label: '⚙️ تنظیمات من' }
    ]
  }
]

const titles: Record<string, string> = {
  '/': 'داشبورد',
  '/invoices': 'فاکتورها',
  '/products': 'کالاها',
  '/stock': 'موجودی انبار',
  '/transfers': 'حواله‌های انبار',
  '/cheques': 'چک‌ها',
  '/bank-accounts': 'حساب‌های بانکی',
  '/bank-transactions': 'دفتر تراکنش‌های بانکی',
  '/cashes': 'صندوق‌ها',
  '/money': 'تراکنش‌های مالی',
  '/people': 'اشخاص',
  '/companies': 'شرکت‌ها',
  '/customers-providers': 'مشتریان و تأمین‌کنندگان',
  '/projects': 'پروژه‌ها',
  '/project-categories': 'دسته‌بندی پروژه',
  '/accounting': 'ساختار حساب‌ها',
  '/users': 'کاربران برنامه',
  '/settings': 'تنظیمات من'
}

export function Layout() {
  const { user, logout, can } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { node } = useFlash()

  const title =
    titles[location.pathname] ??
    (location.pathname.startsWith('/invoices/')
      ? 'ویرایش فاکتور'
      : location.pathname.startsWith('/products/')
        ? 'پرونده کالا'
        : 'صدر')

  const visibleGroups = groups
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => item.to !== '/users' || can('manageUsers'))
    }))
    .filter((group) => group.items.length > 0)

  return (
    <div className="layout">
      <aside className="sidebar">
        <div className="brand">
          صدر
          <small>سامانه یکپارچه مدیریت — نسخه وب</small>
        </div>
        <nav className="nav">
          {visibleGroups.map((group) => (
            <div className="nav-group" key={group.title}>
              <div className="nav-group-title">{group.title}</div>
              {group.items.map((item) => (
                <NavLink key={item.to} to={item.to} end={item.to === '/'}>
                  {item.label}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
      </aside>

      <div className="main">
        <header className="topbar">
          <h1>{title}</h1>
          <div className="who">
            <span>
              {user?.username} · {user?.role}
            </span>
            <button
              className="btn sm"
              onClick={async () => {
                await logout()
                navigate('/login')
              }}
            >
              خروج
            </button>
          </div>
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
      {node}
    </div>
  )
}
