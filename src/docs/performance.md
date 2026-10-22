# Catalog performance: Redis cache on vs off

The catalog (browse, search, product pages) is the read-heavy path and the reason the Redis cache exists. This page records
what the cache is worth, how that was measured, and how to repeat it.

## Result

Same workload, same machine, 50 concurrent users for 45 seconds, 5,050 products:

| | Throughput | Median | p90 | p95 | p99 | Errors |
|---|---:|---:|---:|---:|---:|---:|
| **Cache off** (every read hits SQL) | 2,219 req/s | 17.8 ms | 42.8 ms | 56.4 ms | 94.3 ms | 0 / 100,139 |
| **Cache on** (Redis) | **12,904 req/s** | **3.0 ms** | **6.7 ms** | **7.8 ms** | **10.9 ms** | 0 / 581,133 |
| Improvement | **5.8×** | 5.9× | 6.4× | **7.2×** | 8.7× | |

Every response was checked for correctness (status and shape), not just speed: 100% of 681,000+ checks passed in both modes.

### Reading the numbers

* **Without the cache the database is the bottleneck.** Throughput is limited by SQL Server work per request (filters on specs
  use `EXISTS` sub-queries, plus counts and paging), and tail latency is long.
* **With the cache the API itself is the limit.** A cache hit is one Redis round trip and a JSON deserialization, so latency is
  a few milliseconds and the tail is tight (p99 under 11 ms).
* **10% of the traffic is stock lookups, which are deliberately never cached** (`/api/products/{sku}/availability` must always
  be current because it drives "in stock" and checkout). The cached run includes them, so the improvement is not inflated by
  an easy workload.
* **Invalidation is cheap.** An admin edit changes one version key; the next read of each item rebuilds it. The cache is
  correct after a price edit with no waiting (covered by an end-to-end test).

## How it was measured

| | |
|---|---|
| Tool | [k6](https://k6.io), in Docker, on the same Docker network as the API |
| Workload (`tests/load/catalog.js`) | 40% product lists (category, sort, page, spec filters, price range, text search), 25% product detail, 15% categories, 10% facets, 10% availability |
| Realism | Popular items and queries get most of the traffic: 80% of picks come from the "hot" 20%. Filter values are bucketed so queries repeat, as real ones do |
| Data | The 50 sample products plus 5,000 generated ones (`tests/load/seed-load-data.sql`), each with 3 specs and stock |
| System under test | The production API image in `Production` mode; SQL Server 2022 and Redis 7 containers; all on one Windows machine |
| Procedure | Fresh API container per mode, Redis flushed, 8 s unmeasured warm-up, then 45 s measured at 50 virtual users |
| Rate limiting | Raised for the test (otherwise the per-IP limit would, correctly, reject the load) |

Cache off is a real switch (`Catalog__Cache__Enabled=false`), not the in-memory fallback, so the comparison is against an
uncached API rather than a differently cached one.

## What this does and doesn't tell you

* **Treat the ratios as meaningful and the absolute numbers as not.** Everything shared one machine and the databases are local
  containers. In Azure, SQL is a network hop away (so the uncached path is slower and the cache is worth *more*), but the API,
  Redis and the load generator also behave differently. Re-run against a deployed environment before capacity planning.
* **It is a read test.** Writes (checkout, admin edits) were not under load. They are low-volume by comparison and go through
  the transactional outbox and queues.
* **One SKU of hardware, one data size.** With 5,050 products the whole result set fits in cache comfortably. A much larger
  catalog would want a longer look at memory and hit rate (the TTL is configurable: `Catalog:Cache:TtlSeconds`, default 300).

## Repeat it

```powershell
cd src
$env:SQL_PORT = 14330                                  # only if port 1433 is taken
docker compose --profile apps up -d --build            # creates the schema and the sample products
docker compose --profile apps stop api store-web admin-web
cd tests/load
./run-load-test.ps1 -Vus 50 -Duration 45s              # seeds 5,000 products, runs cache-on and cache-off, prints the table
```

Raw k6 summaries are written to `tests/load/results/` (git-ignored).
