# Quickstart: Owner Sign-up and Businesses

## Automated

`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` and `... web/HelloPos.Web.slnx` pass,
covering: sign-up/sign-in/sign-out, no enumeration (SC-003), lockout after 5 failures (SC-004), rate
limits, verification and reset links single-use and expiring (SC-006), reset ends other sessions,
business creation with country defaults (SC-005), field validation, owners seeing only their own
businesses and "not found" for others (isolation harness, SC-002), opening/switching, stale-tab `409`,
CSRF header required; web pages (bUnit).

## Manual (local environment)

1. `scripts/env.ps1 up --api` (adds Mailpit: http://localhost:8025), then run the web app
   (`scripts/dotnet.ps1 -Port 5090 run --project web/src/HelloPos.Web`), open http://localhost:5090.
2. Sign up → the verification email appears in Mailpit → follow the link → signed in, "create your
   first business".
3. Create a business in India: the form pre-fills INR, tax-inclusive, April, Asia/Kolkata, ₹1 rounding.
   Save; it opens; the header shows its name. Time it (SC-001: under 3 minutes from sign-up).
4. Create a second business (Canada), switch between them; edit a setting in one; confirm the other is
   unchanged.
5. Open two tabs on different businesses; save in the stale tab → "this tab is out of date".
6. Sign up a second owner in a private window: they see none of the first owner's businesses; pasting a
   business id of the first owner shows "not found".
7. "Forgot password" → link in Mailpit → new password → the first browser's session is signed out.
8. Five wrong passwords → locked for 15 minutes with the same generic message.
