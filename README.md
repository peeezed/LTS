# LTS — Logistics Tracking System

Follows a shipment's whole life, from loading abroad to acceptance in a store, across every
country the company operates in.

A shipment travels: **loading → export customs → departure → arrival in target country → import
customs → crossdock**. At the crossdock it is **split into transfers**, one per store, and each
transfer continues: **crossdock departure → store arrival → store pre-acceptance → store
acceptance**. LTS tracks both halves, scores every step against a KPI target, and lets logistics
companies and brokers enter their own dates without seeing each other's.

The app is mid-migration onto **`LTS_Integration`**, an external SQL Server database owned by the
company's own systems. All live tracking pages read and write it today; a couple of older pieces
(flagged below) still hang off the app's original, now largely retired database. Data arrives on
`LTS_Integration` either through scheduled feed polls from the company's internal APIs, or typed
in directly through Shipment Details or bulk Excel upload — the pages and KPI engine don't care which.

---

## Running it

Requires the **.NET 9 SDK**, and access to both a **SQL Server LocalDB** instance (the app's
original database, `Lts`) and the external **`LTS_Integration`** SQL Server database.

```bash
dotnet build
dotnet test
dotnet run --project src/LTS.Web
```

Sign-in accounts are created by an administrator — there is no self-registration. On first run,
if no admin account exists yet, one is bootstrapped from `Lts:Admin` in `appsettings.json`
(default `admin@lts.local` / `ChangeMe!2026`, forced password change at first sign-in). Every
other account is created from **Admin > Users** with a generated one-time password shown once.
Sign-in itself is checked against `LTS_Integration`, not the old database.

**Settings** live under `Lts` in `appsettings.json`:

| Key | Purpose |
|---|---|
| `ApplyMigrationsOnStartup`, `SeedDemoData` | Apply EF migrations / seed demo data into the **old** database only (see caveat below) |
| `Admin` | The bootstrap administrator's email, name and initial password |
| `Integration` | The legacy, per-country JSON-adapter poller (off by default) |
| `ShipmentFeed` | Poll interval, base URL and secret name for the shipment-header feed |
| `ExportAttributeFeed` | Poll interval, base URL and secret name for the attribute-backfill feed |
| `ShipmentStatusReconciliation` | Poll interval for catching up stale `CurrentStatus` values |
| `Mail` | SMTP host/port/credentials for delay alert mails |
| `DelayAlerts` | How often the delay-alert scheduler checks whether any country's mail is due |

Feed and mail credentials are never stored in the database or in `appsettings.json` — each is read
at runtime from `Integration:Secrets:{SecretName}` (e.g. via `dotnet user-secrets` locally).

**Known gap:** `SeedDemoData: true` seeds working demo logins (`logistics@lts.local`,
`carrier@lts.local`, `broker@lts.local`, password `Demo!Pass2026`) plus a full shipment dataset —
but the shipment data lands in the **old** database, which the live Shipments/Transfers/On The
Way/Shipment Details/Audit Log pages no longer read. Demo logins are therefore useful for
exercising the permission model and page shell, not for seeing realistic shipment data — there is
currently no seeder for `LTS_Integration` itself.

---

## How it is put together

```
src/
├─ LTS.Domain          entities, milestone/KPI catalogs, scoring rules (no dependencies)
├─ LTS.Application     services, DTOs, permission model, Excel import, feed/mail contracts
├─ LTS.Infrastructure  EF Core + SQL Server, Identity, the feed pollers, mail sending
└─ LTS.Web             Blazor Server + MudBlazor
tools/
└─ ShipmentFeedSimulator  standalone app for running real feed payloads through the real
                          standardize+upsert pipeline by hand, against a real LTS_Integration DB
tests/
└─ LTS.Tests           128 tests over KPI scoring, permissions, tracking, Excel import and the feeds
```

### Two databases, one in retreat

- **`LTS_Integration`** (connection string `LtsIntegration`) — the live database. Its schema is
  managed by hand (never migrated by EF); `LtsIntegrationDbContext` only ever maps tables that
  already exist. Identity, Shipments, Transfers, On The Way, Shipment Details, KPI Targets, Delay
  Alerts and the Audit Log all read and write here through a parallel set of `Integration`-prefixed
  services (`IntegrationShipmentQueryService`, `IntegrationMilestoneService`,
  `IntegrationKpiAdminService`, `IntegrationAuditQueryService`, …).
- **`Lts`** (the app's original LocalDB, EF-migrated) — still real for exactly one thing: **Admin >
  Integrations** (the old per-country JSON-adapter poller and its status mappings, which shows a
  warning banner if the old database is unreachable). Everything else that once lived here — the
  old audit log, the old KPI admin, Date Upload, the demo-data shipment set — has no live page
  reading it anymore.

Both sides share one vocabulary: `MilestoneCatalog` (12 milestones — 7 shipment-scope, 5
transfer-scope) and `MilestoneType` are used by the old and new writers alike, so a milestone means
the same thing everywhere; only *which database* records it differs.

### How shipments get into `LTS_Integration`

Two independent, config-driven feed pollers pull from the company's own internal APIs — no country
has to open a route inwards:

- **Shipment Feed** (`ShipmentFeedPoller`, default every 5 minutes) — for each country with a
  configured customer code, calls `GetInvoiceListByCustomerCode` for shipment headers and the six
  attribute codes, then `GetInvoiceDetailByInvoiceNumber` per shipment for its boxes/stores.
  Standardizes raw codes against LTS's own lookup tables and upserts `LTS_Shipments` /
  `LTS_ShipmentTransfers` / `LTS_Boxes`. Every raw response is staged (append-only) before being
  applied, and one bad shipment never stops the rest of the batch.
- **Export Attribute Feed** (`ExportAttributeFeedPoller`, default every 10 minutes) — finds
  shipments missing any of the four attributes that gate KPI scoring (Export Type, Loading Point,
  Arrival Customs, Transport Type), fetches each one's detail via `GetLTSExportFileDetail`, applies
  only the fields that came back non-blank, and re-scores that shipment's KPI immediately.

`tools/ShipmentFeedSimulator` runs the exact same standardize+upsert code both pollers use, fed
from API responses pasted by hand instead of a live HTTP call — useful for onboarding a country
before its real endpoint is reachable, or for reproducing a specific payload. It writes into a real
`LTS_Integration` database, same as the pollers.

### KPI scoring

Seven legs, `LoadingToCustomsClearance → CustomsToDeparture → InternationalTransportation →
CountryCustomsClearance → LeadTimeToXdock → Xdock → LocalTransportation`, fixed in
`IntegrationKpiCatalog`. The first five run entirely on the shipment; `Xdock` starts on the
shipment (Crossdock Arrival) but ends on a transfer (Crossdock Departure) and is scored once per
transfer; `LocalTransportation` (Crossdock Departure → Store Arrival) runs entirely on the
transfer. A shipment's `Performance` is the worst of its own five legs plus every transfer's Xdock
leg; a transfer's own `Performance` is the worst of its Xdock and Local Transportation legs.

Targets (`LTS_KpiTargets`) are given in days per leg, keyed on country + the four gating
attributes; any attribute left blank means "any," and the most specific matching row wins. A
shipment missing any of the four gating attributes scores `MissingAttributes` outright rather than
guessing a target for it.

`IntegrationKpiEvaluator`/`IntegrationKpiResolver` are pure and fully unit-tested: a finished leg is
**On Time** or **Late**; a running leg is **On Track**, **At Risk** or **Overdue**.
`IntegrationKpiCalculator` is the EF-touching layer that computes and persists each leg's deadline
and rolls the results up into the stored `Performance` columns. On the Shipments and Transfers
grids, any date past its own leg's deadline gets a small warning icon inline, so a late step is
visible without opening KPI columns.

### Delay alert mails

Two scheduled, Excel-attached daily mails per country, configured independently in **Admin > Delay
Alerts**: a **Shipment Delay Alert** (shipments not yet at Crossdock Arrival that are Late/Overdue
on their five shipment-only legs) and a **Transfer Delay Alert** (transfers not yet at their store
that are Late/Overdue on Xdock or Local Transportation). Each report is rebuilt fresh from raw
dates and current KPI targets at send time — never read off the stored `Performance` columns —
because a running leg can silently tip into Overdue purely from time passing. No delayed rows means
no mail that day. Each config also has a manual "Send Now" that doesn't consume the day's scheduled
slot, for checking a report before relying on the schedule.

### Access control

1. **Country** — which countries an account may enter at all.
2. **Page** — view/edit per page, *per country*, so someone can edit in Türkiye and only read in Poland.
3. **Row** — brokers and logistics companies see only shipments where they are the assigned
   partner, matched by company name against `LTS_Shipments.BrokerCompany`/`LogisticsCompany` in the
   query layer — so no page, export or deep link can escape it.
4. **Field** — on Shipment Details and the Date Upload template, a broker sees only the customs
   dates and a carrier only its own; store pre-acceptance and acceptance are never editable by
   either. Grids show every date read-only regardless of ownership, since tracking needs the full
   picture.

### Auditing

Every date change on `LTS_Integration` is written to `LTS_MilestoneAudit` with its old value, new
value, source (manual / Excel / feed / in-house service) and who made it — visible per country in
**Admin > Audit Log**, itself subject to the same partner-scoped row filtering as the tracking
grids. When a feed overwrites something a person typed, the typed value survives in the log.

---

## Pages

| Page | What it does |
|---|---|
| Country chooser | Landing page after sign-in; the country then lives in every route |
| Shipments | The seven attributes, every date to crossdock arrival, status, performance, optional KPI columns |
| Transfers | The store legs: transfer no, receiver, status, performance, boxes/items, the store dates |
| Shipments On The Way | Dashboard of everything short of a store arrival — where, how late, whose |
| Shipment Details | Date entry, showing only the fields the account owns, writing to `LTS_Integration` |
| Date Upload | Excel bulk entry: template → validate → preview → commit → error report, writing to `LTS_Integration` |
| Admin > Users | Create/manage accounts and their per-country, per-page permissions |
| Admin > Countries | The countries LTS operates in, and the customer code that ties them to feed data |
| Admin > Master Data | Shared lookup tables (customs points, export types, transport types, …) |
| Admin > KPI Targets | Target days per KPI leg, per country, optionally scoped to specific attribute values |
| Admin > Delay Alerts | Per-country configuration for the two delay alert mails, plus manual "Send Now" |
| Admin > Integrations | The old per-country adapter poller and its status mappings (legacy, old database) |
| Admin > Audit Log | Every milestone date change, old/new value, source and author |
