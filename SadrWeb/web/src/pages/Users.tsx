import { useState } from 'react'
import { ApiError, api, type Json } from '../api'
import { DataTable, FieldShell, Modal, useAsync, useFlash } from '../components/ui'

const roles = ['Admin', 'Warehouse', 'Sales', 'User']

/** Application users — same accounts the desktop app signs in with. */
export function Users() {
  const { data, loading, error, reload } = useAsync(() => api.list<Json>('/api/users'), [])
  const [creating, setCreating] = useState(false)
  const [resetting, setResetting] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const remove = async (row: Json) => {
    if (!confirm(`کاربر «${row.username}» حذف شود؟`)) return
    try {
      await api.del(`/api/users/${row.id}`)
      reload()
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <span className="muted">نقش‌ها: مدیر، انباردار، فروشنده، کاربر عادی.</span>
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setCreating(true)}>
            + کاربر جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data ?? []}
          empty="کاربری ثبت نشده است."
          columns={[
            { key: 'username', label: 'نام کاربری' },
            { key: 'email', label: 'ایمیل' },
            { key: 'role', label: 'نقش' },
            { key: 'emailConfirmed', label: 'تأیید شده', render: (row: Json) => (row.emailConfirmed ? 'بله' : 'خیر') },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <div className="row">
                  <button className="btn sm" onClick={() => setResetting(row)}>
                    تغییر رمز
                  </button>
                  <button className="btn sm danger" onClick={() => remove(row)}>
                    حذف
                  </button>
                </div>
              )
            }
          ]}
        />
      </div>

      {creating && (
        <UserForm
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            reload()
            flash('کاربر ساخته شد.')
          }}
        />
      )}

      {resetting && (
        <ResetPassword
          user={resetting}
          onClose={() => setResetting(null)}
          onSaved={() => {
            setResetting(null)
            flash('رمز عبور تغییر کرد.')
          }}
        />
      )}
      {node}
    </>
  )
}

function UserForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState<Json>({ role: 'User' })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      await api.post('/api/users', {
        username: form.username,
        password: form.password,
        email: form.email ?? '',
        role: form.role
      })
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title="کاربر جدید"
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.username || !form.password}>
            ساخت کاربر
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <div className="grid cols-2">
        <FieldShell label="نام کاربری" required>
          <input value={String(form.username ?? '')} onChange={(e) => setForm({ ...form, username: e.target.value })} />
        </FieldShell>
        <FieldShell label="رمز عبور" required hint="حداقل ۸ نویسه.">
          <input type="password" value={String(form.password ?? '')} onChange={(e) => setForm({ ...form, password: e.target.value })} />
        </FieldShell>
        <FieldShell label="ایمیل">
          <input value={String(form.email ?? '')} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </FieldShell>
        <FieldShell label="نقش" required>
          <select value={String(form.role)} onChange={(e) => setForm({ ...form, role: e.target.value })}>
            {roles.map((role) => (
              <option key={role} value={role}>
                {role}
              </option>
            ))}
          </select>
        </FieldShell>
      </div>
    </Modal>
  )
}

function ResetPassword({ user, onClose, onSaved }: { user: Json; onClose: () => void; onSaved: () => void }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')

  const save = async () => {
    try {
      await api.post(`/api/users/${user.id}/password`, { newPassword: password })
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={`تغییر رمز — ${user.username}`}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={password.length < 8}>
            ذخیره
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <FieldShell label="رمز جدید" required hint="کاربر در دستگاه‌های دیگر باید دوباره وارد شود.">
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
      </FieldShell>
    </Modal>
  )
}
