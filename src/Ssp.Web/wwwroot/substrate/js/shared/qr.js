const EXP = new Uint8Array(512)
const LOG = new Uint8Array(256)
{
  let x = 1
  for (let i = 0; i < 255; i++) {
    EXP[i] = x
    LOG[x] = i
    x <<= 1
    if (x & 0x100) x ^= 0x11d
  }
  for (let i = 255; i < 512; i++) EXP[i] = EXP[i - 255]
}

function mul(a, b) {
  return a === 0 || b === 0 ? 0 : EXP[LOG[a] + LOG[b]]
}

function generator(n) {
  let g = [1]
  for (let i = 0; i < n; i++) {
    const next = new Array(g.length + 1).fill(0)
    for (let j = 0; j < g.length; j++) {
      next[j] ^= g[j]
      next[j + 1] ^= mul(g[j], EXP[i])
    }
    g = next
  }
  return g
}

function ecCodewords(data, n) {
  const g = generator(n)
  const rem = new Uint8Array(n)
  for (const byte of data) {
    const lead = byte ^ rem[0]
    rem.copyWithin(0, 1)
    rem[n - 1] = 0
    if (lead === 0) continue
    for (let j = 0; j < n; j++) rem[j] ^= mul(g[j + 1], lead)
  }
  return rem
}

const VERSIONS = [
  null,
  [10, 1, 16, 0, 0],
  [16, 1, 28, 0, 0],
  [26, 1, 44, 0, 0],
  [18, 2, 32, 0, 0],
  [24, 2, 43, 0, 0],
  [16, 4, 27, 0, 0],
  [18, 4, 31, 0, 0],
  [22, 2, 38, 2, 39],
  [22, 3, 36, 2, 37],
  [26, 4, 43, 1, 44],
]
export const MAX_VERSION = VERSIONS.length - 1

function dataCodewords(v) {
  const [, b1, d1, b2, d2] = VERSIONS[v]
  return b1 * d1 + b2 * d2
}

function countBits(v) {
  return v < 10 ? 8 : 16
}

export function capacity(v) {
  return Math.floor((dataCodewords(v) * 8 - 4 - countBits(v)) / 8)
}

function pickVersion(length) {
  for (let v = 1; v <= MAX_VERSION; v++) if (capacity(v) >= length) return v
  return null
}

function codewords(bytes, v) {
  const total = dataCodewords(v)
  const bits = []
  const push = (value, n) => {
    for (let i = n - 1; i >= 0; i--) bits.push((value >> i) & 1)
  }
  push(0b0100, 4)
  push(bytes.length, countBits(v))
  for (const b of bytes) push(b, 8)
  push(0, Math.min(4, total * 8 - bits.length))
  while (bits.length % 8) bits.push(0)
  const data = new Uint8Array(total)
  for (let i = 0; i < bits.length; i += 8) {
    let b = 0
    for (let j = 0; j < 8; j++) b = (b << 1) | bits[i + j]
    data[i / 8] = b
  }
  for (let i = bits.length / 8, k = 0; i < total; i++, k++) data[i] = k % 2 ? 0x11 : 0xec

  const [ec, b1, d1, b2, d2] = VERSIONS[v]
  const blocks = []
  let at = 0
  for (let i = 0; i < b1 + b2; i++) {
    const len = i < b1 ? d1 : d2
    const slice = data.subarray(at, at + len)
    blocks.push({ data: slice, ec: ecCodewords(slice, ec) })
    at += len
  }
  const out = []
  const longest = Math.max(d1, d2)
  for (let i = 0; i < longest; i++)
    for (const b of blocks) if (i < b.data.length) out.push(b.data[i])
  for (let i = 0; i < ec; i++) for (const b of blocks) out.push(b.ec[i])
  return out
}

function alignmentCentres(v) {
  if (v === 1) return []
  const size = 17 + 4 * v
  const n = Math.floor(v / 7) + 2
  const step = v === 32 ? 26 : Math.ceil((size - 13) / (2 * n - 2)) * 2
  const centres = [6]
  for (let p = size - 7; centres.length < n; p -= step) centres.splice(1, 0, p)
  return centres
}

function bch(data, deg, poly) {
  let rem = data
  for (let i = 0; i < deg; i++) rem = (rem << 1) ^ ((rem >>> (deg - 1)) * poly)
  return rem
}

function formatBits(mask) {
  const data = (0b00 << 3) | mask
  return ((data << 10) | bch(data, 10, 0x537)) ^ 0x5412
}

function versionBits(v) {
  return (v << 12) | bch(v, 12, 0x1f25)
}

const MASKS = [
  (r, c) => (r + c) % 2 === 0,
  (r) => r % 2 === 0,
  (r, c) => c % 3 === 0,
  (r, c) => (r + c) % 3 === 0,
  (r, c) => (Math.floor(r / 2) + Math.floor(c / 3)) % 2 === 0,
  (r, c) => ((r * c) % 2) + ((r * c) % 3) === 0,
  (r, c) => (((r * c) % 2) + ((r * c) % 3)) % 2 === 0,
  (r, c) => (((r + c) % 2) + ((r * c) % 3)) % 2 === 0,
]

class Matrix {
  constructor(size) {
    this.size = size
    this.modules = new Uint8Array(size * size)
    this.reserved = new Uint8Array(size * size)
  }
  set(r, c, dark, reserve = true) {
    const i = r * this.size + c
    this.modules[i] = dark ? 1 : 0
    if (reserve) this.reserved[i] = 1
  }
  get(r, c) {
    return this.modules[r * this.size + c]
  }
}

function placeFunctionPatterns(m, v) {
  const n = m.size
  for (const [r0, c0] of [
    [0, 0],
    [0, n - 7],
    [n - 7, 0],
  ]) {
    for (let r = -1; r <= 7; r++)
      for (let c = -1; c <= 7; c++) {
        const rr = r0 + r
        const cc = c0 + c
        if (rr < 0 || cc < 0 || rr >= n || cc >= n) continue
        const edge = r === -1 || r === 7 || c === -1 || c === 7
        const ring = r === 0 || r === 6 || c === 0 || c === 6
        const core = r >= 2 && r <= 4 && c >= 2 && c <= 4
        m.set(rr, cc, !edge && (ring || core))
      }
  }
  for (let i = 8; i < n - 8; i++) {
    m.set(6, i, i % 2 === 0)
    m.set(i, 6, i % 2 === 0)
  }
  const centres = alignmentCentres(v)
  const finder = (r, c) => (r === 6 && (c === 6 || c === n - 7)) || (r === n - 7 && c === 6)
  for (const r of centres)
    for (const c of centres) {
      if (finder(r, c)) continue
      for (let dr = -2; dr <= 2; dr++)
        for (let dc = -2; dc <= 2; dc++)
          m.set(r + dr, c + dc, Math.max(Math.abs(dr), Math.abs(dc)) !== 1)
    }
  for (let i = 0; i < 8; i++) {
    if (i !== 6) {
      m.set(8, i, false)
      m.set(i, 8, false)
    }
    m.set(8, n - 1 - i, false)
    m.set(n - 1 - i, 8, false)
  }
  m.set(8, 8, false)
  m.set(4 * v + 9, 8, true)
  if (v >= 7)
    for (let i = 0; i < 6; i++)
      for (let j = 0; j < 3; j++) {
        m.set(i, n - 11 + j, false)
        m.set(n - 11 + j, i, false)
      }
}

function placeFormat(m, mask) {
  const n = m.size
  const bits = formatBits(mask)
  const bit = (i) => (bits >> i) & 1
  for (let i = 0; i <= 5; i++) m.set(i, 8, bit(i))
  m.set(7, 8, bit(6))
  m.set(8, 8, bit(7))
  m.set(8, 7, bit(8))
  for (let i = 9; i < 15; i++) m.set(8, 14 - i, bit(i))
  for (let i = 0; i < 8; i++) m.set(8, n - 1 - i, bit(i))
  for (let i = 8; i < 15; i++) m.set(n - 15 + i, 8, bit(i))
}

function placeVersion(m, v) {
  if (v < 7) return
  const n = m.size
  const bits = versionBits(v)
  for (let i = 0; i < 18; i++) {
    const dark = (bits >> i) & 1
    const a = Math.floor(i / 3)
    const b = n - 11 + (i % 3)
    m.set(a, b, dark)
    m.set(b, a, dark)
  }
}

function placeData(m, words, mask) {
  const n = m.size
  const fn = MASKS[mask]
  let bit = 0
  const total = words.length * 8
  let upward = true
  for (let right = n - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5
    for (let k = 0; k < n; k++) {
      const r = upward ? n - 1 - k : k
      for (const c of [right, right - 1]) {
        if (m.reserved[r * n + c]) continue
        let dark = 0
        if (bit < total) dark = (words[bit >> 3] >> (7 - (bit & 7))) & 1
        bit += 1
        if (fn(r, c)) dark ^= 1
        m.set(r, c, dark, false)
      }
    }
    upward = !upward
  }
}

function penalty(m) {
  const n = m.size
  let score = 0
  const at = (r, c) => m.modules[r * n + c]
  for (let pass = 0; pass < 2; pass++) {
    const get = pass === 0 ? at : (r, c) => at(c, r)
    for (let r = 0; r < n; r++) {
      let run = 1
      for (let c = 1; c <= n; c++) {
        if (c < n && get(r, c) === get(r, c - 1)) {
          run += 1
          continue
        }
        if (run >= 5) score += 3 + (run - 5)
        run = 1
      }
      for (let c = 0; c + 7 <= n; c++) {
        const pat = [1, 0, 1, 1, 1, 0, 1].every((b, i) => get(r, c + i) === b)
        if (!pat) continue
        const before = c >= 4 && [1, 2, 3, 4].every((i) => get(r, c - i) === 0)
        const after = c + 10 < n && [7, 8, 9, 10].every((i) => get(r, c + i) === 0)
        if (before) score += 40
        if (after) score += 40
      }
    }
  }
  for (let r = 0; r + 1 < n; r++)
    for (let c = 0; c + 1 < n; c++) {
      const a = at(r, c)
      if (a === at(r, c + 1) && a === at(r + 1, c) && a === at(r + 1, c + 1)) score += 3
    }
  let dark = 0
  for (const b of m.modules) dark += b
  const pct = (dark * 100) / (n * n)
  const k = Math.floor(Math.abs(pct - 50) / 5)
  score += 10 * k
  return score
}

function encode(text, version, forcedMask) {
  const bytes = new TextEncoder().encode(String(text))
  const v = version ?? pickVersion(bytes.length)
  if (!v || bytes.length > capacity(v))
    throw new RangeError(`too long for a QR up to version ${MAX_VERSION}`)
  const words = codewords(bytes, v)
  const n = 17 + 4 * v
  let best = null
  const masks = forcedMask === undefined ? [0, 1, 2, 3, 4, 5, 6, 7] : [forcedMask]
  for (const mask of masks) {
    const m = new Matrix(n)
    placeFunctionPatterns(m, v)
    placeFormat(m, mask)
    placeVersion(m, v)
    placeData(m, words, mask)
    const score = penalty(m)
    if (!best || score < best.score) best = { m, mask, score }
  }
  return { size: n, version: v, mask: best.mask, modules: best.m.modules }
}

export function qrMatrix(text, { version, mask } = {}) {
  return encode(text, version, mask)
}

const QUIET = 4

export function qrSvg(text, label = 'QR code') {
  const { size, modules } = encode(text)
  const n = size + 2 * QUIET
  let d = ''
  for (let r = 0; r < size; r++)
    for (let c = 0; c < size; c++)
      if (modules[r * size + c]) d += `M${c + QUIET} ${r + QUIET}h1v1h-1z`
  const esc = String(label).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;')
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${n} ${n}" ` +
    `shape-rendering="crispEdges" role="img" aria-label="${esc}">` +
    `<path fill="currentColor" d="${d}"/></svg>`
  )
}

const TEXT_QUIET = 2
export function qrText(text) {
  const { size, modules } = encode(text)
  const n = size + 2 * TEXT_QUIET
  const light = (r, c) => {
    if (r < 0 || r >= n) return true
    const rr = r - TEXT_QUIET
    const cc = c - TEXT_QUIET
    if (rr < 0 || rr >= size || cc < 0 || cc >= size) return true
    return !modules[rr * size + cc]
  }
  const lines = []
  for (let r = 0; r < n; r += 2) {
    let line = ''
    for (let c = 0; c < n; c++) {
      const top = light(r, c)
      const bottom = light(r + 1, c)
      line += top && bottom ? '█' : top ? '▀' : bottom ? '▄' : ' '
    }
    lines.push(line)
  }
  return lines.join('\n')
}
