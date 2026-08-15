# LTS — Logistics Tracking System

Follows a shipment's whole life, from loading abroad to acceptance in a store, across every
country the company operates in.

A shipment travels: **loading → export customs → departure → arrival in target country → import
customs → crossdock**. At the crossdock it is **split into transfers**, one per store, and each
transfer continues: **crossdock departure → store arrival → store pre-acceptance → store
acceptance**. LTS tracks both halves, scores every step against a KPI target, and lets logistics
companies and brokers enter their own dates without seeing each other's.

Countries differ in *how* their data arrives, never in *what* it means. An integration layer —
invisible to users — normalises every source system onto one standard status model, so the
screens, statuses and KPIs are identical everywhere.

---

## Running it

Requires the **.NET 9 SDK** and **SQL Server LocalDB** (both already present on the original
development machine).

```bash
dotnet build
dotnet test
dotnet run --project src/LTS.Web
```

The app creates and migrates its database on startup. In `Development` it also seeds a full demo
dataset — 2 countries, ~240 shipments, ~800 transfers, KPI targets, mock integration sources —
so every screen has something real to show.

Then open the app and sign in:

| Account | Password | What it demonstrates |
|---|---|---|
| `admin@lts.local` | `ChangeMe!2026` | Everything, including user and integration administration |
| `logistics@lts.local` | `Demo!Pass2026` | Logistics department: all shipments, all date fields |
| `carrier@lts.local` | `Demo!Pass2026` | Logistics company: only its own shipments, only its own date fields |
| `broker@lts.local` | `Demo!Pass2026` | Broker: only its own shipments, only the two customs dates |

The admin password is a bootstrap credential and must be changed at first sign-in.

**Settings** live under `Lts` in `appsettings.json`: `SeedDemoData`, `ApplyMigrationsOnStartup`,
the initial admin, and the integration poller's interval and mock data folder.

---

## How it is put together

```
src/
├─ LTS.Domain          entities, the milestone and KPI catalogs, scoring rules (no dependencies)
├─ LTS.Application     services, DTOs, permission model, Excel import, integration contracts
├─ LTS.Infrastructure  EF Core + SQL Server, Identity, adapters, the poller
└─ LTS.Web             Blazor Server + MudBlazor
tests/
└─ LTS.Tests           81 tests over the scoring, permission, milestone and import rules
```

### The standard status model

Twelve milestones, fixed in code in `MilestoneCatalog`, each with an owner, a lifecycle position
and the status it confers. This one catalog drives the entry form's field groups, the Excel
template, the status-mapping dropdown and the current-status calculation, so those can never
disagree with each other.

Status is never stored as an independent fact — it is derived from the dates that exist
(`TrackingStatusCalculator`), so the two cannot drift apart.

### KPI scoring

Targets are given in days per step, keyed on **export type + loading country + arrival country**.
Any key left blank means "any", and the most specific matching row wins, so a broad fallback can
sit underneath country-specific numbers. Targets are versioned by effective date and read as of
the shipment's loading date, so revising a KPI does not re-score journeys that already happened.

`KpiEvaluator` is pure and fully unit-tested. A finished step is **On Time** or **Late**; a step
still running is **On Track**, **At Risk** (past 80% of target) or **Overdue**. A shipment's
Performance column is its worst step.

Day counting goes through `IDayCounter` — calendar days today, with a working-day counter able to
be dropped in per country later.

### The integration layer

```
[Poller]  → adapter.FetchAsync(cursor)         one adapter per country system
          → canonical DTOs                     the only shape the rest of LTS knows
          → StatusMapping: raw code → milestone   admin-editable, no release needed
          → MilestoneService.ApplyAsync           audits, recalculates, saves
```

LTS **pulls** on a schedule, so no country has to open a route inwards or change its systems.
Onboarding a country is: add the country and its master data, write one adapter class, add its
integration source and status mappings. Nothing in the domain, the KPI engine or the UI changes.

`MockJsonAdapter` ships with the app and reads sample payloads from `src/LTS.Web/SampleData`, so
the whole path — poll, map, apply, audit, monitor — runs before any real endpoint exists.
`HttpJsonAdapter` is the base class real country adapters subclass.

Unmapped codes are not silently dropped: they are counted, surfaced on the integration monitor,
and mappable in one click.

### Access control

Three layers, all enforced server-side:

1. **Country** — which countries an account may enter at all.
2. **Page** — view/edit per page, *per country*, so someone can edit in Türkiye and only read in Poland.
3. **Row** — brokers and logistics companies see only shipments where they are the assigned partner.
   Applied in the query layer (`ShipmentScope`), so no page, export or deep link can escape it.
4. **Field** — on Shipment Details, a broker sees only the customs dates and a carrier only its
   own; store pre-acceptance and acceptance come from the in-house service and are never editable.

Accounts are created by an administrator with a generated one-time password — there is no
self-registration, because external partners are onboarded deliberately.

### Auditing

Every date change is written to `MilestoneAudit` with its old value, new value, source
(manual / Excel / integration / in-house) and who made it. When an integration overwrites
something a person typed, the typed value survives in the log — and a source can be configured to
defer to manual entry instead, in which case the incoming value is still recorded.

---

## Pages

| Page | What it does |
|---|---|
| Country chooser | Offered after sign-in; the country then lives in every route |
| Shipments | All seven attributes, every date to crossdock arrival, counts, status, performance |
| Transfers | The store legs: transfer no, receiver, status, performance, boxes/items, the five store dates |
| Shipments On The Way | Dashboard of everything short of a store arrival — where, how late, whose |
| Shipment Details | Date entry, showing only the fields the account owns |
| Date Upload | Excel bulk entry: template → validate → preview → commit → error report |
| Admin | Users, countries, master data, KPI targets (+ Excel), integrations, status mappings, run monitor, audit log |
