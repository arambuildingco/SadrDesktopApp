import { useMemo, useState } from 'react'
import { ApiError, api, money, number, qs, type Json } from '../api'
import { Badge, DataTable, FieldShell, Modal, useAsync, useFlash, useOptions } from '../components/ui'
import { isoToJalali, jalaliToIso, todayJalali } from '../jalali'

const statusLabels: Record<number, string> = { 0: 'شروع نشده', 1: 'در حال اجرا', 2: 'متوقف', 3: 'انجام شده', 4: 'ناموفق' }

export function Projects() {
  const [term, setTerm] = useState('')
  const [editing, setEditing] = useState<Json | null>(null)
  const [openTasks, setOpenTasks] = useState<Json | null>(null)
  const { flash, fail, node } = useFlash()

  const query = useMemo(() => qs({ q: term }), [term])
  const { data, loading, error, reload } = useAsync(() => api.list<Json>(`/api/projects${query}`), [query])

  return (
    <>
      <div className="card">
        <div className="toolbar">
          <input placeholder="نام یا کد پروژه…" value={term} onChange={(e) => setTerm(e.target.value)} style={{ maxWidth: 240 }} />
          <span className="spacer" />
          <button className="btn" onClick={reload}>
            بازخوانی
          </button>
          <button className="btn primary" onClick={() => setEditing({ startDate: todayJalali(), endDate: todayJalali(), status: 1, projectType: 0, projectPrice: 0, baseBudget: 0, currentProgressPercentage: 0 })}>
            + پروژه جدید
          </button>
        </div>
        {error && <div className="card" style={{ color: 'var(--danger)' }}>{error}</div>}
        <DataTable
          loading={loading}
          rows={data ?? []}
          empty="پروژه‌ای ثبت نشده است."
          columns={[
            { key: 'name', label: 'پروژه' },
            { key: 'code', label: 'کد' },
            { key: 'managerName', label: 'مدیر پروژه' },
            { key: 'startDateG', label: 'شروع', render: (row: Json) => isoToJalali(row.startDateG) },
            { key: 'endDateG', label: 'پایان', render: (row: Json) => isoToJalali(row.endDateG) },
            { key: 'projectPrice', label: 'مبلغ قرارداد', numeric: true, render: (row: Json) => money(row.projectPrice) },
            {
              key: 'progress',
              label: 'پیشرفت',
              numeric: true,
              render: (row: Json) => `${number(row.currentProgressPercentage)}٪ (${number(row.doneTasks)}/${number(row.taskCount)})`
            },
            {
              key: 'status',
              label: 'وضعیت',
              render: (row: Json) => <Badge kind={row.status === 3 ? 'ok' : row.status === 4 ? 'bad' : 'info'}>{statusLabels[row.status] ?? '—'}</Badge>
            },
            {
              key: '__actions',
              label: '',
              render: (row: Json) => (
                <div className="row">
                  <button className="btn sm" onClick={() => setOpenTasks(row)}>
                    وظایف
                  </button>
                  <button
                    className="btn sm"
                    onClick={() =>
                      setEditing({ ...row, startDate: isoToJalali(row.startDateG), endDate: isoToJalali(row.endDateG) })
                    }
                  >
                    ویرایش
                  </button>
                </div>
              )
            }
          ]}
        />
      </div>

      {editing && (
        <ProjectForm
          draft={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
            flash('پروژه ذخیره شد.')
          }}
        />
      )}
      {openTasks && <TasksModal project={openTasks} onClose={() => setOpenTasks(null)} onError={fail} />}
      {node}
    </>
  )
}

function ProjectForm({ draft, onClose, onSaved }: { draft: Json; onClose: () => void; onSaved: () => void }) {
  const categories = useOptions('/api/project-categories')
  const currencies = useOptions('/api/currencies')
  const people = useOptions('/api/people')
  const [form, setForm] = useState<Json>({ ...draft })
  const [error, setError] = useState('')

  const save = async () => {
    try {
      const body = {
        name: form.name ?? '',
        description: form.description ?? '',
        code: form.code ?? '',
        projectType: Number(form.projectType ?? 0),
        startDateG: jalaliToIso(form.startDate),
        endDateG: jalaliToIso(form.endDate),
        projectPrice: Number(form.projectPrice ?? 0),
        baseBudget: Number(form.baseBudget ?? 0),
        currencyId: Number(form.currencyId),
        status: Number(form.status ?? 0),
        parentProjectId: form.parentProjectId ? Number(form.parentProjectId) : null,
        categoryId: Number(form.categoryId),
        currentProgressPercentage: Number(form.currentProgressPercentage ?? 0),
        creatorPersonId: Number(form.creatorPersonId ?? 0),
        projectManagerPersonId: Number(form.projectManagerPersonId ?? 0)
      }
      if (form.id) await api.put(`/api/projects/${form.id}`, body)
      else await api.post('/api/projects', body)
      onSaved()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err))
    }
  }

  return (
    <Modal
      title={form.id ? 'ویرایش پروژه' : 'پروژه جدید'}
      onClose={onClose}
      footer={
        <>
          <button className="btn primary" onClick={save} disabled={!form.name || !form.categoryId || !form.currencyId}>
            ذخیره
          </button>
          <button className="btn" onClick={onClose}>
            انصراف
          </button>
          {error && <span style={{ color: 'var(--danger)', fontSize: 12.5 }}>{error}</span>}
        </>
      }
    >
      <div className="grid cols-2">
        <FieldShell label="نام پروژه" required>
          <input value={String(form.name ?? '')} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </FieldShell>
        <FieldShell label="کد">
          <input value={String(form.code ?? '')} onChange={(e) => setForm({ ...form, code: e.target.value })} />
        </FieldShell>
        <FieldShell label="دسته‌بندی" required>
          <select value={String(form.categoryId ?? '')} onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
            <option value="">—</option>
            {categories.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="واحد پول" required>
          <select value={String(form.currencyId ?? '')} onChange={(e) => setForm({ ...form, currencyId: e.target.value })}>
            <option value="">—</option>
            {currencies.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="تاریخ شروع">
          <input value={String(form.startDate ?? '')} onChange={(e) => setForm({ ...form, startDate: e.target.value })} />
        </FieldShell>
        <FieldShell label="تاریخ پایان">
          <input value={String(form.endDate ?? '')} onChange={(e) => setForm({ ...form, endDate: e.target.value })} />
        </FieldShell>
        <FieldShell label="مبلغ قرارداد">
          <input type="number" value={String(form.projectPrice ?? 0)} onChange={(e) => setForm({ ...form, projectPrice: e.target.value })} />
        </FieldShell>
        <FieldShell label="بودجه پایه">
          <input type="number" value={String(form.baseBudget ?? 0)} onChange={(e) => setForm({ ...form, baseBudget: e.target.value })} />
        </FieldShell>
        <FieldShell label="مدیر پروژه">
          <select value={String(form.projectManagerPersonId ?? '')} onChange={(e) => setForm({ ...form, projectManagerPersonId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="وضعیت">
          <select value={String(form.status ?? 0)} onChange={(e) => setForm({ ...form, status: Number(e.target.value) })}>
            {Object.entries(statusLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="توضیحات">
          <textarea value={String(form.description ?? '')} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </FieldShell>
      </div>
    </Modal>
  )
}

function TasksModal({ project, onClose, onError }: { project: Json; onClose: () => void; onError: (m: string) => void }) {
  const detail = useAsync(() => api.get<Json>(`/api/projects/${project.id}`), [project.id])
  const people = useOptions('/api/people')
  const [draft, setDraft] = useState<Json>({ name: '', startDate: todayJalali(), dueDate: todayJalali(), workingDays: 1, progressPersentage: 0, startBudget: 0, status: 0, closed: false })

  const addTask = async () => {
    try {
      await api.post('/api/tasks', {
        projectId: project.id,
        name: draft.name,
        startDateG: jalaliToIso(draft.startDate),
        dueDateG: jalaliToIso(draft.dueDate),
        workingDays: Number(draft.workingDays ?? 0),
        responcePersonId: Number(draft.responcePersonId ?? 0),
        description: draft.description ?? '',
        progressPersentage: Number(draft.progressPersentage ?? 0),
        previousTasks: draft.previousTasks ?? '',
        startBudget: Number(draft.startBudget ?? 0),
        status: Number(draft.status ?? 0),
        closed: false
      })
      setDraft({ ...draft, name: '', description: '' })
      detail.reload()
    } catch (err) {
      onError(err instanceof ApiError ? err.message : String(err))
    }
  }

  const setProgress = async (task: Json, progress: number) => {
    await api.post(`/api/tasks/${task.id}/progress?progress=${progress}`)
    detail.reload()
  }

  return (
    <Modal title={`وظایف پروژه — ${project.name}`} onClose={onClose} wide>
      <DataTable
        loading={detail.loading}
        rows={detail.data?.tasks ?? []}
        empty="وظیفه‌ای ثبت نشده است."
        columns={[
          { key: 'name', label: 'وظیفه' },
          { key: 'personName', label: 'مسئول' },
          { key: 'dueDateG', label: 'مهلت', render: (row: Json) => isoToJalali(row.dueDateG) },
          { key: 'progressPersentage', label: 'پیشرفت', numeric: true, render: (row: Json) => `${number(row.progressPersentage)}٪` },
          { key: 'spentCost', label: 'هزینه', numeric: true, render: (row: Json) => money(row.spentCost) },
          { key: 'status', label: 'وضعیت', render: (row: Json) => statusLabels[row.status] ?? '—' },
          {
            key: '__actions',
            label: '',
            render: (row: Json) => (
              <div className="row">
                <button className="btn sm" onClick={() => setProgress(row, 100)}>
                  اتمام
                </button>
                <button className="btn sm" onClick={() => setProgress(row, Math.min(100, (row.progressPersentage ?? 0) + 25))}>
                  +۲۵٪
                </button>
              </div>
            )
          }
        ]}
      />

      <hr className="sep" />
      <h2 className="section">وظیفه جدید</h2>
      <div className="grid cols-3">
        <FieldShell label="نام وظیفه" required>
          <input value={String(draft.name)} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
        </FieldShell>
        <FieldShell label="مسئول">
          <select value={String(draft.responcePersonId ?? '')} onChange={(e) => setDraft({ ...draft, responcePersonId: e.target.value })}>
            <option value="">—</option>
            {people.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FieldShell>
        <FieldShell label="تاریخ شروع">
          <input value={String(draft.startDate)} onChange={(e) => setDraft({ ...draft, startDate: e.target.value })} />
        </FieldShell>
        <FieldShell label="مهلت">
          <input value={String(draft.dueDate)} onChange={(e) => setDraft({ ...draft, dueDate: e.target.value })} />
        </FieldShell>
        <FieldShell label="روزهای کاری">
          <input type="number" value={String(draft.workingDays)} onChange={(e) => setDraft({ ...draft, workingDays: e.target.value })} />
        </FieldShell>
        <FieldShell label="بودجه">
          <input type="number" value={String(draft.startBudget)} onChange={(e) => setDraft({ ...draft, startBudget: e.target.value })} />
        </FieldShell>
      </div>
      <button className="btn primary" style={{ marginTop: 12 }} onClick={addTask} disabled={!draft.name}>
        افزودن وظیفه
      </button>
    </Modal>
  )
}
