import { api, type Json } from '../api'
import { useAsync } from '../components/ui'

/** The chart of accounts as a tree: گروه → کل → معین → تفصیلی. */
export function Accounting() {
  const { data, loading, error } = useAsync(() => api.get<Json>('/api/accounting/tree'), [])

  if (loading) return <div className="loading">در حال بارگذاری ساختار حساب‌ها…</div>
  if (error) return <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>

  return (
    <div className="card">
      <h2 className="section">ساختار حساب‌ها</h2>
      <p className="muted">
        هر حساب تفصیلی می‌تواند به یک شخص یا شرکت متصل باشد؛ اتصال از صفحه اشخاص/شرکت‌ها با انتخاب «حساب معین» انجام می‌شود.
      </p>

      {(data?.tree ?? []).map((group: Json) => (
        <div key={group.id} style={{ marginBottom: 18 }}>
          <div className="row" style={{ marginBottom: 6 }}>
            <strong>{group.name}</strong>
            <span className="muted">کد {group.code || '—'}</span>
            <span className="badge">{group.generalAccounts?.length ?? 0} حساب کل</span>
          </div>

          {(group.generalAccounts ?? []).map((general: Json) => (
            <div key={general.id} style={{ marginInlineStart: 18, marginBottom: 8 }}>
              <div className="row">
                <span>▪ {general.name}</span>
                <span className="muted">{general.code}</span>
                <span className="badge">{(general.subSidiaryAccounts ?? []).length} معین</span>
              </div>

              {(general.subSidiaryAccounts ?? []).map((sub: Json) => (
                <div key={sub.id} style={{ marginInlineStart: 18 }}>
                  <div className="row">
                    <span>• {sub.name}</span>
                    <span className="muted">{sub.code}</span>
                    <span className="badge">{(sub.detailAccounts ?? []).length} تفصیلی</span>
                  </div>
                  {(sub.detailAccounts ?? []).length > 0 && (
                    <div className="row" style={{ marginInlineStart: 18, flexWrap: 'wrap', gap: 6 }}>
                      {(sub.detailAccounts ?? []).map((detail: Json) => (
                        <span key={detail.id} className="badge info">
                          {detail.name} {detail.code ? `(${detail.code})` : ''}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          ))}
        </div>
      ))}

      {!data?.tree?.length && <div className="empty">هنوز گروه حسابی تعریف نشده است.</div>}

      <hr className="sep" />
      <p className="muted" style={{ fontSize: 12 }}>
        ردیف‌های بدون والد: حساب کل {data?.orphans?.generalAccounts ?? 0} · حساب معین{' '}
        {data?.orphans?.subsidiaryAccounts ?? 0} · حساب تفصیلی {data?.orphans?.detailAccounts ?? 0}
      </p>
    </div>
  )
}
