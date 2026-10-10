# Hello POS

Hello POS is a rewrite of OHY POS: a multi-business point-of-sale system for India, the USA and Canada.
It replaces the old .NET 8 / MySQL backend, the old admin website, and the old Android app
(`com.recbook.ohypos`), fixing their main problems: money rounding errors, sales lost or
duplicated on network failure, data leaking between businesses, and tokens stored in plain text.

> Status: planning. No application code yet.

## Applications

| App | Folder | Stack | Used by |
|-----|--------|-------|---------|
| API and backend | `backend/` | .NET 10, ASP.NET Core minimal APIs, vertical slices, EF Core, PostgreSQL | All apps |
| Web back office | `web/` | Blazor WebAssembly (.NET 10), MudBlazor | Owners and managers: sign-up, businesses, catalog, taxes, staff, payment settings, reports |
| POS | `android/` | Kotlin, Jetpack Compose, Hilt, Room, WorkManager | Cashiers: selling, checkout, orders, printing |
| Contract | `contracts/` | OpenAPI, shared pricing test cases | Source of truth between the three apps |

The web back office and the Android app talk only to the API. Card payments go through Square or
Clover. The system is hosted on DigitalOcean.

## Documents

Read these before changing anything:

- [Constitution](.specify/memory/constitution.md): the rules every change must follow (exact
  money, no lost or duplicate sales or charges, tenant isolation, append-only sales,
  contract-first API, tests first).
- [Roadmap](specs/roadmap.md): v1 split into phases and features.
- [Pricing rules](specs/pricing-rules.md): how a sale's totals, taxes, discounts, tips and
  rounding are calculated.
- Feature specs: `specs/NNN-feature-name/` (spec, plan, tasks), created per feature.

## How we work

Development follows [Spec Kit](https://github.com/github/spec-kit). Each roadmap feature gets its
own branch and goes through:

`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-analyze` → `/speckit-implement`

Every plan is checked against the constitution. API changes start in the OpenAPI contract.

## Android app structure

Single `app` module with strict package boundaries:

```
app/src/main/java/<package>/
├── core/
│   ├── money/        Money type, formatting
│   ├── network/      Retrofit setup, auth interceptor, token refresh, error mapping
│   ├── printing/     PrinterService, ESC/POS implementation, receipt layout
│   ├── designsystem/ theme, shared Compose components
│   └── util/
├── data/
│   ├── remote/       API interfaces and DTOs
│   ├── local/        Room database, DAOs, entities
│   └── repository/   repositories that combine remote and local
├── domain/           pure Kotlin, no Android imports
│   ├── model/        Product, Cart, CartItem, Sale, Tax, ...
│   ├── pricing/      pricing rules, tested against contracts/pricing-cases.json
│   └── usecase/
└── feature/
    ├── auth/
    ├── pos/          catalog + bill
    ├── checkout/
    ├── pending/
    ├── sales/
    ├── reports/
    ├── kitchen/
    └── settings/     printer, account, logout
```

Each feature has a `Screen` (Compose), a `ViewModel` and a `UiState`. ViewModels call
repositories and use cases, never Retrofit or Room directly. `BLUETOOTH_CONNECT` and
`BLUETOOTH_SCAN` are requested on Android 12+ before printing.

## Open decisions

| Question | Current assumption |
|----------|--------------------|
| Android application ID | Decided: `com.recbook.hellopos` (installs next to the old app) |
| Paid plans | Decided: subscriptions for the hosted service, paid on the web and checked by the API (roadmap Phase 2). No license key in the app. Billing can be switched off for self-hosted installs. |
| Subscription lapse behavior | Read-only or blocked, decided in roadmap #9 |
| Billing provider | Must work in India, the USA and Canada; decided in roadmap #8 |
| Email and SMS receipt providers | Decided in the receipts feature (roadmap #25) |
| Open pricing questions | Listed at the end of [pricing rules](specs/pricing-rules.md) |

## Building

Not available yet. Phase 0 of the roadmap adds the repository skeleton, a one-command local
environment (Podman compose with PostgreSQL and a mock API), and CI.

Requirements (expected): .NET 10 SDK (stable), Podman, Android Studio (latest stable) with
JDK 17+.

## License

[Apache License 2.0](LICENSE).
