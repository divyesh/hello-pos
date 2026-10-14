# Quickstart: Business Settings

Prerequisites: the local environment, the web back office, an owner with an open business, a manager of
it, and (for step 6) a paired till.

## Automated

```powershell
.\scripts\dotnet.ps1 test --solution backend/HelloPos.slnx
.\scripts\dotnet.ps1 test --solution web/HelloPos.Web.slnx
cd android; $env:JAVA_HOME='C:\Program Files\Android\Android Studio2\jbr'; .\gradlew assembleDebug testDebugUnitTest lintDebug
```

## Manual

1. **Payment types** (owner): Settings → Payment types shows Cash. Add "UPI" (digital wallet), "Card
   (terminal)" (external card), "Cheque"; move UPI above Cash; deactivate Cheque. Cash cannot be deactivated.
2. **As a manager**: the payment types and the discount settings are read-only; tables are editable.
3. **Discounts and tips** (owner): the limit shows 0%; set 10% and an amount of 20.00; turn tips on with
   10, 15, 20.
4. **Tables**: turn on "uses tables", add the area "Main hall", bulk add 1–20 with 4 seats, add "Patio" and
   T1–T5; try 1–5 again in Patio (refused, names listed); deactivate table 20; try to deactivate Patio while
   it has tables (refused).
5. **History**: Settings → History shows every change above with who and when.
6. **Till**: refresh the catalog on the till; the next selling feature uses the data. Check the download with
   the API (or the till's database) contains the payment types in order, the tables and the limit.
7. **Timing**: SC-001 (payment types, limit and tips in under 5 minutes) and SC-002 (3 areas, 60 tables in
   under 5 minutes).
