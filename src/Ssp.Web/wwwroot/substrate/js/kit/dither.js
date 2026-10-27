export const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]
export const LEVELS = 16
export const cells = (level, hide = false) => BAYER.map((b) => (hide ? b >= level : b < level))
export const stops = (list, n = LEVELS) =>
  list.map((m, i) => `${((i / n) * 100).toFixed(2)}% { mask-image: ${m} }`).join(' ')
const style =
  typeof document === 'undefined'
    ? null
    : document.head.appendChild(document.createElement('style'))

function tile(dev, level, hide) {
  const c = document.createElement('canvas')
  c.width = c.height = 4 * dev
  const g = c.getContext('2d')
  cells(level, hide).forEach(
    (on, i) => on && g.fillRect((i % 4) * dev, Math.floor(i / 4) * dev, dev, dev)
  )
  return `url(${c.toDataURL()})`
}

export function ditherFor(dev) {
  const size = (4 * dev) / devicePixelRatio
  const shown = Array.from({ length: LEVELS + 1 }, (_, l) => tile(dev, l, false))
  const hidden = Array.from({ length: LEVELS + 1 }, (_, l) => tile(dev, l, true))
  document.documentElement.style.setProperty('--dither', `${size}px`)
  const frames = (list) => stops(list)
  style.textContent = `
    @keyframes dither-in { ${frames(shown)} }
    @keyframes dither-out { ${frames(hidden)} }
    @keyframes dither-blink { ${stops([...hidden, ...shown.slice(1)], 2 * LEVELS)} }
    .dither-in, .dither-out, .dither-swap-in, .dither-swap-out, .dither-blink { mask-size: ${size}px ${size}px; mask-repeat: repeat; }
    .dither-in, .dither-swap-in { animation: dither-in var(--fade, 0.27s) linear both; }
    .dither-out, .dither-swap-out { animation: dither-out var(--fade, 0.27s) linear both; }
    ::view-transition-old(root) { animation: none; }
    ::view-transition-new(root) { animation: dither-in 0.4s linear both; mask-size: ${size}px ${size}px; }
    @media (prefers-reduced-motion: reduce) { .dither-in, .dither-out, .dither-swap-in, .dither-swap-out { animation-duration: 1ms; } }
  `
}

export function fade(el, way) {
  return new Promise((done) => {
    el.classList.remove('dither-in', 'dither-out')
    void el.offsetWidth
    el.classList.add(`dither-${way}`)
    const end = () => (
      el.removeEventListener('animationend', end),
      el.classList.remove(`dither-${way}`),
      done()
    )
    el.addEventListener('animationend', end)
  })
}
