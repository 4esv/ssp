export const over = (speed) => Math.max(0, Math.floor(Math.abs(speed)) - 1)

export function past(x, y, dx, dy, speed, ok) {
  let px = x
  let py = y
  for (let i = 0; i < over(speed) && (dx || dy); i++) {
    if (!ok(px + dx, py + dy)) break
    px += dx
    py += dy
  }
  return [px, py]
}
