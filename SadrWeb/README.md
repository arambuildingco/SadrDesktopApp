# SadrWeb — سامانه وب صدر

A web application built on the same data model and business rules as the Windows desktop
application (`SadrApp`), exposed through a documented REST API so a desktop or mobile client
can use the same data later.

Everything the desktop app can do — invoices, warehouse transfers, cheques, bank accounts,
cash boxes, money movements, products with images and attributes, projects and tasks, the
chart of accounts, people/companies, users — is available here over HTTP.

---

## 1. Stack and why

| Layer | Choice | Reason |
| --- | --- | --- |
| API | **ASP.NET Core 10 minimal API** | The desktop app is C#/EF Core. The entity classes, the Persian date helpers, the cheque and cash ledgers and the stock rules were reused **verbatim**, so the web app cannot drift from the desktop's behaviour. |
| ORM | **EF Core 10** | Same model configuration as `SadrApp/Data/SadrDbContext.cs`. |
| Database | **SQLite** by default, **SQL Server** switchable | Zero-setup demo, `Database:Provider=SqlServer` points at the *existing* desktop database. |
| Auth | **JWT access + rotating refresh tokens**, PBKDF2 passwords | Works for browsers, desktop and mobile alike (no cookie dependency). Roles are the desktop's own: `Admin`, `Warehouse`, `Sales`, `User`. |
| Docs | **OpenAPI 3** at `/openapi/v1.json` | Any client generator (C#, Kotlin, Swift, Dart, TypeScript) can build its SDK from it. |
| UI | **React 19 + TypeScript + Vite**, Persian RTL | Fast SPA; a metadata-driven CRUD layer mirrors the desktop's generic `FieldEditorWindow`, with bespoke screens for the complex forms. |

```
   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
   │  Browser SPA │   │ WPF desktop  │   │   Mobile     │
   │ React (this) │   │  (existing)  │   │  (future)    │
   └──────┬───────┘   └──────┬───────┘   └──────┬───────┘
          │  HTTPS / JSON, Bearer token        │
          └────────────────┬───────────────────┘
                           ▼
              ┌────────────────────────────┐
              │  SadrWeb.Api (REST)        │
              │  auth · rules · ledgers    │
              └─────────────┬──────────────┘
                            ▼
              ┌────────────────────────────┐
              │ EF Core → SQLite default   │
              │        → SQL Server option │
              └────────────────────────────┘
```

---

## 2. Running it

> **Step-by-step guide (install, configuration, SQL Server, deployment, troubleshooting):
> [`docs/RUN.md`](docs/RUN.md).** The short version follows.

### Development (two processes)

```bash
# terminal 1 — API on :5199
cd SadrWeb/api
dotnet run --urls http://0.0.0.0:5199

# terminal 2 — SPA on :5173 (proxies /api to :5199)
cd SadrWeb/web
npm install
npm run dev
```

### Single process (what ships)

```bash
cd SadrWeb/web && npm run build            # → web/dist
cd .. && ./build.sh                        # copies dist into api/wwwroot and builds the API
cd api && dotnet run --urls http://0.0.0.0:5199
```

Open <http://localhost:5199>. The API serves the built SPA and falls back to `index.html`
for client-side routes; everything under `/api` stays the REST API.

### First sign-in

The first start creates an administrator. Either set the password explicitly:

```bash
export Seed__AdminPassword='<your-password>'
```

or let it generate one and read `api/App_Data/admin-password.txt` (username `admin`).
Change it after the first sign-in (تنظیمات من → تغییر رمز).

### Database

| Setting | Default | Notes |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | Set to `SqlServer` to use SQL Server. |
| `ConnectionStrings:Sqlite` | *(empty → `App_Data/sadr.db`)* | Relative to the content root. |
| `ConnectionStrings:SqlServer` | `localhost\SQLEXPRESS / SadrDb` | Point it at the desktop app's database to share data. |
| `Files:Root` | *(empty → `App_Data/UploadedFiles`)* | Product images. |
| `Jwt:Key` | *(empty → generated once in `App_Data/jwt.key`)* | Set it in production so tokens survive redeploys. |

The schema is created with `EnsureCreated()`; there are no migrations to apply. Tables are
the desktop app's tables — including `Deleted` (soft delete) and the audit columns.

---

## 3. Feature map (desktop → web → API)

| Desktop page (`SadrApp/Views/Pages`) | Web page | REST endpoints |
| --- | --- | --- |
| `InvoiceListPage` + `InvoiceEditorWindow` | `/invoices`, `/invoices/:id` | `GET/POST /api/invoices`, `GET/PUT/DELETE /api/invoices/{id}`, `POST /api/invoices/{id}/status`, `POST /api/invoices/{id}/money`, `GET /api/invoices/{id}/balance` |
| `ProductListPage` + `ProductImagesWindow` + attributes/prices dialogs | `/products` (پرونده کالا) | `/api/products`, `/api/products/{id}`, `/api/products/{id}/images`, `/api/products/{id}/attributes`, `/api/products/{id}/extra-categories`, `/api/prices`, `/api/files/{id}` |
| `WarehouseTransfersPage` | `/transfers` | `/api/warehouse/transfers`, `POST .../{id}/accept`, `DELETE .../{id}` |
| `WarehouseStockReportPage` | `/stock` | `GET /api/warehouse/stock`, `GET /api/warehouse/stock/{productId}` |
| `ChequesPage` + `ChequeDashboardPage` | `/cheques` | `/api/cheques`, `POST /api/cheques/{id}/status`, `GET /api/reports/cheques/due` |
| `BankAccountsPage` | `/bank-accounts` | `/api/bank-accounts` |
| `BankTransactionsPage` | `/bank-transactions` | `/api/bank-transactions` |
| `CashesPage` | `/cashes` | `/api/cashes` |
| `AccountTransactionsPage` | `/money` | `/api/account-transactions`, `POST /api/account-transactions/transfer` |
| `BalancesDashboardPage` | `/` (dashboard) | `GET /api/reports/balances`, `GET /api/reports/dashboard` |
| `PeopleListPage` | `/people` | `/api/people` |
| `CompanyListPage` | `/companies` | `/api/companies` |
| `ProjectListPage` + `TaskListPage` + `TaskReportsWindow` | `/projects` | `/api/projects`, `/api/projects/{id}/tasks`, `/api/tasks`, `POST /api/tasks/{id}/progress`, `/api/task-reports` |
| `AccountingPage` (4 levels) | `/accounting` + 4 CRUD pages | `/api/accounting/tree`, `/api/accounting/summary`, `/api/account-{groups,}`, `/api/general-accounts`, `/api/subsidiary-accounts`, `/api/detail-accounts` |
| `CategoryTreePage` (products) | `/product-categories` | `/api/product-categories`, `/api/category-attributes` |
| `CategoryTreePage` (projects) | `/project-categories` | `/api/project-categories` |
| `SimpleListPage` (brands/units/currencies/warehouses/banks/attributes/roles/customers-providers/print settings) | same-named pages | one CRUD endpoint each |
| `UserListPage` | `/users` | `/api/users`, `POST /api/users/{id}/password` |
| `SettingsPage` / `FontSettingsWindow` / `PrintSettingsWindow` | `/settings`, `/print-settings` | `/api/auth/change-password`, `/api/invoice-print-settings` |
| `FirstRunSetupWindow` / `ConnectionSettingsWindow` / `BackupWindow` / `MnuDbUpgrade` | — | Server-side concerns now: configuration, database file/connection, backups. There is no in-app backup screen yet (see §7). |

---

## 4. Business rules ported from the desktop app

These are not re-implemented by guesswork — the C# sources were copied from `SadrApp` into
`api/Data` and `api/Infrastructure` and are the same code the desktop runs.

- **Invoice line math** — `price × qty × (1 + extra%) × (1 − discount%) − discountAmount`,
  rounded to whole rials; the invoice total is the sum of the lines.
- **Invoice numbering** — `S-1405-0001` (sell), `B-…` (buy), `P-…` (pre-invoice), per type per
  Jalali year; a manual number always wins.
- **Stock** — `product.InitialQuantity + Σ accepted transfers (in − out)`. Pending transfers
  never move stock. Saving a buy/sell invoice (re)queues one **pending** transfer per line; a
  warehouse keeper accepts it with the real physical date (`CanAcceptTransfers`).
- **Default warehouse** — creating a product when no warehouse exists creates
  «انبار پیشفرش» automatically (`DefaultWarehouse.EnsureAsync`).
- **Sell-invoice stock warning** — the API returns `warnings` when the projected stock would
  go negative; the sale is still allowed, exactly like the desktop message box.
- **Cheque ledger** — وصول شد → deposit (received) / withdrawal (issued); خرج شد → withdrawal;
  برگشت خورده یا ابطال → the posted effect is removed. Balances are always recomputed from
  history, so they can never drift (`ChequeLedger`).
- **Cash/bank ledger** — `CurrentBalance = StartBalance + receipts + transfers − payments`,
  recalculated after every posting (`CashLedger`).
- **Invoice payments** — several receipts/payments per invoice, each posting to a bank account
  or a cash box and adjusting the balance immediately.
- **Codes** — Persian/Arabic digits normalised, duplicate codes rejected with the desktop's
  Persian messages (409), Iranian national ID and legal-ID checksums validated.
- **People/companies** — saving mirrors the row into `CustomersProviders` (so it can be the
  party of an invoice) and syncs the optional auto `DetailAccount`.
- **Roles** — `Admin` (everything), `Warehouse` (transfers register + accept), `Sales`
  (invoices), `User` (everything except the two above). Enforced server-side.
- **Audit + soft delete** — `Deleted`, `RecordUniqueId`, `CreateDateTime`, `CreateUserId`,
  `UpdateDateTime`, `UpdateUserId` are stamped automatically on every write.

### Regression test

`tools/smoke_test.py` drives the API exactly like a client and asserts the business rules:

```bash
cd SadrWeb
python3 tools/smoke_test.py                        # against http://127.0.0.1:5199
python3 tools/smoke_test.py http://host:5199 --password '…'
```

It covers authentication and token rotation, role permissions, CRUD + soft delete, image
upload/download, invoice totals and numbering, warehouse transfers and stock, the cheque
ledger (post and reverse), invoice payments and their reversal, and every report endpoint.
Current status: **45/45 checks pass**.

---

## 5. Connecting a desktop or mobile client

1. `GET /openapi/v1.json` → generate or hand-write the client.
2. `POST /api/auth/login` `{ "username", "password" }` → `{ accessToken, refreshToken, user }`.
3. Send `Authorization: Bearer <accessToken>` on every request. Access tokens live
   `Jwt:AccessTokenMinutes` (120 by default). On `401`, call `POST /api/auth/refresh` with
   `{ "refreshToken" }` once and retry; the refresh token is rotated on each use.
4. `GET /api/meta` returns every constant a client needs to render Persian labels
   (invoice types/statuses, cheque directions/statuses, task statuses, transaction types,
   account types, role permissions).
5. Uploads: `POST /api/products/{id}/images` as `multipart/form-data`, field `files`
   (repeatable). Downloads require the bearer token too: `GET /api/files/{uploadedFileId}`.

```bash
# login + list invoices + register a receipt
TOKEN=$(curl -s -X POST localhost:5199/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"…"}' | jq -r .accessToken)

curl -s localhost:5199/api/invoices?type=0 -H "Authorization: Bearer $TOKEN"
curl -s -X POST localhost:5199/api/invoices/1/money \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"type":1,"transActionSystem":2,"bankAccountId":1,"value":2000000}'
```

Because the API is stateless and token-based, the same endpoints serve the WPF app
(`HttpClient`), a .NET MAUI app, a Flutter app or a browser SPA without changes.

---

## 6. Project layout

```
SadrWeb/
├── api/                      ASP.NET Core 10 minimal API
│   ├── Data/                 entities + DbContext + ledgers (copied from SadrApp)
│   ├── Infrastructure/       auth, JWT, audit, generic CRUD, file store, seed
│   ├── Endpoints/            one file per functional area
│   └── wwwroot/              built SPA (generated by build.sh)
├── web/                      React 19 + TypeScript SPA
│   └── src/
│       ├── api.ts            fetch client with token refresh
│       ├── jalali.ts         Jalali ⇄ Gregorian dates
│       ├── auth.tsx          session context
│       ├── components/       layout, table, modal, metadata-driven CrudPage
│       └── pages/            dashboard, invoices, products, cheques, …
├── docs/API.md               endpoint reference
├── build.sh / build.ps1      build SPA + API
└── README.md
```

## 7. Deliberate gaps

- **No backups/jobs UI** — database backup and restore remain an operator task (copy the
  SQLite file, or use SQL Server tooling). The desktop's `BackupWindow` has no web equivalent yet.
- **Printing** — invoice print **templates** are stored (`/api/invoice-print-settings`) and the
  desktop keeps its WPF printer; the web app does not render the print layouts yet.
- **Users/passwords** — the web app writes the same `Users` table, but passwords hashed by the
  desktop (`PBKDF2$…`) and by the API use the identical format, so either can sign in.
- **Project budgets** — `FinalSpendBudget`/`CurrentSpendBudget` are stored but not recalculated
  from task reports automatically (same as the desktop, where they are edited by hand).
