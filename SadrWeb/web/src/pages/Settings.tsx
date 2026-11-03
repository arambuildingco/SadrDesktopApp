import { useState } from 'react'
import { useAuth } from '../auth'
import { ApiError, api } from '../api'
import { FieldShell, useFlash } from '../components/ui'

/** Own-account settings: password change and a short client/API reference. */
export function Settings() {
  const { user } = useAuth()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const { flash, fail, node } = useFlash()

  const change = async () => {
    if (next !== repeat) {
      fail('تکرار رمز جدید مطابقت ندارد.')
      return
    }
    try {
      await api.post('/api/auth/change-password', { currentPassword: current, newPassword: next })
      setCurrent('')
      setNext('')
      setRepeat('')
      flash('رمز عبور تغییر کرد؛ دستگاه‌های دیگر باید دوباره وارد شوند.')
    } catch (err) {
      fail(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <>
      <div className="grid cols-2">
        <div className="card">
          <h2 className="section">حساب من</h2>
          <p className="muted">
            کاربر: <strong>{user?.username}</strong> · نقش: <strong>{user?.role}</strong> · ایمیل: {user?.email || '—'}
          </p>

          <hr className="sep" />
          <h2 className="section">تغییر رمز عبور</h2>
          <div className="grid cols-2">
            <FieldShell label="رمز فعلی" required>
              <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} />
            </FieldShell>
            <FieldShell label="رمز جدید" required hint="حداقل ۸ نویسه.">
              <input type="password" value={next} onChange={(e) => setNext(e.target.value)} />
            </FieldShell>
            <FieldShell label="تکرار رمز جدید" required>
              <input type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} />
            </FieldShell>
          </div>
          <button className="btn primary" style={{ marginTop: 12 }} onClick={change} disabled={!current || next.length < 8}>
            تغییر رمز
          </button>
        </div>

        <div className="card">
          <h2 className="section">اتصال برنامه‌های دیگر</h2>
          <p className="muted">
            این سامانه یک REST API کامل دارد؛ برنامه دسکتاپ، موبایل یا هر کلاینت دیگری می‌تواند با همان توکن به همین
            داده‌ها وصل شود.
          </p>
          <ul className="muted" style={{ fontSize: 12.5, lineHeight: 2 }}>
            <li>
              مستندات ماشین‌خوان: <code>/openapi/v1.json</code>
            </li>
            <li>
              سلامت سرویس: <code>/api/health</code>
            </li>
            <li>
              ورود: <code>POST /api/auth/login</code> → access + refresh token
            </li>
            <li>
              تمدید: <code>POST /api/auth/refresh</code>
            </li>
            <li>
              ثابت‌های برنامه (نقش‌ها، وضعیت‌ها): <code>GET /api/meta</code>
            </li>
          </ul>
          <p className="muted" style={{ fontSize: 12 }}>
            کلاینت‌ها باید هدر <code>Authorization: Bearer &lt;accessToken&gt;</code> را در همه درخواست‌ها بفرستند و در
            پاسخ ۴۰۱ یک‌بار با refresh token توکن را تمدید کنند.
          </p>
        </div>
      </div>
      {node}
    </>
  )
}
