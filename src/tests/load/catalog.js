// k6 load test for the public catalog (the read-heavy path the Redis cache exists for).
//
//   docker run --rm -i --network <compose network> -v <this folder>:/load grafana/k6 run \
//     -e BASE_URL=http://<api>:8080 -e VUS=50 -e DURATION=45s /load/catalog.js
//
// Or just run ./run-load-test.ps1, which starts the API twice (cache on, cache off) and compares.
import http from 'k6/http'
import { check } from 'k6'

const BASE = __ENV.BASE_URL || 'http://localhost:5233'

export const options = {
  scenarios: {
    browse: { executor: 'constant-vus', vus: Number(__ENV.VUS || 50), duration: __ENV.DURATION || '45s' },
  },
  thresholds: {
    // Correctness first: nothing may fail, whatever the speed.
    http_req_failed: ['rate<0.001'],
    checks: ['rate>0.999'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
}

// ---- helpers -------------------------------------------------------------------------------------------------
const pick = (items) => items[Math.floor(Math.random() * items.length)]

/** Real traffic is skewed: a few popular products/queries get most of the hits. 80% of picks come from the first 20%. */
function popular(items) {
  const hot = Math.max(1, Math.floor(items.length * 0.2))
  return Math.random() < 0.8 ? items[Math.floor(Math.random() * hot)] : pick(items)
}

const SORTS = ['name', '-name', 'price', '-price']
const SOCKETS = ['AM5', 'LGA1700', 'LGA1851']
const COLORS = ['Black', 'White', 'Silver', 'Red']
const WORDS = ['Acme', 'Zenith', 'Orbit', 'Nova', 'Pixel', 'Vertex']

export function setup() {
  const categories = http.get(`${BASE}/api/categories`).json().map((c) => c.slug)

  // A pool of real SKUs, taken from the first pages of results.
  const skus = []
  for (let page = 1; page <= 8; page++) {
    const result = http.get(`${BASE}/api/products?pageSize=100&page=${page}&sort=name`).json()
    for (const item of result.items) skus.push(item.sku)
  }
  if (!categories.length || !skus.length) throw new Error('the API has no catalog data: run seed-load-data.sql')
  return { categories, skus }
}

function listUrl(data) {
  const params = [`category=${popular(data.categories)}`, `sort=${popular(SORTS)}`]
  if (Math.random() < 0.8) params.push(`page=${popular([1, 2, 3, 4])}`)
  if (Math.random() < 0.4) params.push(`spec=socket:${popular(SOCKETS)}`)
  if (Math.random() < 0.2) params.push(`spec=color:${popular(COLORS)}`)
  if (Math.random() < 0.2) params.push(`minPrice=${popular([0, 50, 100])}&maxPrice=${popular([150, 300, 1000])}`) // bucketed so queries repeat
  if (Math.random() < 0.1) params.push(`q=${popular(WORDS)}`)
  return `${BASE}/api/products?${params.join('&')}`
}

// ---- the workload --------------------------------------------------------------------------------------------
export default function (data) {
  const roll = Math.random()
  let response, kind

  if (roll < 0.4) {
    kind = 'list'
    response = http.get(listUrl(data), { tags: { kind } })
    check(response, { 'list: 200 with items': (r) => r.status === 200 && Array.isArray(r.json('items')) })
  } else if (roll < 0.65) {
    kind = 'detail'
    response = http.get(`${BASE}/api/products/${popular(data.skus)}`, { tags: { kind } })
    check(response, { 'detail: 200 with specs': (r) => r.status === 200 && !!r.json('specs') })
  } else if (roll < 0.8) {
    kind = 'categories'
    response = http.get(`${BASE}/api/categories`, { tags: { kind } })
    check(response, { 'categories: 200': (r) => r.status === 200 })
  } else if (roll < 0.9) {
    kind = 'facets'
    response = http.get(`${BASE}/api/products/facets?category=${popular(data.categories)}`, { tags: { kind } })
    check(response, { 'facets: 200 with brands': (r) => r.status === 200 && r.json('brands').length > 0 })
  } else {
    // Stock levels are deliberately never cached, so this is the same with the cache on or off.
    kind = 'availability'
    response = http.get(`${BASE}/api/products/${popular(data.skus)}/availability`, { tags: { kind } })
    check(response, { 'availability: 200': (r) => r.status === 200 })
  }
}
