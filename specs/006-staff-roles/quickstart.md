# Quickstart: Staff and Roles

## Automated

`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` and `... web/HelloPos.Web.slnx` pass,
covering: the role rule table (kernel unit tests and API tests for every refused case, SC-003), PIN rules
and hashing, PIN verification failing for inactive staff (SC-004), last-owner rule including two owners
deactivating each other concurrently, version conflicts, invitations (single use, 7-day expiry,
revocation, wrong email gets the same 404, preview), the 005 migration (existing owners become owner
staff), staff isolation on the shared harness (SC-002), no PIN in any response or log (SC-005), and the
web pages (bUnit).

## Manual (local environment)

1. `scripts/env.ps1 up --api`, run the web app, sign in as an owner with a business, open it.
2. Staff page: you are listed as owner. Add a cashier "Ravi" with username `ravi` and PIN `4826`; time
   it (SC-001: under 1 minute). Try PIN `1234`: refused on the field.
3. Invite a manager by email; the invitation shows as pending; the email appears in Mailpit.
4. In a private window, follow the link: the page names the business and the role; sign up with the
   invited email, verify, accept; the business appears in the list with role manager.
5. As the manager: open the business, see staff; deactivate and reactivate Ravi; try to change Ravi to
   manager, or to deactivate the owner: refused. The settings page is not offered.
6. As the owner: deactivate the manager; the manager's next click is refused. Try to deactivate
   yourself while you are the only owner: "a business needs at least one active owner".
7. Edit Ravi in two tabs; save both: the second gets "changed by someone else, reload".
8. Invite a second owner, revoke the invitation, and confirm the link now says it is not valid.
