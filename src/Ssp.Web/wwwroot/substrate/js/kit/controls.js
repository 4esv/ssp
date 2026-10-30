import { fade, ditherFor } from './dither.js'
import { unit, width, mono } from './lattice.js'
import { glide } from './morph.js'
import { confirm, resolve } from './machines.js'

export const $ = (s, el = document) => el.querySelector(s)
export const $$ = (s, el = document) => [...el.querySelectorAll(s)]
export const still = matchMedia('(prefers-reduced-motion: reduce)').matches
export const TICK = 1000 / 24
export const wait = (ms) => new Promise((r) => setTimeout(r, ms))
export const esc = (s) =>
  String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
export { unit, fade }

export const btn = (t, cls = '', at = '') =>
  `<button class="ctl btn ${cls}" ${at}><span class="lbl">${t}</span></button>`

const formWidth = (f) => {
  const ns = [...f.childNodes]
  return ns.reduce((w, n, i) => {
    if (n.nodeType !== 3) return w + width(n.textContent, mono)
    let t = n.data
    if (i === 0) t = t.trimStart()
    if (i === ns.length - 1) t = t.trimEnd()
    return w + width(t)
  }, 0)
}
export function hold(scope = document) {
  for (const b of $$('[data-alts]', scope)) {
    if ($('.hold', b)) continue
    const first = $('.lbl', b).firstChild
    const h = Object.assign(document.createElement('span'), { className: 'hold' })
    h.innerHTML = [first.textContent, ...b.dataset.alts.split(';')]
      .map(
        (f, i) =>
          `<span${i ? '' : ' class="on"'}>${esc(f).replace(/ \|$/, ' <span class="spin">|</span>')}</span>`
      )
      .join('')
    first.replaceWith(h)
    const forms = [...h.children]
    const ws = forms.map(formWidth)
    const max = Math.max(...ws)
    forms.forEach((f, i) => f.style.setProperty('--mid', Math.floor((max - ws[i]) / 2)))
  }
}
export function say(b, i) {
  $$('.hold > *', b).forEach((s, j) => {
    const was = s.classList.contains('on')
    s.classList.remove('dither-swap-in', 'dither-swap-out')
    s.classList.toggle('on', j === i)
    if (still || was === (j === i)) return
    void s.offsetWidth
    s.classList.add(was ? 'dither-swap-out' : 'dither-swap-in')
    s.addEventListener(
      'animationend',
      () => s.classList.remove('dither-swap-in', 'dither-swap-out'),
      { once: true }
    )
  })
}
export const saying = (b) => $$('.hold > *', b).findIndex((s) => s.classList.contains('on'))

export function spin(b) {
  let i = 0
  const s = $('.spin', b)
  const t = setInterval(() => s && (s.textContent = '|/-\\'[++i % 4]), still ? 1e9 : 125)
  return () => clearInterval(t)
}

export const canCopy = () =>
  !!navigator.clipboard?.writeText || !!document.queryCommandSupported?.('copy')
function legacyCopy(text, near) {
  const was = document.activeElement
  const sel = window.getSelection()
  const kept = Array.from({ length: sel.rangeCount }, (_, i) => sel.getRangeAt(i))
  const ta = Object.assign(document.createElement('textarea'), { value: text, readOnly: true })
  ta.setAttribute('aria-hidden', 'true')
  ta.style.cssText = 'position:fixed;top:0;left:-9999px;opacity:0;font-size:16px'
  ;(near?.closest('dialog') || document.body).appendChild(ta)
  ta.focus({ preventScroll: true })
  ta.select()
  ta.setSelectionRange(0, text.length)
  let ok = false
  try {
    ok = document.execCommand('copy')
  } catch {
  }
  ta.remove()
  was?.focus?.({ preventScroll: true })
  sel.removeAllRanges()
  for (const r of kept) sel.addRange(r)
  return ok
}
export async function copyText(text, near) {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
    }
  }
  return legacyCopy(text, near)
}

export async function copied(b, text) {
  if (!(await copyText(text, b))) {
    const shown = b.closest('.cmd, .codeblock')?.querySelector('code, pre')
    if (shown) {
      const r = document.createRange()
      r.selectNodeContents(shown)
      window.getSelection().removeAllRanges()
      window.getSelection().addRange(r)
    } else if ($('.toast')) toast('could not copy, select it by hand')
    return false
  }
  b.dataset.copied = text
  say(b, 1)
  clearTimeout(b.t)
  b.t = setTimeout(() => say(b, 0), 1400)
  return true
}

const swatch = document.body.appendChild(
  Object.assign(document.createElement('i'), { hidden: true })
)
export function tone(v) {
  swatch.style.color = `var(${v})`
  const c = getComputedStyle(swatch).color
  const m = c.match(/^color\(srgb ([\d.]+) ([\d.]+) ([\d.]+)/)
  return m ? `rgb(${m.slice(1).map((x) => Math.round(x * 255))})` : c
}
export function pixels(cv, w, h = 20) {
  const d = Math.round(unit() * devicePixelRatio)
  cv.width = w * d
  cv.height = h * d
  cv.style.width = `calc(${w} * var(--u))`
  cv.style.height = `calc(${h} * var(--u))`
  const g = cv.getContext('2d')
  return (c, x, y, ww = 1, hh = 1) => ((g.fillStyle = c), g.fillRect(x * d, y * d, ww * d, hh * d))
}
export const draws = new Set()

export const METER = 70
export function meter(cv, frac, w = METER) {
  const dot = pixels(cv, w)
  const ink = tone('--ink')
  const done = Math.round(frac * w)
  for (let x = 0; x < w; x++)
    for (let y = 5; y <= 15; y++)
      if (x < done || (x < done + 2 ? (x + y) % 2 === 0 : x % 2 === 0 && y % 2 === 0))
        dot(ink, x, y)
}
export const pct = (frac) => `${String(Math.round(frac * 100)).padStart(3)}%`

export function toast(t) {
  const el = $('.toast')
  el.textContent = t
  el.classList.add('up')
  clearTimeout(toast.t)
  toast.t = setTimeout(
    () =>
      fade(el, 'out').then(() => {
        el.style.transition = 'none'
        el.classList.remove('up')
        void el.offsetWidth
        el.style.transition = ''
        el.textContent = ''
      }),
    1600
  )
}

export function astext(id, text) {
  const pre = document.getElementById(id)
  if (pre && pre.textContent !== text) pre.textContent = text
}

export function size(n) {
  const units = ['B', 'KB', 'MB', 'GB']
  let i = 0
  while (n >= 1000 && i < units.length - 1) ((n /= 1000), i++)
  return `${i && n < 10 ? n.toFixed(1) : Math.round(n)} ${units[i]}`
}

export const fmtWhen = (ms) =>
  ms == null ? 'never' : new Date(ms).toISOString().slice(0, 16).replace('T', ' ')
export function fmtRelative(ms, now = Date.now()) {
  if (ms == null) return 'never'
  const d = ms - now
  const a = Math.abs(d)
  if (a < 60e3) return d >= 0 ? 'in under a minute' : 'just now'
  const [u, s] = [
    ['d', 864e5],
    ['h', 36e5],
    ['m', 6e4],
  ].find(([, s]) => a >= s)
  const n = Math.round(a / s)
  return d >= 0 ? `in ${n}${u}` : `${n}${u} ago`
}
export const expiry = (ms) =>
  ms == null
    ? 'never expires'
    : ms > Date.now()
      ? `expires ${fmtRelative(ms)}`
      : `expired ${fmtRelative(ms)}`

export function swap(el, text) {
  const old = el.querySelector('.now')
  if (old && old.textContent === text) return
  const n = Object.assign(document.createElement('span'), { className: 'now', textContent: text })
  if (!old || still) return void el.replaceChildren(n)
  for (const o of el.querySelectorAll('.was')) o.remove()
  old.className = 'was dither-swap-out'
  n.classList.add('dither-swap-in')
  el.classList.add('swapping')
  el.append(n)
  old.addEventListener('animationend', () => (old.remove(), n.classList.remove('dither-swap-in')), {
    once: true,
  })
}

const armed = (b) => (b.getAttribute('aria-pressed') === 'true' ? 'armed' : 'idle')
export function arm(b, sure = 1) {
  clearTimeout(b.arm)
  if (confirm(armed(b), 'press') === 'fired') {
    b.setAttribute('aria-pressed', 'false')
    return true
  }
  say(b, sure)
  b.setAttribute('aria-pressed', 'true')
  b.arm = setTimeout(() => disarm(b), 4000)
  return false
}
export function disarm(b) {
  clearTimeout(b.arm)
  if (confirm(armed(b), 'quiet') === armed(b)) return
  b.setAttribute('aria-pressed', 'false')
  say(b, 0)
}

export function walk(e, items, vertical = true) {
  const keys = vertical ? ['ArrowUp', 'ArrowDown'] : ['ArrowLeft', 'ArrowRight']
  const i = items.indexOf(document.activeElement)
  let j = -1
  if (e.key === keys[1]) j = (i + 1) % items.length
  if (e.key === keys[0]) j = (i - 1 + items.length) % items.length
  if (e.key === 'Home') j = 0
  if (e.key === 'End') j = items.length - 1
  if (j < 0) return false
  e.preventDefault()
  items[j].focus()
  return items[j]
}

export function fader(f, onset = () => {}, wide = 70) {
  const across = () => (typeof wide === 'function' ? wide() : wide)
  const cv = f.querySelector('canvas') || f.appendChild(document.createElement('canvas'))
  const span = () => [+f.getAttribute('aria-valuemin'), +f.getAttribute('aria-valuemax')]
  let blink = true
  const draw = () => {
    const [lo, hi] = span()
    const W = across()
    const dot = pixels(cv, W)
    const x = Math.round(((f.getAttribute('aria-valuenow') - lo) / (hi - lo || 1)) * (W - 2))
    dot(tone('--dim'), 0, 11, W)
    dot(tone('--ink'), 0, 11, x)
    if (blink) dot(tone('--ink'), x, 4, 2, 15)
  }
  const set = (v, quiet) => {
    const [lo, hi] = span()
    f.setAttribute('aria-valuenow', Math.round(Math.max(lo, Math.min(hi, v))))
    draw()
    if (!quiet) onset(+f.getAttribute('aria-valuenow'))
  }
  const at = (e) => {
    const [lo, hi] = span()
    const r = cv.getBoundingClientRect()
    const W = across()
    set(lo + ((((e.clientX - r.left) / r.width) * W - 1) / (W - 2)) * (hi - lo))
  }
  f.addEventListener('pointerdown', (e) => (f.setPointerCapture(e.pointerId), at(e)))
  f.addEventListener('pointermove', (e) => f.hasPointerCapture(e.pointerId) && at(e))
  f.addEventListener('keydown', (e) => {
    const [lo, hi] = span()
    const px = (hi - lo) / (across() - 2)
    const d = { ArrowRight: 1, ArrowUp: 1, ArrowLeft: -1, ArrowDown: -1 }[e.key]
    if (d)
      (e.preventDefault(),
        set(+f.getAttribute('aria-valuenow') + d * Math.max(1, px) * (e.shiftKey ? 10 : 1)))
    if (e.key === 'Home' || e.key === 'End') (e.preventDefault(), set(e.key === 'Home' ? lo : hi))
  })
  let t = 0
  f.addEventListener(
    'focus',
    () => !still && (t = setInterval(() => ((blink = !blink), draw()), 500))
  )
  f.addEventListener('blur', () => (clearInterval(t), (blink = true), draw()))
  draws.add(draw)
  draw()
  return set
}

const REACH = 64
export function targets(scope = document, at) {
  const live = (c) => !c.disabled && !c.closest('[inert], .ghost') && c.getClientRects().length
  return $$('.ctl', scope)
    .filter(live)
    .filter((c) => {
      if (!at) return true
      const r = c.getBoundingClientRect()
      return (
        Math.hypot(
          Math.max(r.left - at[0], 0, at[0] - r.right),
          Math.max(r.top - at[1], 0, at[1] - r.bottom)
        ) < REACH
      )
    })
    .map((c) => {
      const r = c.getBoundingClientRect()
      const s = getComputedStyle(c, '::before')
      const v = (x) => (Number.isFinite(parseFloat(x)) ? parseFloat(x) : 0)
      return {
        id: c.dataset.id ?? c,
        c,
        r,
        pad: {
          left: r.left + v(s.left),
          top: r.top + v(s.top),
          right: r.right - v(s.right),
          bottom: r.bottom - v(s.bottom),
        },
      }
    })
}
export const probe = (scope, x, y, near = true, ts = targets(scope)) => resolve(ts, x, y, near)
export function nearest(scope, x, y) {
  const ts = targets(scope, [x, y])
  const hit = resolve(ts, x, y)
  return hit && ts.find((t) => t.id === hit.got).c
}

const groupOf = (c) => c.closest('[role=radiogroup], [role=tablist]') ?? c.parentElement
const members = (g) => $$('[role=radio], [role=tab]', g).filter((r) => !r.closest('.ghost'))
export function choose(c) {
  const role = c.getAttribute('role')
  if (c.disabled || c.closest('.ghost')) return
  if (role === 'checkbox' || role === 'switch')
    c.setAttribute('aria-checked', c.getAttribute('aria-checked') !== 'true')
  else if (role === 'radio' || role === 'tab') {
    const group = groupOf(c)
    const was = members(group).find((r) => r.getAttribute('aria-checked') === 'true')
    if (was === c) return
    if (was && !still && group.matches('.grp') && group.getClientRects().length === 1)
      glide(group, was, c)
    for (const r of members(group)) {
      r.setAttribute('aria-checked', r === c)
      if (role === 'tab') r.setAttribute('aria-selected', r === c)
      r.tabIndex = r === c ? 0 : -1
    }
  } else return
  c.dispatchEvent(new Event('change', { bubbles: true }))
}
export function stops(scope = document) {
  for (const g of $$('[role=radiogroup]:not(.ghost), [role=tablist]:not(.ghost)', scope)) {
    const rs = members(g)
    const on = rs.find((r) => r.getAttribute('aria-checked') === 'true')
    rs.forEach((r, i) => (r.tabIndex = (on ? r === on : i === 0) ? 0 : -1))
  }
}

let bound = false
export function bind() {
  for (const pre of $$('.astext pre')) {
    pre.tabIndex = 0
    if (!pre.hasAttribute('aria-label'))
      pre.setAttribute('aria-label', 'the text form, scrolls sideways')
  }
  if (bound) return stops()
  bound = true
  document.addEventListener('click', (e) => {
    const c = e.target.closest('[role=checkbox], [role=switch], [role=radio], [role=tab]')
    if (c && !c.closest('[inert], .bar')) choose(c)
    if (e.target.closest('a[href="#"]')) e.preventDefault()
  })
  document.addEventListener('click', (e) => {
    if (!e.isTrusted) return
    if (
      e.target.closest(
        '.ctl, a, button, input, textarea, select, dialog, [inert], .bar, [data-native]'
      )
    )
      return
    nearest(document, e.clientX, e.clientY)?.click()
  })
  document.addEventListener('keydown', (e) => {
    const g = e.target.closest?.('[role=radiogroup], [role=tablist]')
    if (!g || !e.target.matches('[role=radio], [role=tab]')) return
    const rs = members(g)
    const next = walk(e, rs, false) || walk(e, rs, true)
    if (next) choose(next)
  })
  stops()
}

export function chrome(cells, { relayout = () => {}, redraw = () => {} } = {}) {
  const root = document.documentElement
  const paint = () => (draws.forEach((d) => d()), redraw(), cells.draw())
  const pixel = () =>
    cells.layout().then(() => {
      $('#read').textContent = ` pixel ${cells.pixel} device px `
      ditherFor(cells.pixel)
      relayout()
      paint()
    })
  $('#less').onclick = () => ((cells.pixel -= 1), pixel())
  $('#more').onclick = () => ((cells.pixel += 1), pixel())
  $('#theme').onclick = (e) => {
    const dark = root.dataset.theme !== 'dark'
    const change = () => {
      root.dataset.theme = dark ? 'dark' : 'light'
      e.target.textContent = dark ? 'Light' : 'Dark'
    }
    if (still || !document.startViewTransition) change()
    else document.startViewTransition(change)
  }
  new MutationObserver(paint).observe(root, { attributeFilter: ['data-theme'] })
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', paint)
  return pixel
}
