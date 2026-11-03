# SadrWeb REST API

Base URL: `http://<host>:5199/api` · Machine-readable spec: `GET /openapi/v1.json`

All endpoints require `Authorization: Bearer <accessToken>` except the ones marked **public**.
Business data is never anonymous: the API's fallback policy requires authentication.

Conventions:

- Collections answer `{ "items": [...], "total": n, "page": 1, "pageSize": 50 }`.
- Every entity has `deleted`, `recordUniqueId`, `createUserId`, `createDateTime`, `updateUserId`,
  `updateDateTime`. Deletes are **soft** (`deleted = true`); `POST {collection}/{id}/restore`
  (admin) revives a row and `?includeDeleted=true` lists deleted rows where supported.
- Errors are `{ "message": "…" }` with `400` (validation), `401` (no/expired token),
  `403` (role), `404`, or `409` (duplicate code).
- Dates: write `…DateG` as ISO (`2026-11-03T00:00:00`); the matching `…Date` string is filled
  by the API with the Jalali text (`1405/08/12`).

## Authentication

| Method | Path | Body / notes |
| --- | --- | --- |
| POST | `/auth/login` **public** | `{username, password}` → `{accessToken, expiresAt, refreshToken, user}` |
| POST | `/auth/refresh` **public** | `{refreshToken}` → new pair (the old refresh token is rotated) |
| POST | `/auth/logout` | clears the stored refresh token |
| GET | `/auth/me` | current user |
| POST | `/auth/change-password` | `{currentPassword, newPassword}` (min 8 chars) |

## Meta

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/meta` **public** | roles, invoice types/statuses/layouts, cheque directions/statuses, task statuses, account types, transaction types, permissions, default warehouse name |
| GET | `/health` **public** | `{status:"ok"}` |

## Generic CRUD collections

Each of these supports `GET /` (`?q=&page=&pageSize=&includeDeleted=`), `GET /{id}`,
`POST /`, `PUT /{id}`, `DELETE /{id}` (soft), `POST /{id}/restore` (admin):

`brands`, `units`, `currencies`, `roles`, `warehouses`, `banks`, `bank-branches`,
`attributes`, `product-categories`, `project-categories`, `customers-providers`,
`account-groups`, `general-accounts`, `subsidiary-accounts`, `detail-accounts`,
`invoice-print-settings`, `tasks`, `task-reports`.

Extra collections:

| Path | Notes |
| --- | --- |
| `/category-attributes` | `?categoryId=`, POST, DELETE — attribute ↔ category links (`isForced`) |
| `/prices` | `?productId=&activeOnly=`, POST, PUT, DELETE |

## Products

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/products` | `?q=&categoryId=&brandId=&page=&pageSize=` → stock + last price per product |
| GET | `/products/{id}` | product + `stock` + attributes + extra categories + prices + images |
| POST | `/products` | creates the default warehouse when none exists |
| PUT / DELETE | `/products/{id}` | soft delete |
| GET | `/products/{id}/images` | `[{productFileId, uploadedFileId, originalName, url, fileSize}]` |
| POST | `/products/{id}/images` | `multipart/form-data`, field `files` (repeatable, ≤8 MB, images only) |
| DELETE | `/products/{id}/images/{productFileId}` | removes the link, keeps the stored file |
| PUT | `/products/{id}/attributes` | `[{categoryAttributeId, attributeId, value}]` (replaces the set) |
| PUT | `/products/{id}/extra-categories` | `[categoryId, …]` |
| GET | `/files/{uploadedFileId}` | the image bytes (bearer token required) |

## Invoices

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/invoices` | `?type=&status=&partyId=&from=&to=&q=&page=&pageSize=` + `paidAmount` |
| GET | `/invoices/{id}` | `{invoice, lines, money, transfers}` |
| POST | `/invoices` | `{invoiceType, status, invoiceNumber?, code?, description, invoiceDateG, partyId, lines:[…]}`; returns `warnings[]` for a projected stock shortfall |
| PUT | `/invoices/{id}` | replaces the line set by line identity (a product may repeat) |
| DELETE | `/invoices/{id}` | soft delete; pending warehouse transfers go with it, accepted ones stay |
| POST | `/invoices/{id}/status?status=` | 0 draft · 1 issued · 2 confirmed · 3 cancelled |
| POST | `/invoices/{id}/money` | `{type, transActionSystem, bankAccountId?, cashId?, value, dateG?, description?}` |
| GET | `/invoices/{id}/balance` | `{total, paid, remaining}` |

Line object: `{id?, productId, unitId, priceId?, unitPrice, currencyId?, amount, extraPercent?,
discountPercent?, discountAmount?}`.

## Warehouse

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/warehouse/transfers` | `?direction=1\|2&accepted=true\|false&productId=&warehouseId=` |
| POST | `/warehouse/transfers` | `{productId, direction, quantity, transferDateG, wareHouseId?, personId?, description?}` |
| PUT | `/warehouse/transfers/{id}` | editing returns the row to the pending queue |
| POST | `/warehouse/transfers/{id}/accept?accept=true\|false` | **Admin/Warehouse only**, else 403 |
| DELETE | `/warehouse/transfers/{id}` | accepted rows need Admin/Warehouse |
| GET | `/warehouse/stock` | `?warehouseId=&q=` → per product `opening, stock, pending, projected` + totals |
| GET | `/warehouse/stock/{productId}` | single product stock |

## Banking

| Method | Path | Notes |
| --- | --- | --- |
| GET/POST/PUT/DELETE | `/bank-accounts` | `currentBalance` is always recomputed from the ledger |
| GET | `/bank-transactions` | `?bankAccountId=&from=&to=` + `balance` |
| POST | `/bank-transactions` | `{bankAccountId, kind: 1 deposit | 2 withdrawal, amount, dateG, description}` |
| PUT/DELETE | `/bank-transactions/{id}` | manual rows only (cheque-generated rows are read-only here) |
| GET | `/cheques` | `?direction=&status=&bankAccountId=&from=&to=&q=` + in-hand totals |
| GET/POST/PUT/DELETE | `/cheques/{id}` | `{bankAccountId, dateG, number, serialNumber, recieverFullName, recieverCode, description, amount, invoiceId?, personId?, direction, status}` |
| POST | `/cheques/{id}/status` | `{status}` → 3 cashed (deposit/withdrawal), 4 bounced (reversal), 5 spent (withdrawal), others reverse any posting |

## Money

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/cashes` | `{items, totalBalance}` |
| GET/POST/PUT/DELETE | `/cashes/{id}` | `{name, code, startBalance, responcePersonId, ownerPersonId?, ownerCompanyId?, description}` |
| GET | `/account-transactions` | `?type=&system=&bankAccountId=&cashId=&from=&to=` + `received, paid, net` |
| POST | `/account-transactions` | `{type, transActionSystem, bankAccountId?, cashId?, value, dateG, description, invoiceId?, personId?}` |
| PUT/DELETE | `/account-transactions/{id}` | deleting reverses the balance effect |
| POST | `/account-transactions/transfer?bankAccountId=&cashId=&value=` | bank → cash transfer (two balanced rows) |

## People & companies

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/people` | `?q=` (name, code, phone) |
| GET | `/people/{id}` | `{person, subsidiaryAccountId}` |
| POST/PUT/DELETE | `/people` | `PUT /people/{id}?subsidiaryAccountId=` links the auto detail account |
| GET | `/companies` | `?q=` |
| GET | `/companies/{id}` | `{company, subsidiaryAccountId}` |
| POST/PUT/DELETE | `/companies` | validates the 11-digit شناسه ملی checksum |

## Projects & tasks

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/projects` | `?q=&status=&categoryId=` + task counts and progress |
| GET | `/projects/{id}` | `{project, tasks, spent}` |
| POST/PUT/DELETE | `/projects` | `{name, code, categoryId, currencyId, startDateG, endDateG, projectPrice, baseBudget, status, projectManagerPersonId, creatorPersonId, …}` |
| GET | `/projects/{id}/tasks` | tasks + `spentCost` |
| POST | `/tasks` | `{projectId, name, startDateG, dueDateG, workingDays, responcePersonId, startBudget, …}` |
| POST | `/tasks/{id}/progress?progress=` | 0–100; 100 marks Done and recalculates the project average |
| GET | `/tasks/{id}/reports` | task reports |
| POST | `/task-reports` | `{taskId, reportDateG, description, cost}` |

## Accounting

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/accounting/tree` | groups → general → subsidiary → detail, plus orphan counts |
| GET | `/accounting/summary` | account counts per group |
| POST | `/accounting/general-accounts` | requires a live account group |
| POST | `/accounting/subsidiary-accounts` | requires a live general account |
| POST | `/accounting/detail-accounts` | requires a live subsidiary account |

## Users (Admin only)

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/users` | `?q=` (never returns password hashes) |
| POST | `/users` | `{username, password (≥8), email, role}` |
| PUT | `/users/{id}` | `{username, email, role, emailConfirmed}` |
| POST | `/users/{id}/password` | `{newPassword}` — also revokes refresh tokens |
| DELETE | `/users/{id}` | cannot delete yourself or the last admin |

## Reports

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/reports/dashboard` | money totals, this month's sales/purchases, pending transfers, counts |
| GET | `/reports/balances` | cash boxes + bank accounts with balances and grand total |
| GET | `/reports/cheques/due?days=30` | overdue, due soon, bounced + totals |
| GET | `/reports/invoices/summary?from=&to=` | by type and by status, totals, paid |

## Status codes used by clients

| Code | Meaning | What the client should do |
| --- | --- | --- |
| 401 | missing/expired access token | refresh once, retry, otherwise sign in |
| 403 | role not allowed (e.g. Sales accepting a transfer) | show the message, do not retry |
| 409 | duplicate code | show `message`, keep the form open |
| 400 | validation (`message`) | show `message` next to the form |
