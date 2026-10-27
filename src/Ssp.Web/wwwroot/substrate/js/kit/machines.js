export const CONFIRM = {
  idle: { press: 'armed' },
  armed: { press: 'fired', leave: 'idle', quiet: 'idle' },
  fired: {},
}
export const confirm = (state, event) => CONFIRM[state][event] || state

export function choice(role, on, i) {
  if (role === 'checkbox' || role === 'switch')
    return { on: on.map((v, j) => (j === i ? !v : v)), was: -1 }
  return { on: on.map((_, j) => j === i), was: on.indexOf(true) }
}

export const DISCLOSE = {
  closed: { press: 'opening' },
  opening: { done: 'open' },
  open: { press: 'closing' },
  closing: { done: 'closed' },
}
export const disclose = (state, event) => DISCLOSE[state][event] || state

export const stepped = (v, d, floor = 0) => Math.max(floor, Math.min(9, v + d))

export const inside = (b, x, y) => x >= b.left && x < b.right && y >= b.top && y < b.bottom
export const dist = (b, x, y) =>
  Math.hypot(Math.max(b.left - x, 0, x - b.right), Math.max(b.top - y, 0, y - b.bottom))
export function resolve(ts, x, y, near = true) {
  const holding = ts.filter((t) => inside(t.pad, x, y))
  if (!holding.length) return null
  const d = holding.map((t) => dist(t.r, x, y))
  const min = Math.min(...d)
  const meant = holding.filter((_, i) => d[i] - min < 0.01).map((t) => t.id)
  const got = near ? meant[0] : holding[holding.length - 1].id
  return { meant, got }
}
