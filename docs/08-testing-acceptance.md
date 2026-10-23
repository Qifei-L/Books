# Testing and acceptance

Status: Test plan draft. No cases in this document have been run. A confirmed requirement is not a passed test.

## Purpose

Turn the requirements into cases that can be checked and recorded. Preserve the organization and permission cases, then add accounting cases for review.

## Confirmed

The organization roles, one-ledger-per-company relationship, administrator handover, creation policy, multiple memberships, invitation policy, and explicit reauthorization rules are confirmed. The five API principles and MCP's use of the same backend are also confirmed.

The accounting policies used in the proposed cases below are not yet confirmed. Adding a test case does not approve its policy or prove that the product supports it.

## Proposed test setup

Use synthetic users, Organizations A and B, companies, and separate ledgers. Do not use real customer records.

Prepare these users:

- An organization administrator.
- An ordinary member with read-only access.
- An ordinary member with editable access.
- A removed member.
- A user who has not joined the organization.
- One user with different roles in A and B.

Check key permissions through both the API and MCP. Use controlled concurrent requests rather than trying to click quickly by hand.

## Organization and permission cases

All cases below are Not run. The Basis column separates confirmed business rules from proposed workflows and technical safeguards.

| ID | Basis | Scenario | Expected result |
| --- | --- | --- | --- |
| ORG-01 | Confirmed | An A administrator uses all A ledgers, then substitutes a B ledger ID without B access. | Allow valid A operations. Deny the B operation without exposing B data. |
| ORG-02 | Confirmed | An ordinary member opens an unassigned ledger or writes to a read-only ledger. | Deny access or the write. Leave no partial changes. |
| ORG-03 | Confirmed | An editable member invites someone, changes a role, or grants access. | Deny the action. Editable ledger access is not administrator access. |
| ORG-04 | Confirmed | The same user switches from administrator in A to ordinary member in B. | Apply B permissions. Do not carry A permissions into B. |
| ORG-05 | Confirmed | Create companies with their first-release ledger relationship. Use the same account code in two ledgers. | Each company has one independent ledger. Records with the same account code stay separate. |
| ORG-06 | Confirmed | The last administrator tries to leave, then assigns a replacement and tries again. | Deny departure without a replacement. Allow it once another administrator is active. |
| ORG-07 | Confirmed rule; concurrency safeguard to design | Two administrators leave, remove each other, or demote each other at the same time. | At least one active administrator remains after normal membership or role changes. Test concurrent as well as sequential requests. This must not block security suspension of a compromised account. |
| ORG-08 | Proposed enforcement workflow | A removed member uses an old session, retry, MCP call, or export request. | Check current permissions and deny access. Old sessions or cached permissions must not provide new access. |
| ORG-09 | Proposed concurrency workflow | A permission revocation and a write run together. | If revocation completes first, deny the write. If the write completes first, preserve that order in history. Leave no partial changes. |
| ORG-10 | Confirmed creation policy; proposed atomicity and retry mechanism | Fail either organization creation or first-administrator creation. Retry a successful request. | Do not leave an organization without its first administrator. Do not create a duplicate on retry. |
| ORG-11 | Confirmed invitation policy; proposed one-time acceptance mechanism | The intended user verifies their identity and actively accepts a valid invitation. | Create one membership with explicit permissions. Consume the invitation once. |
| ORG-12 | Confirmed invitation policy; server-time boundary check to design | Use a different identity, an invitation at or beyond its 7-day expiry, a revoked invitation, or no active acceptance. | Do not add membership or grant access. Test the expiry boundary against server time. |
| ORG-13 | Proposed invitation workflow | Repeat an invitation with identical content, then invite again with different permissions. | Avoid conflicting active invitations. A replacement invalidates the old version. |
| ORG-14 | Proposed invitation workflow | An existing member accepts an invitation again. | Do not duplicate membership, upgrade the role, or replace current permissions. |
| ORG-15 | Proposed concurrency workflow | Accept and revoke together, or accept twice at the same time. | Produce one valid state change. Revoking an already accepted invitation does not remove the member. |
| ORG-16 | Confirmed | A removed member accepts an old invitation, or the inviter is no longer an administrator. | Do not restore access. Require a new invitation from a current administrator. |
| ORG-17 | Confirmed | Demote an administrator to ordinary member, then remove and reinvite them. | Apply only explicit new permissions. Do not restore historical ledger access automatically. |
| ORG-18 | Proposed technical safeguard | Link a membership to another organization's ledger permission, or duplicate a user-organization membership. | Reject invalid links and duplicate memberships in both the service and storage layers. |
| ORG-19 | Proposed mechanism supporting confirmed API principles | Reuse an idempotency key for different input, or update a record using an old version. | Return a clear conflict. Do not return a result for different input or silently overwrite changes. |
| ORG-20 | Proposed query safeguards | Check pages, search suggestions, counts, and exports. | Include only authorized data. Keep a stable order and follow the agreed rules for changes between pages. |
| ORG-21 | Proposed history and audit safeguards | Inject a failure during a permission change. Inspect state, history, and audit records. | Successful changes and audit records agree. Failed changes do not appear successful. Keep historical operators. |
| ORG-22 | Confirmed | Attempt the same unauthorized operation through API and MCP. | Both deny it. MCP must not use elevated access to bypass the backend. |

Additional entry-flow checks, also Not run:

- ORG-23: An existing account signs in again, including after session expiry. Proposed interaction result: resolve the same account, show only accessible organizations and relevant invitations, and create no account or organization merely because of sign-in.
- ORG-24: A user selects an existing organization. Confirmed access result: recheck current membership and use its current permissions. A guessed organization ID or matching company name grants no access.
- ORG-25: A user with no organizations signs in. Proposed interaction result: show an empty list and explicit invitation-review or Create organization choices. Do not create anything automatically.
- ORG-26: The user explicitly requests organization creation. Confirmed creation policy: require current authenticated and verified identity; on success the creator is the first administrator. Proposed interaction and retry safeguards: failed verification creates nothing, and an uncertain response is checked before retrying the same operation without duplicates.
- ORG-27: A new account signs up but has not completed identity verification. Proposed entry flow: request verification separately from sign-in. Do not create an organization before verification and an explicit creation request.

These cases clarify the [entry and creation workflows](11-workflows.md#1-sign-in-choose-an-organization-or-create-one). They do not approve a new identity provider or claim that integration has been tested.

Additional confirmed-policy checks, also Not run:

- A registered user with verified identity can create an organization and becomes its first administrator.
- A user whose identity is not verified cannot create an organization under this policy.
- One account can join several organizations without registering again. Each membership and its permissions remain independent.

Proposed security exception check, also Not run: Suspend a compromised account that is the last usable administrator. Do not block the suspension to satisfy normal administrator-count protection. Check the separately proposed locked-organization recovery workflow before release.

## Proposed accounting cases

All cases below are Proposed and Not run. Review the accounting policies first, then finalize the expected results. Examples use synthetic amounts and do not choose a production currency, decimal scale, or rounding rule.

### Amounts and balanced entries

- ACC-01: Post an entry with debit 100 and credit 100 in the same ledger and currency. Proposed result: accept it once all other checks pass. Debit 100 and credit 99 must fail with no balance change.
- ACC-02: Use decimal amounts such as 0.10 and 0.20. Proposed result: calculate and compare amounts with the agreed decimal rules, without binary floating-point drift. Reject or round excess precision only as the approved policy specifies.
- ACC-03: Test zero amounts, negative amounts, missing amounts, and both debit and credit on one line. Proposed result: apply explicit line-validation rules. Chapter 05 proposes positive, single-sided lines and at least two non-zero lines at posting. Test that proposal once accepted; the test list does not itself approve it.
- ACC-04: Fail partway through posting an entry with several lines. Proposed result: save all required posting effects together or none. Do not leave partial journal lines or balances.

### Repeated posting and concurrent changes

- ACC-05: Submit the same posting twice, including a retry after a timeout and two simultaneous requests. Proposed result: one posted entry and one set of balance changes. The no-accidental-duplicates API principle is confirmed; the posting mechanism and lifecycle are not.
- ACC-06: Submit changed posting data using the same retry identifier. Proposed result: a clear conflict, not a second posting or the result for unrelated data. The identifier mechanism remains proposed.
- ACC-07: Two users edit or post the same draft. Proposed result: a clear conflict or a single valid state change. Do not silently lose an edit or post twice.

### Posted history and reversals

- ACC-08: Try to edit or delete a posted entry, including as an administrator. Proposed result: reject direct changes and keep the original entry. Immutability of posted entries still needs policy approval.
- ACC-09: Reverse a posted entry. Proposed result: create a linked correcting entry, retain the original, and show the combined balance effect correctly. The permitted reversal date, accounts, and permissions remain open.
- ACC-10: Retry or simultaneously request the same reversal. Proposed result: do not reverse the same effect twice. If multiple partial reversals are later allowed, test their limits under that explicit policy.

### Closed periods

- ACC-11: Try to post, backdate, edit, or reverse into a closed period. Proposed result: deny changes to that period. The period-close and reopening policies are not confirmed.
- ACC-12: Close a period while another request posts into it. Proposed result: one clear order. A posting cannot slip into the period after closure takes effect.
- ACC-13: Test the last accounting date of one period and the first date of the next. Proposed result: apply the agreed accounting calendar and business time zone consistently. Do not derive an accounting date from a timestamp without the agreed rule.

### Reports and ledger isolation

- ACC-14: Build a small known set of posted entries. Proposed result: ledger movements, account balances, trial balance, and report totals reconcile using the same scope and cutoff. The report set and calculation policies remain open.
- ACC-15: Add a draft and a reversal to the fixture. Proposed result: each report includes them only as the approved status and reversal policies require. State those policies in the test before running it.
- ACC-16: Query the same report with different page sizes and filters. Proposed result: pagination does not change the full-query total; a filter changes the total only by changing the query scope. Distinguish a page subtotal from a full-query total. The pagination principle is confirmed; report formulas remain proposed.
- ACC-17: Use identical account codes in two companies and query as a user with access to only one ledger. Proposed result: no mixed balances, unauthorized rows, counts, or totals. Ledger isolation is confirmed; report implementation remains to be designed.

## Proposed Suspense checks

The inclusion of incomplete, unusable and errored records in Suspense is confirmed. These handling scenarios remain proposed and **Not run**:

- SUS-01: Missing amounts, unbalanced lines or unusable accounts enter the review list without posting or balance changes.
- SUS-02: An otherwise valid, balanced entry with unknown classification can use a Suspense Account only under the agreed posting policy. Its posted balance appears in the accounting source, not as a fabricated review-list total.
- SUS-03: A response is lost after a possible commit. Show Pending / Unknown, check the saved outcome and retry safely without duplicate posting.
- SUS-04: Correct a record and validate again. Resolve an already-posted item through the agreed correction process; keep the original and resolution history.
- SUS-05: A user without ledger access cannot read the source record, issue count, error details or resolution history. Error records contain no authentication secrets.

## Proposed API contract checks

These cases support the confirmed API principles. They are Not run. Exact formats and mechanisms must be defined before execution.

- API-01: Distinguish success, validation failure, denied access, and system failure. Never represent an error as zero balance or an empty successful query.
- API-02: Test money, accounting dates, timestamps, omitted fields, null, empty lists, and zero against the agreed contract.
- API-03: Registration and organization creation do not require a ledger that does not yet exist. Ledger operations validate their ledger and organization context.
- API-04: Run equivalent authorized requests through API and MCP. Check the same business result and validation rules.
- API-05: Check filters, their combinations, default ordering, pages, and totals against the same contract across similar queries.
- API-06: Test an API or MCP contract change with an existing client. Follow the agreed compatibility or migration plan rather than silently changing field meanings.

## Evidence for each test

Cover service logic, storage constraints, and API/MCP integration where relevant. Inject failures between transaction steps. Check the final data, permissions, history, and audit records together.

Check that errors and logs do not expose another organization's data or credentials.

Record for every case:

1. Tested version and environment.
2. Data setup and exact steps.
3. Expected result and the rule it comes from.
4. Actual result.
5. Evidence, such as sanitized responses and state checks.
6. Conclusion: Not run, Passed, or Failed.

Writing this plan does not change any case from Not run to Passed. Adopt a proposed workflow before using it as a final acceptance requirement.

## Business and release decisions

- Review the proposed accounting policies in the [short business review list](10-decisions.md#short-business-review-list).
- Name the final acceptance decision-maker and agree on release readiness.
- Provide expected usage or recovery needs if known. Engineering should propose test volumes and explain any cost or service-level trade-off.

## Technical Design Tasks

These tasks belong to the later technical-design stage. Current review focuses on planning and business workflows; detailed expansion is paused.

Engineering owns test fixtures, concurrency scenarios, load levels, pass/fail evidence and the proposed technical release gates. Use the [contract's implementation milestones and minimum tests](design/phase-1-technical-contract.md#11-implementation-milestones-after-coding-approval) as the starting plan, not a new questionnaire for the owner. No tests are authorized or reported as run by this reclassification.

Candidate release blockers include unauthorized access, normal membership changes leaving an organization with no administrator, writes using revoked access, partial postings, and missing history or audit records. Security suspension of a compromised last administrator is a separate case: it must remain possible, with a reviewed locked-organization recovery workflow. These are proposed gates, not an approved release checklist.

This draft does not authorize tests on real books, real member creation, credential setup, code implementation, merges, or deployment.

[Organization and permission design](design/organizations-permissions.md) · [Back to documentation](README.md)
