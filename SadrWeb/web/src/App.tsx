import { Navigate, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { useAuth } from './auth'
import { Dashboard } from './pages/Dashboard'
import { Invoices } from './pages/Invoices'
import { InvoiceEditor } from './pages/InvoiceEditor'
import { Products } from './pages/Products'
import { Stock } from './pages/Stock'
import { Transfers } from './pages/Transfers'
import { Cheques } from './pages/Cheques'
import { BankAccounts, BankTransactions } from './pages/Banking'
import { Cashes, MoneyTransactions } from './pages/Money'
import { People, Companies } from './pages/People'
import { Projects } from './pages/Projects'
import { Accounting } from './pages/Accounting'
import { Users } from './pages/Users'
import { Settings } from './pages/Settings'
import { Login } from './pages/Login'
import {
  AccountGroups,
  Attributes,
  Banks,
  BankBranches,
  Brands,
  Currencies,
  CustomersProviders,
  DetailAccounts,
  GeneralAccounts,
  Prices,
  PrintSettings,
  ProductCategories,
  ProjectCategories,
  Roles,
  SubsidiaryAccounts,
  Units,
  Warehouses
} from './pages/Lookups'

export default function App() {
  const { user } = useAuth()

  if (!user) {
    return (
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    )
  }

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/invoices" element={<Invoices />} />
        <Route path="/invoices/:id" element={<InvoiceEditor />} />
        <Route path="/products" element={<Products />} />
        <Route path="/stock" element={<Stock />} />
        <Route path="/transfers" element={<Transfers />} />
        <Route path="/cheques" element={<Cheques />} />
        <Route path="/bank-accounts" element={<BankAccounts />} />
        <Route path="/bank-transactions" element={<BankTransactions />} />
        <Route path="/cashes" element={<Cashes />} />
        <Route path="/money" element={<MoneyTransactions />} />
        <Route path="/people" element={<People />} />
        <Route path="/companies" element={<Companies />} />
        <Route path="/projects" element={<Projects />} />
        <Route path="/accounting" element={<Accounting />} />
        <Route path="/users" element={<Users />} />
        <Route path="/settings" element={<Settings />} />

        <Route path="/product-categories" element={<ProductCategories />} />
        <Route path="/project-categories" element={<ProjectCategories />} />
        <Route path="/customers-providers" element={<CustomersProviders />} />
        <Route path="/brands" element={<Brands />} />
        <Route path="/units" element={<Units />} />
        <Route path="/currencies" element={<Currencies />} />
        <Route path="/warehouses" element={<Warehouses />} />
        <Route path="/attributes" element={<Attributes />} />
        <Route path="/banks" element={<Banks />} />
        <Route path="/bank-branches" element={<BankBranches />} />
        <Route path="/prices" element={<Prices />} />
        <Route path="/print-settings" element={<PrintSettings />} />
        <Route path="/roles" element={<Roles />} />
        <Route path="/account-groups" element={<AccountGroups />} />
        <Route path="/general-accounts" element={<GeneralAccounts />} />
        <Route path="/subsidiary-accounts" element={<SubsidiaryAccounts />} />
        <Route path="/detail-accounts" element={<DetailAccounts />} />

        <Route path="/login" element={<Navigate to="/" replace />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
