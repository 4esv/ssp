export function growth(cells, sx, sy, reach = 4) {
  const at = new Map(cells.map((c, i) => [c[1] * 1e5 + c[0], i]))
  const depth = new Array(cells.length).fill(-1)
  let left = cells.length
  let front = []
  let d = 0
  const seed = () => {
    let best = -1
    let bd = Infinity
    cells.forEach(([x, y], i) => {
      const dd = (x - sx) ** 2 + (y - sy) ** 2
      if (depth[i] < 0 && dd < bd) ((bd = dd), (best = i))
    })
    depth[best] = d
    left--
    front = [best]
  }
  seed()
  while (left) {
    const next = []
    d++
    for (const i of front)
      for (let dy = -reach; dy <= reach; dy++)
        for (let dx = -reach; dx <= reach; dx++) {
          const j = at.get((cells[i][1] + dy) * 1e5 + cells[i][0] + dx)
          if (j !== undefined && depth[j] < 0) ((depth[j] = d), left--, next.push(j))
        }
    front = next
    if (!front.length && left) seed()
  }
  return depth
}

export function rooms(total, line, shown) {
  const n = Math.ceil(total / line)
  return Array.from({ length: Math.max(1, n) }, (_, j) =>
    Math.min(total, (shown ? j + 1 : n - j - 1) * line)
  )
}

export function wave(depth, forward) {
  const max = Math.max(0, ...depth)
  const per = Math.max(1, Math.ceil(max / 10))
  return {
    lit: (d, t) => (forward ? d <= t * per : d < max - t * per),
    more: (t) => (t + 1) * per <= max + per,
  }
}

export const ease = (t) => 1 - (1 - t) ** 3
export const beats = (dx, k) => Math.max(4, Math.min(9, Math.round(Math.abs(dx) / (10 * k))))
export function edges(t, right) {
  const lead = ease(t)
  const trail = ease(Math.max(0, (t - 0.3) / 0.7))
  return right ? [trail, lead] : [lead, trail]
}
