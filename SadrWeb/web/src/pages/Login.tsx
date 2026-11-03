import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'
import { ApiError } from '../api'

export function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      await login(username.trim(), password)
      navigate('/')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'ورود ناموفق بود.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <form className="login-card" onSubmit={submit}>
        <h1>صدر</h1>
        <p className="sub">سامانه یکپارچه مدیریت فروش، خرید، انبار و مالی</p>
        <div className="fields">
          <label className="field">
            <span>نام کاربری</span>
            <input value={username} onChange={(event) => setUsername(event.target.value)} autoFocus />
          </label>
          <label className="field">
            <span>رمز عبور</span>
            <input
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          <div className="error">{error}</div>
          <button className="btn primary" type="submit" disabled={busy || !username || !password}>
            {busy ? 'در حال ورود…' : 'ورود'}
          </button>
        </div>
        <p className="muted" style={{ fontSize: 11.5, marginTop: 14 }}>
          همان حساب کاربری برنامه دسکتاپ؛ نقش‌ها: مدیر، انباردار، فروشنده.
        </p>
      </form>
    </div>
  )
}
