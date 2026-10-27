import {
  NONE,
  DIM,
  INK,
  LINE,
  BAYER,
  lit,
  width,
  prose,
  mono,
  whole,
  left,
  line,
  mix,
} from '../shared/lattice.js'

export { NONE, DIM, INK, LINE, BAYER, lit, width, prose, mono }
const TPS = 24
const still = matchMedia('(prefers-reduced-motion: reduce)').matches

export const unit = () => +getComputedStyle(document.documentElement).getPropertyValue('--px')
const tone = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim()

export function figure(host, cols, rows) {
  rows = whole(rows)
  host.classList.add('fig')
  const cv = host.querySelector('canvas') || host.appendChild(document.createElement('canvas'))
  cv.setAttribute('aria-hidden', 'true')
  const words =
    host.querySelector('.words') ||
    host.appendChild(Object.assign(document.createElement('div'), { className: 'words' }))
  let shown = new Uint8Array(cols * rows)
  let next = new Uint8Array(cols * rows)
  let busy = 0
  const paint = (buf) => {
    const d = Math.round(unit() * devicePixelRatio)
    if (cv.width !== cols * d) ((cv.width = cols * d), (cv.height = rows * d))
    cv.style.width = `calc(${cols} * var(--u))`
    cv.style.height = `calc(${rows} * var(--u))`
    const g = cv.getContext('2d')
    g.clearRect(0, 0, cv.width, cv.height)
    const inks = [null, tone('--dim'), tone('--ink')]
    for (const t of [DIM, INK]) {
      g.fillStyle = inks[t]
      for (let i = 0; i < buf.length; i++)
        if (buf[i] === t) g.fillRect((i % cols) * d, Math.floor(i / cols) * d, d, d)
    }
  }
  const f = {
    cols,
    rows,
    host,
    put(x, y, t = INK) {
      x = Math.round(x)
      y = Math.round(y)
      if (x >= 0 && y >= 0 && x < cols && y < rows) next[y * cols + x] = t
    },
    rect(x, y, w, h, t = INK, level = 16) {
      for (let j = y; j < y + h; j++)
        for (let i = x; i < x + w; i++) if (lit(i, j, level)) f.put(i, j, t)
    },
    line(p0, p1, t = INK, dash = null) {
      line(p0, p1).forEach(([x, y], s) => (!dash || dash(s)) && f.put(x, y, t))
    },
    clear: () => next.fill(0),
    show(first = false) {
      const from = shown.slice()
      const to = next.slice()
      shown = to
      clearInterval(busy)
      if (first || still) return paint(to)
      let level = 0
      const step = () => {
        level += 4
        paint(level >= 16 ? to : mix(from, to, cols, level))
        if (level >= 16) clearInterval(busy)
      }
      step()
      busy = setInterval(step, 1000 / TPS)
    },
    redraw: () => paint(shown),
    word(text, x, y, { face = prose, align = 'left', cls = '' } = {}) {
      const at = left(width(text, face), x, align)
      const s = document.createElement('span')
      s.className = `w ${face === mono ? 'm' : ''} ${cls}`
      s.textContent = text
      s.style.left = `calc(${at} * var(--u))`
      s.style.top = `calc(${Math.round(y) - face.TOP} * var(--u))`
      words.append(s)
      return s
    },
    words,
    reset() {
      words.textContent = ''
      next = new Uint8Array(cols * rows)
    },
  }
  return f
}
