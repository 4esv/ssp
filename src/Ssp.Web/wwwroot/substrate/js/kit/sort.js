import { still, unit, TICK } from './controls.js'

const $ = (s, el) => el.querySelector(s)
const $$ = (s, el) => [...el.querySelectorAll(s)]
const asNumber = (s) =>
  parseFloat(
    s
      .replace(/\u2212/g, '-')
      .replace(/,/g, '')
      .replace(/^[^\d.+-]+/, '')
  )
const isNumber = (s) => /^[^\d]{0,2}[-+]?[\d,]*\.?\d+\s*[%a-zA-Z]{0,3}$/.test(s.trim())

export function sortable(tbl) {
  const rows = $$('tbody tr', tbl)
  if (rows.length < 2) return
  $$('thead th', tbl).forEach((th, i) => {
    if ($('button', th) || !th.textContent.trim()) return
    const cells = rows.map((r) => r.cells[i]?.textContent.trim() ?? '').filter(Boolean)
    const num = cells.length && cells.every(isNumber)
    const b = document.createElement('button')
    b.className = 'ctl btn'
    b.dataset.sort = i
    if (num) b.dataset.num = ''
    const lbl = document.createElement('span')
    lbl.className = 'lbl'
    lbl.append(...th.childNodes, ' ')
    const v = document.createElement('span')
    v.className = 'v'
    v.textContent = ' '
    lbl.append(v)
    b.append(lbl)
    th.append(b)
  })
}

export function sort(tbl, b) {
  const col = +b.dataset.sort
  const down = b.dataset.dir !== 'down'
  for (const o of $$('[data-sort]', tbl)) ((o.dataset.dir = ''), ($('.v', o).textContent = ' '))
  b.dataset.dir = down ? 'down' : 'up'
  $('.v', b).textContent = down ? 'v' : '^'
  const rows = $$('tbody tr', tbl)
  const before = new Map(rows.map((r) => [r, r.getBoundingClientRect().top]))
  const key = (r) => r.cells[col]?.textContent.trim() ?? ''
  const num = b.hasAttribute('data-num')
  const dir = down ? 1 : -1
  rows.sort((x, y) => {
    if (!num) return key(x).localeCompare(key(y)) * dir
    const [p, q] = [asNumber(key(x)), asNumber(key(y))]
    if (isNaN(p) || isNaN(q)) return isNaN(p) - isNaN(q)
    return (p - q) * dir
  })
  $('tbody', tbl).append(...rows)
  if (still) return
  const k = unit()
  const from = rows.map((r) => [r, Math.round((before.get(r) - r.getBoundingClientRect().top) / k)])
  tbl.classList.add('sorting')
  for (const [r, dy] of from) r.style.zIndex = Math.abs(dy)
  const n = 8
  let i = 0
  const step = () => {
    i++
    const t = 1 - (1 - i / n) ** 3
    for (const [r, dy] of from)
      r.style.transform = i >= n ? '' : `translateY(calc(${Math.round(dy * (1 - t))} * var(--u)))`
    if (i < n) setTimeout(step, TICK)
    else (tbl.classList.remove('sorting'), from.forEach(([r]) => (r.style.zIndex = '')))
  }
  step()
}
