#!/usr/bin/env python3
"""
End-to-end regression test for the SadrWeb API.

It exercises the real business rules through HTTP only — the same calls a desktop or mobile
client makes — and prints one PASS/FAIL line per check.

Usage:
    python3 tools/smoke_test.py                       # http://127.0.0.1:5199
    python3 tools/smoke_test.py http://host:5199 --password '…'

When --password is omitted it is read from the running instance's
bin/*/net10.0/App_Data/admin-password.txt (generated on first start).
"""

from __future__ import annotations

import glob
import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1].startswith("http") else "http://127.0.0.1:5199"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    results.append((name, ok, detail))
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f"   [{detail}]" if detail else ""))


def admin_password() -> str:
    if "--password" in sys.argv:
        return sys.argv[sys.argv.index("--password") + 1]
    candidates = glob.glob(os.path.join(ROOT, "api/bin/*/net10.0/App_Data/admin-password.txt"))
    candidates += glob.glob(os.path.join(ROOT, "api/App_Data/admin-password.txt"))
    if not candidates:
        sys.exit("admin password file not found — pass --password")
    newest = max(candidates, key=os.path.getmtime)
    for line in open(newest, encoding="utf-8"):
        if line.startswith("password:"):
            return line.split(":", 1)[1].strip()
    sys.exit(f"no password line in {newest}")


TOKEN = ""


def call(method: str, path: str, body=None, token: str | None = None, raw: bool = False,
         form: dict | None = None):
    """Returns (status, parsed json or bytes)."""
    url = BASE + path
    headers = {}
    data = None

    if form is not None:
        boundary = "----sadr" + uuid.uuid4().hex
        parts: list[bytes] = []
        for key, (filename, content) in form.items():
            parts.append(f"--{boundary}\r\n".encode())
            parts.append(
                f'Content-Disposition: form-data; name="{key}"; filename="{filename}"\r\n'.encode()
            )
            parts.append(b"Content-Type: application/octet-stream\r\n\r\n")
            parts.append(content)
            parts.append(b"\r\n")
        parts.append(f"--{boundary}--\r\n".encode())
        data = b"".join(parts)
        headers["Content-Type"] = f"multipart/form-data; boundary={boundary}"
    elif body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"

    active = TOKEN if token is None else token
    if active:
        headers["Authorization"] = f"Bearer {active}"

    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = response.read()
            status = response.status
            content_type = response.headers.get("content-type", "")
    except urllib.error.HTTPError as error:
        payload = error.read()
        status = error.code
        content_type = error.headers.get("content-type", "")

    if raw:
        return status, payload
    if "application/json" in content_type and payload:
        try:
            return status, json.loads(payload)
        except json.JSONDecodeError:
            return status, payload.decode(errors="replace")
    return status, payload.decode(errors="replace")


def items(path: str) -> list:
    status, data = call("GET", path)
    if isinstance(data, dict):
        return data.get("items", [])
    return data if isinstance(data, list) else []


def main() -> int:
    global TOKEN

    status, _ = call("GET", "/api/products")
    check("unauthenticated data request is rejected", status == 401, f"status={status}")

    status, _ = call("POST", "/api/auth/login", {"username": "admin", "password": "wrong-password"})
    check("wrong password is rejected", status == 401, f"status={status}")

    password = admin_password()
    status, login = call("POST", "/api/auth/login", {"username": "admin", "password": password})
    if status != 200:
        check("admin login", False, f"status={status} {login}")
        return 1
    check("admin login", True, login["user"]["role"])
    TOKEN = login["accessToken"]

    status, refreshed = call("POST", "/api/auth/refresh", {"refreshToken": login["refreshToken"]})
    rotated = status == 200 and refreshed.get("refreshToken") not in (None, "", login["refreshToken"])
    check("refresh token rotates", rotated, f"status={status}")
    TOKEN = refreshed["accessToken"] if status == 200 else TOKEN

    status, stale = call("POST", "/api/auth/refresh", {"refreshToken": login["refreshToken"]})
    check("used refresh token is no longer accepted", status == 401, f"status={status}")

    status, meta = call("GET", "/api/meta")
    check("meta catalogue is public", status == 200 and len(meta["chequeStatuses"]) == 6)

    # ---------------------------------------------------------------- fixtures
    stamp = str(int(time.time()))
    status, category = call("POST", "/api/product-categories", {"name": f"دسته {stamp}", "code": f"CAT-{stamp}"})
    check("create product category", status == 201, f"status={status}")
    category_id = category["id"]

    status, unit_list = call("GET", "/api/units")
    unit_id = unit_list["items"][0]["id"]
    status, currency_list = call("GET", "/api/currencies")
    currency_id = currency_list["items"][0]["id"]

    status, product = call("POST", "/api/products", {
        "name": f"کالای تست {stamp}", "code": f"P-{stamp}", "description": "",
        "mainCategoryId": category_id, "mainUnitId": unit_id, "initialQuantity": 10
    })
    check("create product (opening stock 10)", status == 201, f"status={status}")
    product_id = product["id"]

    status, stock = call("GET", f"/api/warehouse/stock/{product_id}")
    check("stock starts at the opening quantity", status == 200 and stock["stock"] == 10, f"stock={stock['stock']}")

    status, person = call("POST", "/api/people", {
        "firstName": "تست", "lastName": stamp, "code": f"00{stamp[-8:]}"
    })
    check("create person", status == 201, f"status={status}")

    status, _ = call("POST", "/api/people", {"firstName": "تست", "lastName": "تکراری", "code": f"00{stamp[-8:]}"})
    check("duplicate person code is rejected", status == 409, f"status={status}")

    status, _ = call("POST", "/api/companies", {"fullName": "شرکت تست", "code": f"C-{stamp}", "nationalId": "12345678901"})
    check("invalid شناسه ملی is rejected", status == 400, f"status={status}")

    mirror = [row for row in items("/api/customers-providers") if row.get("personId") == person["id"]]
    check("person is mirrored into customers-providers", len(mirror) == 1)

    # ---------------------------------------------------------------- images
    png = bytes.fromhex(
        "89504e470d0a1a0a0000000d4948445200000001000000010806000000"
        "1f15c4890000000d4944415478da63f8cfc0500f0004050080a98c2109"
        "0000000049454e44ae426082"
    )
    status, uploaded = call("POST", f"/api/products/{product_id}/images",
                            form={"files": ("pixel.png", png)})
    check("upload product image", status == 200 and len(uploaded) == 1, f"status={status}")
    file_id = uploaded[0]["uploadedFileId"] if status == 200 else 0

    status, raw = call("GET", f"/api/files/{file_id}", raw=True)
    check("download product image with token", status == 200 and len(raw) > 0, f"{len(raw)} bytes")

    status, _ = call("GET", f"/api/files/{file_id}", token="", raw=True)
    check("image download without token is rejected", status == 401, f"status={status}")

    status, _ = call("POST", f"/api/products/{product_id}/images",
                     form={"files": ("notes.txt", b"not an image")})
    check("non-image upload is rejected", status == 400, f"status={status}")

    # ---------------------------------------------------------------- invoice
    party_id = mirror[0]["id"] if mirror else 0
    status, invoice = call("POST", "/api/invoices", {
        "invoiceType": 0, "status": 1, "description": "فروش تست", "invoiceDateG": "2026-11-02",
        "partyId": party_id,
        "lines": [{"productId": product_id, "unitId": unit_id, "unitPrice": 2500000,
                   "amount": 3, "discountPercent": 5}]
    })
    expected_total = 7125000
    check("create sell invoice", status == 201, f"status={status}")
    invoice_id = invoice["id"]
    check("line math (3 x 2,500,000 - 5%)", invoice["totalPrice"] == expected_total,
          f"total={invoice['totalPrice']}")
    check("invoice number is generated", str(invoice["invoiceNumber"]).startswith("S-"), invoice["invoiceNumber"])

    status, _ = call("POST", "/api/invoices", {"invoiceType": 0, "status": 1, "lines": []})
    check("invoice without lines is rejected", status == 400, f"status={status}")

    transfers = [row for row in items("/api/warehouse/transfers?accepted=false") if row["invoiceId"] == invoice_id]
    check("invoice queues a pending warehouse transfer", len(transfers) == 1,
          f"{transfers[0]['quantity'] if transfers else '-'} out")

    status, accepted = call("POST", f"/api/warehouse/transfers/{transfers[0]['id']}/accept?accept=true")
    check("accept transfer", status == 200 and accepted["stock"] == 7, f"stock={accepted.get('stock')}")

    status, balance = call("GET", f"/api/invoices/{invoice_id}/balance")
    check("invoice balance starts unpaid",
          status == 200 and balance["paid"] == 0 and balance["remaining"] == expected_total)

    # ---------------------------------------------------------------- bank + cheque
    status, bank = call("POST", "/api/banks", {"name": f"بانک {stamp}", "code": f"B-{stamp}"})
    status, branch = call("POST", "/api/bank-branches",
                          {"name": "شعبه مرکزی", "code": f"BR-{stamp}", "bankId": bank["id"]})
    status, account = call("POST", "/api/bank-accounts", {
        "name": f"حساب {stamp}", "bankBranchId": branch["id"], "currencyId": currency_id,
        "accountNumber": stamp, "idNumber": "", "accountType": 0, "startBalance": 1000000
    })
    check("create bank account", status == 201, f"status={status}")
    account_id = account["id"]

    status, cheque = call("POST", "/api/cheques", {
        "bankAccountId": account_id, "dateG": "2026-12-01", "number": f"CH-{stamp}",
        "amount": 5000000, "direction": 1, "status": 1, "recieverFullName": "تست"
    })
    check("create received cheque", status == 201, f"status={status}")
    cheque_id = cheque["id"]

    status, cashed = call("POST", f"/api/cheques/{cheque_id}/status", {"status": 3})
    check("cashing a cheque deposits to the bank", cashed["balance"] == 6000000, f"balance={cashed['balance']}")

    status, bounced = call("POST", f"/api/cheques/{cheque_id}/status", {"status": 4})
    check("bouncing a cheque reverses the deposit", bounced["balance"] == 1000000, f"balance={bounced['balance']}")

    # ---------------------------------------------------------------- invoice payment
    status, payment = call("POST", f"/api/invoices/{invoice_id}/money", {
        "type": 1, "transActionSystem": 2, "bankAccountId": account_id,
        "value": 2000000, "dateG": "2026-11-03"
    })
    check("register a receipt against the invoice", status == 201, f"status={status}")

    status, after = call("GET", f"/api/invoices/{invoice_id}/balance")
    check("invoice balance reflects the payment",
          after["paid"] == 2000000 and after["remaining"] == expected_total - 2000000,
          f"remaining={after['remaining']}")

    status, account_now = call("GET", f"/api/bank-accounts/{account_id}")
    check("bank balance reflects the receipt", account_now["currentBalance"] == 3000000,
          f"balance={account_now['currentBalance']}")

    status, _ = call("DELETE", f"/api/account-transactions/{payment['id']}")
    status, account_after = call("GET", f"/api/bank-accounts/{account_id}")
    check("deleting the receipt reverses the balance", account_after["currentBalance"] == 1000000,
          f"balance={account_after['currentBalance']}")

    # ---------------------------------------------------------------- CRUD + soft delete
    status, brand = call("POST", "/api/brands", {"name": f"برند {stamp}", "code": f"BD-{stamp}"})
    check("create lookup row", status == 201, f"status={status}")
    call("PUT", f"/api/brands/{brand['id']}", {"name": f"برند ویرایش {stamp}", "code": f"BD-{stamp}"})
    status, updated = call("GET", f"/api/brands/{brand['id']}")
    check("update lookup row", updated["name"] == f"برند ویرایش {stamp}", updated["name"])

    call("DELETE", f"/api/brands/{brand['id']}")
    live = items(f"/api/brands?q=BD-{stamp}")
    status, all_rows = call("GET", f"/api/brands?includeDeleted=true&q=BD-{stamp}")
    check("delete is soft", len(live) == 0 and all_rows["total"] >= 1,
          f"live={len(live)} all={all_rows['total']}")

    # ---------------------------------------------------------------- roles
    call("POST", "/api/users", {"username": f"sales{stamp}", "password": "Sales@12345", "role": "Sales"})
    status, sales_login = call("POST", "/api/auth/login",
                               {"username": f"sales{stamp}", "password": "Sales@12345"})
    sales_token = sales_login.get("accessToken", "") if status == 200 else ""
    check("create sales user and sign in", bool(sales_token), f"status={status}")

    status, _ = call("POST", f"/api/warehouse/transfers/{transfers[0]['id']}/accept?accept=true",
                     token=sales_token)
    check("sales cannot accept warehouse transfers", status == 403, f"status={status}")

    status, _ = call("GET", "/api/users", token=sales_token)
    check("sales cannot list users", status == 403, f"status={status}")

    status, _ = call("GET", "/api/invoices", token=sales_token)
    check("sales can use invoices", status == 200, f"status={status}")

    # ---------------------------------------------------------------- reports
    status, dashboard = call("GET", "/api/reports/dashboard")
    check("dashboard aggregates", status == 200 and dashboard["products"] >= 1, f"status={status}")

    status, balances = call("GET", "/api/reports/balances")
    check("balances report", status == 200 and "total" in balances)

    status, stock_report = call("GET", "/api/warehouse/stock")
    check("stock report", status == 200 and stock_report["totals"]["products"] >= 1)

    status, due = call("GET", "/api/reports/cheques/due?days=60")
    check("cheque due report", status == 200 and "dueSoon" in due)

    status, tree = call("GET", "/api/accounting/tree")
    check("chart of accounts tree", status == 200 and "tree" in tree)

    status, _ = call("GET", "/openapi/v1.json", raw=True)
    check("OpenAPI document available", status == 200, f"status={status}")

    # ---------------------------------------------------------------- summary
    failed = [name for name, ok, _ in results if not ok]
    print()
    print(f"{len(results) - len(failed)}/{len(results)} checks passed")
    if failed:
        print("failed:")
        for name in failed:
            print(f"  - {name}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
