# LTS — Logistics Tracking System

Follows a shipment's whole life, from loading abroad to acceptance in a store, across every
country the company operates in.

A shipment travels: **loading → export customs → departure → arrival in target country → import
customs → crossdock**. At the crossdock it is **split into transfers**, one per store, and each
transfer continues: **crossdock departure → store arrival → store pre-acceptance → store
acceptance**. LTS tracks both halves, scores every step against a KPI target, and lets logistics
companies and brokers enter their own dates without seeing each other's.

The app runs entirely on **`LTS_Integration`**, an external SQL Server database owned by the
company's own systems — every page reads and writes it, and nothing else. Data arrives either
through scheduled feed polls from the company's internal APIs, or typed in directly through
Shipment Details or bulk Excel upload — the pages and KPI engine don't care which.

---

## Running it

Requires the **.NET 9 SDK** and access to the external **`LTS_Integration`** SQL Server database
(connection string `LtsIntegration`) — there is no local database to set up.

```bash
dotnet build
dotnet test
dotnet run --project src/LTS.Web
```

Sign-in accounts are created by an administrator — there is no self-registration. On first run,
if no admin account exists yet, one is bootstrapped from `Lts:Admin` in `appsettings.json`
(default `admin@lts.local` / `ChangeMe!2026`, forced password change at first sign-in). Every
other account is created from **Admin > Users** with a generated one-time password shown once.

**Settings** live under `Lts` in `appsettings.json`:

| Key | Purpose |
|---|---|
| `Admin` | The bootstrap administrator's email, name and initial password |
| `ShipmentFeed` | Poll interval, base URL and secret name for the shipment-header feed |
| `ExportAttributeFeed` | Poll interval, base URL and secret name for the attribute-backfill feed |
| `ShipmentStatusReconciliation` | Poll interval for catching up stale `CurrentStatus` values |
| `Mail` | SMTP host/port/credentials for delay alert mails |
| `DelayAlerts` | How often the delay-alert scheduler checks whether any country's mail is due |

Feed and mail credentials are never stored in the database or in `appsettings.json` — each is read
at runtime from `Integration:Secrets:{SecretName}` (e.g. via `dotnet user-secrets` locally).

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
└─ LTS.Tests           72 tests over KPI scoring, permissions, tracking, Excel import and the feeds
```

### One database

`LTS_Integration` (connection string `LtsIntegration`) is the only database — there was originally
a second, app-owned LocalDB (`Lts`) from before this migration, backing an early, generic
per-country adapter/poller system (admin-editable status mappings, a run monitor) and a demo-data
seeder. Both were retired outright rather than migrated: the adapter system was superseded in
substance by the concrete `ShipmentFeed`/`ExportAttributeFeed` pollers below, which talk to the one
real API directly instead of through a pluggable-adapter abstraction designed before that API was
known, and the last of the code still touching that old database was deleted once nothing live
depended on it anymore. `LtsIntegrationDbContext`'s schema is managed by hand (never migrated by
EF) — it only ever maps tables that already exist. Every page reads and writes it through a set of
services named `Integration*` (`IntegrationShipmentQueryService`, `IntegrationMilestoneService`,
`IntegrationKpiAdminService`, `IntegrationAuditQueryService`, …) — a naming leftover from when a
non-`Integration`-prefixed counterpart of each existed side by side with the old database; none do
anymore, but the names stuck.

`MilestoneCatalog` (12 milestones — 7 shipment-scope, 5 transfer-scope) and `MilestoneType` are the
one vocabulary the whole app uses for a "milestone," written by `IntegrationMilestoneService`.

### How shipments get into `LTS_Integration`

Two independent, config-driven feed pollers pull from the company's own internal APIs — no country
has to open a route inwards:

- **Shipment Feed** (`ShipmentFeedPoller`, default every 5 minutes) — for each country with a
  configured customer code, calls `GetInvoiceListByCustomerCode` for shipment headers and the six
  attribute codes, then `GetInvoiceDetailByInvoiceNumber` per shipment for its boxes/stores.
  Standardizes raw codes against LTS's own lookup tables and upserts `LTS_Shipments` /
  `LTS_ShipmentTransfers` / `LTS_Boxes`. A transfer whose destination store isn't in `LTS_Stores`
  yet gets a bare placeholder row created for it (just the store's CurrAccCode — see "Stores"
  below) rather than being left unresolvable. Every raw response is staged (append-only) before
  being applied, and one bad shipment never stops the rest of the batch.
- **Export Attribute Feed** (`ExportAttributeFeedPoller`, default every 10 minutes) — finds
  shipments missing any of the four attributes that gate KPI scoring (Export Type, Loading Point,
  Arrival Customs, Transport Type), fetches each one's detail via `GetLTSExportFileDetail`, applies
  only the fields that came back non-blank, and re-scores that shipment's KPI immediately.

`tools/ShipmentFeedSimulator` runs the exact same standardize+upsert code both pollers use, fed
from API responses pasted by hand instead of a live HTTP call — useful for onboarding a country
before its real endpoint is reachable, or for reproducing a specific payload. It writes into a real
`LTS_Integration` database, same as the pollers.

### Romania (KLG OneClick)

A genuinely third-party integration, unlike the two feeds above — KLG's own "OneClick" API
(`api.oneclick.ro`), with OAuth-style rotating refresh tokens rather than a static bearer secret,
and a different data shape entirely: one KLG "domestic shipment" corresponds to one LTS **transfer**
(a single crossdock-to-store leg), not a whole multi-leg LTS shipment. So this poller never lists —
it looks a transfer up individually, once it has been linked to a KLG id by hand.

- **Linking** — on the Transfers page, Romania (`RO`) shows an extra "KLG Shipment ID" column right
  after Transfer No, where a person types in KLG's `perm_shipment_id` for that transfer
  (`LTS_ShipmentTransfers.RomaniaPermShipmentId`, gated by the page's normal edit permission).
- **`RomaniaShipmentPoller`** (default hourly) — every linked transfer that has no recorded Store
  Arrival date yet is looked up via `GET /api/v1/domestic-shipments?filter[perm_shipment_id]=...`.
  Three transfer-scope milestones are applied through the same `IIntegrationMilestoneService
  .ApplyAsync` path Shipment Details' manual entry uses, so status/KPI recompute and the
  `LTS_MilestoneAudit` trail happen exactly as they already do: `loading_act_start_date` →
  Crossdock Departure, `unloading_start_date` → Planned Store Arrival, `unloading_act_start_date` →
  Store Arrival. `shipment_date` is read but never applied — it maps to the shipment-scope Crossdock
  Arrival milestone, deliberately left alone by this per-transfer feed for now.
- **Independent of Crossdock Arrival** — `MilestoneApplyOptions.SkipChronologyValidation` (used only
  by this feed) skips `IntegrationMilestoneService`'s usual same-owner-chain prerequisite check, so
  these three dates apply whether or not a person has already entered Crossdock Arrival by hand —
  the automated pipeline never waits on that.
- **Tokens** — KLG invalidates the whole access/refresh pair on every refresh and reissues both, and
  every refresh token issued after the first is fixed at a 30-day lifetime regardless of what was
  configured at manual generation. `RomaniaTokenStore` persists the current pair encrypted (ASP.NET
  Core Data Protection) in `LTS_RomaniaOneClickToken`, refreshing proactively an hour before the
  access token expires, since `LTS_Integration` is a shared database — not app-private — and losing
  the current refresh token means a human has to regenerate a pair by hand in OneClick's UI.
- **Staging** — every lookup and every token refresh is staged into the same `LTS_ShipmentFeedStaging`
  table/lifecycle the internal shipment feed already uses (`RomaniaOneClickEndpointKinds`), except a
  token refresh's raw HTTP body is never staged verbatim — only a redacted summary — since it is the
  live token pair.

Configured under `Lts:RomaniaOneClick` (`Enabled`, `BaseUrl`, `ApiKeySecretName`,
`RefreshKeySecretName`, `PollSeconds`). `ApiKeySecretName`'s value is kept only for reference —
`RomaniaTokenStore` bootstraps straight from the refresh key and never trusts the manually-generated
access token's assumed lifetime. Both secrets are seeded once from the pair generated by hand in
OneClick's Company Profile → API Keys page.

Two hand-managed schema additions this integration needs on `LTS_Integration` (see "One database"
above — nothing here is ever migrated by EF):

```sql
ALTER TABLE LTS_ShipmentTransfers ADD RomaniaPermShipmentId NVARCHAR(50) NULL;

CREATE TABLE LTS_RomaniaOneClickToken (
    ID INT IDENTITY PRIMARY KEY,
    EncryptedAccessToken NVARCHAR(MAX) NOT NULL,
    EncryptedRefreshToken NVARCHAR(MAX) NOT NULL,
    AccessTokenExpiresAtUtc DATETIME2 NOT NULL,
    RefreshTokenExpiresAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);
```

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

### Stores

`LTS_Stores` (`StoreCode`, `StoreCurrAccCode`, `StoreDescription`, `City`, per country) is the one
piece of master data the shipment feed can write to as well as read: the feed only ever knows a
store by its **CurrAccCode** (the field `GetInvoiceDetailByInvoiceNumber` calls "StoreCode" is
actually this), never by the `StoreCode`/`StoreDescription` Master Data assigns. The Transfers grid
resolves each transfer's store by that CurrAccCode and shows "Store Code - Description" once an
admin has filled those in via **Admin > Master Data > Stores**, falling back to the raw CurrAccCode
until then. `City` is captured but not read by anything yet — it's there for scoping the Local
Transportation KPI leg per store/city, which hasn't been built (targets are still scored only by
country + the four shipment attributes, the same as every other leg).

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
| Admin > Master Data | Shared lookup tables (customs points, export types, transport types, …), plus per-country Stores |
| Admin > KPI Targets | Target days per KPI leg, per country, optionally scoped to specific attribute values |
| Admin > Delay Alerts | Per-country configuration for the two delay alert mails, plus manual "Send Now" |
| Admin > Audit Log | Every milestone date change, old/new value, source and author |
