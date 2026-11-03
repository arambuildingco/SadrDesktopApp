# Running SadrWeb — راهنمای اجرا

Everything below assumes the repository root is the folder that contains `SadrApp/` and
`SadrWeb/`. Replace paths as needed if you copied `SadrWeb/` somewhere else.

---

## 0. Quick start (one command, three steps)

If you only want to see the app running:

```bash
# 1. install the .NET 10 SDK (skip if `dotnet --version` already prints 10.x)
#    https://dotnet.microsoft.com/download/dotnet/10.0

# 2. build the SPA into the API and compile the API
cd SadrWeb
./build.sh                 # Windows: .\build.ps1

# 3. run it
cd api
dotnet run -c Release --urls http://0.0.0.0:5199
```

Open <http://localhost:5199> in a browser. The API and the web UI are served by the same
process on the same port, so there is nothing else to start and no CORS to configure.

**Signing in the first time:** a user `admin` is created on the first start. If you did not set
`Seed:AdminPassword`, a random password is generated and written to:

```
SadrWeb/api/bin/Release/net10.0/App_Data/admin-password.txt
```

Read that file, sign in, and change the password afterwards. To pick your own password instead,
delete the `App_Data` folder (or the whole `bin`/`obj` folders) and start with:

```bash
Seed__AdminPassword='YourPassword1!' dotnet run -c Release --urls http://0.0.0.0:5199
```

> The path above is the *build output* folder. `dotnet run` also copies the project's
> `App_Data` next to the compiled DLL, so the files you care about always live beside the
> executable, never in the source folder.

---

## 1. What you need

| Requirement | Version | Needed for |
|---|---|---|
| .NET SDK | 10.0 or newer | building and running the API |
| .NET ASP.NET Core runtime | 10.0 | running a published build without the SDK |
| Node.js + npm | 20 or newer (24+ recommended) | only for frontend work and `build.sh` |
| SQL Server | 2019+ / Express / LocalDB | optional — only if you want to share the desktop database |

`build.sh` runs `npm ci` automatically when `web/node_modules` is missing, so a fresh clone only
needs the .NET SDK **if** you are happy with the SPA that is already committed in `api/wwwroot`.

---

## 2. Two ways to run it

### A. Single process — how it ships

The built SPA lives in `api/wwwroot`, so the API serves both the REST endpoints (`/api/...`) and
the web UI (`/`, `/invoices`, …).

```bash
cd SadrWeb && ./build.sh
cd api && dotnet run -c Release --urls http://0.0.0.0:5199
```

A published, self-contained copy (no SDK or runtime needed on the target machine):

```bash
cd SadrWeb/api
dotnet publish -c Release -r win-x64  --self-contained -o ../../publish/win
dotnet publish -c Release -r linux-x64 --self-contained -o ../../publish/linux
# then:  ./publish/linux/SadrWeb.Api --urls http://0.0.0.0:5199
```

Copy the whole publish folder — `App_Data` is created inside it on first start, and that folder
holds the database, the uploaded files and the signing key.

### B. Two processes — for frontend development

Vite gives you instant hot reload and proxies `/api` to the API, so URLs stay identical to
production.

```bash
# terminal 1 — API on :5199
cd SadrWeb/api
dotnet run --urls http://localhost:5199

# terminal 2 — SPA on :5173 with hot reload
cd SadrWeb/web
npm install
npm run dev
```

Open <http://localhost:5173>. If you change the API port, update `server.proxy` in
`web/vite.config.ts` to match.

After changing frontend code for a real deployment, re-run `./build.sh` so the new bundle is
copied into `api/wwwroot`.

---

## 3. Configuration

Settings are read from `api/appsettings.json`, then overridden by environment variables (standard
.NET order). Every key can be set as an environment variable by replacing `:` with `__`:

| Setting | Default | Meaning |
|---|---|---|
| `Database:Provider` | `Sqlite` | `Sqlite` or `SqlServer` |
| `ConnectionStrings:Sqlite` | *(empty → `App_Data/sadr.db`)* | path to the SQLite file |
| `ConnectionStrings:SqlServer` | LocalDB `SadrDb` | used when the provider is `SqlServer` |
| `Jwt:Key` | *(empty → generated)* | HMAC signing key, at least 32 characters |
| `Jwt:Issuer` / `Jwt:Audience` | `SadrWeb` / `SadrClients` | token issuer/audience claims |
| `Jwt:AccessTokenMinutes` | `120` | access-token lifetime |
| `Jwt:RefreshTokenDays` | `14` | refresh-token lifetime |
| `Seed:AdminUsername` | `admin` | first administrator's user name |
| `Seed:AdminEmail` | `admin@sadr.local` | first administrator's e-mail |
| `Seed:AdminPassword` | *(empty → generated)* | password for the first administrator |
| `Files:Root` | *(empty → `App_Data/UploadedFiles`)* | where uploaded images are stored |
| `ASPNETCORE_URLS` | `http://localhost:5000` | listen addresses (`--urls` also works) |

Examples:

```bash
# different port, custom admin password, explicit signing key
ASPNETCORE_URLS=http://0.0.0.0:8080 \
Jwt__Key='a-very-long-random-string-used-to-sign-tokens' \
Seed__AdminPassword='ChangeMe1!' \
dotnet run -c Release
```

```powershell
# same thing in PowerShell
$env:ASPNETCORE_URLS = "http://0.0.0.0:8080"
$env:Jwt__Key = "a-very-long-random-string-used-to-sign-tokens"
dotnet run -c Release
```

> Keep `Jwt:Key` stable. If it changes, every issued token becomes invalid and all clients must
> sign in again. When it is empty, the app generates one and stores it in `App_Data/jwt.key`.
> In production set it explicitly so the key does not depend on a file that could be lost.

---

## 4. Using SQL Server (sharing data with the desktop app)

By default the API uses its own SQLite file. To point it at the desktop application's database so
**both apps work on the same records**:

1. Find the desktop app's connection string (it uses `SadrDb` on LocalDB, SQL Express or a full
   instance).
2. Start the API with the SQL Server provider:

```bash
Database__Provider=SqlServer \
ConnectionStrings__SqlServer='Server=localhost\SQLEXPRESS;Database=SadrDb;Trusted_Connection=True;TrustServerCertificate=True;' \
dotnet run -c Release --urls http://0.0.0.0:5199
```

On Linux/macOS give it SQL credentials instead of `Trusted_Connection`:

```
Server=192.168.1.10;Database=SadrDb;User Id=sa;Password=***;TrustServerCertificate=True;
```

Requirements and caveats:

* The schema must already exist. On SQLite the API calls `EnsureCreated()` and builds the schema
  itself; against an existing SQL Server database it reuses the tables as they are.
* Take a backup of the database before the first connection.
* Use one application at a time for heavy work. Both write the same rows, and neither caches data,
  but there is no cross-process locking beyond the database's own transactions.
* The desktop app stores passwords with the same PBKDF2 hasher, so **existing desktop users can
  sign in to the web app with the same credentials**.

To migrate existing data *into* the API's own SQLite database instead, export from the desktop app
or write the rows with any SQL Server → SQLite tool before the first start.

---

## 5. Exposing it on the network (LAN / server)

```bash
cd SadrWeb/api
ASPNETCORE_URLS=http://0.0.0.0:5199 dotnet run -c Release
```

Then browse to `http://<server-ip>:5199` from any machine in the office, and open the firewall
port (`sudo ufw allow 5199/tcp`, or a Windows Defender inbound rule).

For real use put HTTPS in front of it with IIS or nginx and let the proxy forward to
`http://127.0.0.1:5199`. The SPA and API share an origin, so no CORS configuration is required.

**Windows service** (survives reboots and logouts):

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o C:\SadrWeb
sc.exe create SadrWeb binPath= "C:\SadrWeb\SadrWeb.Api.exe --urls http://0.0.0.0:5199" start= auto
sc.exe start SadrWeb
```

**Linux systemd**:

```ini
# /etc/systemd/system/sadrweb.service
[Unit]
Description=SadrWeb API
After=network.target

[Service]
WorkingDirectory=/opt/sadrweb
ExecStart=/opt/sadrweb/SadrWeb.Api --urls http://0.0.0.0:5199
Restart=always
User=www-data

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now sadrweb
```

---

## 6. Where the data lives, and how to back it up

Everything the app owns sits in `App_Data`, next to the executable (`api/bin/<Config>/net10.0/App_Data`
under `dotnet run`, or beside the published exe):

| File / folder | Contents |
|---|---|
| `sadr.db` | the SQLite database (all records) |
| `UploadedFiles/` | images uploaded for products, people, … |
| `jwt.key` | generated token signing key, when `Jwt:Key` is not set |
| `admin-password.txt` | the generated first password (only when it was generated) |

**Backing up** means copying `App_Data` **while the app is stopped** (SQLite writes a journal file
alongside the database). With SQL Server, use a normal database backup and copy `UploadedFiles/`
separately.

> The `App_Data` folder of the build output is ignored by git and is *not* the one in the source
> tree — do not copy data into `SadrWeb/api/App_Data` expecting the running app to see it.

---

## 7. Checking that the install is healthy

```bash
curl http://localhost:5199/api/health          # {"status":"ok", ...}
curl http://localhost:5199/api/meta            # public catalogue of lookups/roles
curl http://localhost:5199/openapi/v1.json     # full API description

cd SadrWeb && python3 tools/smoke_test.py      # 45 end-to-end business checks
```

`tools/smoke_test.py` writes test records into the database it runs against, so point it at a
fresh instance, not at production data:

```bash
python3 tools/smoke_test.py http://localhost:5199 --password 'ChangeMe1!'
```

Sign in from the UI and open the dashboard — the header shows the signed-in user and the sidebar
switches between the modules your role allows (`Admin`, `Sales`, `Warehouse`, `User`).

---

## 8. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `Address already in use` | another process owns the port. `dotnet run --urls http://0.0.0.0:5199` with a free port, or stop the old process. |
| Browser shows the app, but every request returns **401** | you are not signed in, or the token expired — sign out and sign in again. If it persists, `Jwt:Key` changed between starts. |
| **404** on `/invoices` after a rebuild | `api/wwwroot` was emptied without rebuilding. Run `./build.sh` to copy `web/dist` back. |
| Blank page, only the HTML loads | the JS bundle is missing from `wwwroot` — same fix: `./build.sh`. Check the browser console for a 404 on `/assets/...`. |
| `SQLite Error 14: unable to open database file` | the `App_Data` folder could not be created. Give the process write permission to its own directory. |
| `Login failed` with the right password | you are starting a **different build** (Debug vs Release) and therefore a different `App_Data` and different admin user. Look for the password file in the folder you actually run. |
| Frontend dev server shows **Failed to fetch** | the API is not running on `127.0.0.1:5199`, or `server.proxy` in `vite.config.ts` points at the wrong port. |
| Prices/dates look wrong in a client | the API exchanges dates as ISO-8601 UTC; only the UI converts to Jalali. Send and store Gregorian, display Jalali. |
| `npm ci` fails | Node is older than the version in `package.json`/lockfile; install Node 20+ (24+ recommended). |

---

## 9. Resetting the demo data

Stop the app, delete `App_Data`, start again. The schema, the default currency, roles, units, the
default warehouse «انبار پیشفرش» and a fresh administrator are recreated on start-up. Uploaded
files go away with the folder, so remove product images from records first if you want to keep
them.

---

## 10. Where to read more

* [`../README.md`](../README.md) — stack rationale, desktop → web → endpoint feature map, business
  rules ported from the WPF app, deliberate gaps.
* [`API.md`](API.md) — every endpoint with request/response examples, for the mobile or desktop
  client.
* `/openapi/v1.json` on the running server — the machine-readable contract.
