/**
 * Jalali (Persian) calendar helpers, so the web UI shows the same dates the desktop app
 * does while the API keeps storing Gregorian DateTime values.
 */

const breaks = [
  -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262,
  2324, 2394, 2456, 3178
]

function div(a: number, b: number) {
  return ~~(a / b)
}

function mod(a: number, b: number) {
  return a - ~~(a / b) * b
}

function jalCal(jy: number) {
  const bl = breaks.length
  const gy = jy + 621
  let leapJ = -14
  let jp = breaks[0]
  if (jy < jp || jy >= breaks[bl - 1]) throw new Error('invalid Jalali year')

  let jump = 0
  for (let i = 1; i < bl; i += 1) {
    const jm = breaks[i]
    jump = jm - jp
    if (jy < jm) break
    leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4)
    jp = jm
  }
  let n = jy - jp
  leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4)
  if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1

  const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150
  const march = 20 + leapJ - leapG
  if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33
  let leap = mod(mod(n + 1, 33) - 1, 4)
  if (leap === -1) leap = 4
  return { leap, gy, march }
}

export function toJalali(date: Date): { jy: number; jm: number; jd: number } {
  const gy = date.getFullYear()
  const gm = date.getMonth() + 1
  const gd = date.getDate()
  let jy = gy - 621
  const r = jalCal(jy)
  const gdn = g2d(gy, gm, gd)
  const jdn = gdn - d2g(jy, 1, 1, r.march)
  let jm: number
  let jd: number
  if (jdn <= 185) {
    jm = 1 + div(jdn, 31)
    jd = mod(jdn, 31) + 1
  } else {
    jm = 7 + div(jdn - 186, 30)
    jd = mod(jdn - 186, 30) + 1
  }
  return { jy, jm, jd }
}

function g2d(gy: number, gm: number, gd: number) {
  let d =
    div((gy + div(gm - 8, 6) + 100100) * 1461, 4) +
    div(153 * mod(gm + 9, 12) + 2, 5) +
    gd -
    34840408
  d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752
  return d
}

function d2g(jy: number, jm: number, jd: number, march: number) {
  const j =
    365 * (jy - 1) +
    div(jy - 1, 4) * 1 +
    div(jy - 1, 33) * 8 +
    div(mod(jy - 1, 33) + 3, 4) +
    (jm - 1) * 30 +
    jd - 1
  return j + march + 78 - 1
}

export function jalaliToDate(jy: number, jm: number, jd: number): Date {
  const r = jalCal(jy)
  const jdn = d2g(jy, jm, jd, r.march)
  return new Date(d2gregorian(jdn))
}

/** Converts the Jalali parts into a JS Date (UTC-normalised to local midnight). */
function d2gregorian(jdn: number): string {
  let j = 4 * jdn + 139361631
  j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908
  const i = div(mod(j, 1461), 4) * 5 + 308
  const gd = div(mod(i, 153), 5) + 1
  const gm = mod(div(i, 153), 12) + 1
  const gy = div(j, 1461) - 100100 + div(8 - gm, 6)
  return `${gy.toString().padStart(4, '0')}-${gm.toString().padStart(2, '0')}-${gd
    .toString()
    .padStart(2, '0')}T00:00:00`
}

/** Gregorian ISO string -> "1405/08/12" */
export function isoToJalali(iso: string | null | undefined): string {
  if (!iso || iso.startsWith('0001') || iso.startsWith('1900')) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const { jy, jm, jd } = toJalali(date)
  return `${jy}/${String(jm).padStart(2, '0')}/${String(jd).padStart(2, '0')}`
}

/** "1405/08/12" -> ISO date string for the API, or "" when invalid. */
export function jalaliToIso(text: string | null | undefined): string {
  if (!text) return ''
  const parts = text.replace(/-/g, '/').trim().split('/').filter(Boolean)
  if (parts.length < 3) return ''
  const jy = Number(parts[0])
  const jm = Number(parts[1])
  const jd = Number(parts[2])
  if (!jy || !jm || !jd) return ''
  try {
    return jalaliToDate(jy, jm, jd).toISOString()
  } catch {
    return ''
  }
}

export function todayJalali(): string {
  return isoToJalali(new Date().toISOString())
}
