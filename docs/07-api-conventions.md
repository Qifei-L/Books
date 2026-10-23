# API conventions

Status: The five principles below and MCP as a backend entry point were confirmed on 2026-10-23. Exact contracts and implementation choices are still open. This is documentation work, not approval to build or deploy the API.

## Purpose

All clients should use the same business rules, permissions, and data meanings.

## Confirmed

### 1. Use one backend for all clients

All clients use the same backend API. Web development is deferred for now. Test tools can call the backend first.

MCP is another way to call the backend. It exposes defined business operations to AI. It must not write directly to the database, bypass the API, or create a separate set of accounting and permission rules.

An AI caller does not get extra permissions. MCP uses the same organization access, ledger access, and accounting checks as other clients.

Example: If a member cannot edit Ledger A through the API, the same member cannot edit it through MCP.

### 2. Verify identity and operation scope

The backend must identify the caller and check the organization and ledger needed for the operation. Client-supplied IDs identify targets; they do not prove permission.

Accounting operations need a clear target ledger. The backend checks its organization, the caller's current membership, and the permission for the action. Organization-level operations check organization access.

Not every request needs an existing organization and ledger. Registration and organization creation must follow their own identity checks. They cannot require a ledger that does not exist yet.

Example: Replacing a ledger ID in a request must not expose another organization's records.

The API contract will specify which context comes from authentication and which comes from request fields.

### 3. Use consistent requests, responses, and errors

Use a common approach to input, successful results, and errors. Callers must be able to distinguish success, invalid business data, denied access, and system failure. A failed query must not look like an empty result or a zero balance.

The contract must explain:

- Money: Currency, decimal precision, rounding, and how amounts are sent.
- Dates: Accounting dates versus exact timestamps, and the applicable time zone.
- Missing values: The difference between an omitted field, null, an empty list, and zero.

Example: A balance of zero is a valid result. A permission error is not a balance of zero.

No specific decimal scale, JSON field type, date format, or default value is approved by this principle.

### 4. Make query behavior consistent

Similar queries use consistent search, filter, sort, and pagination rules. The contract must describe available filters, how filters combine, the default order, and how pages work.

Results must have an explainable order. Different clients must not change the meaning of the same query.

For balances and transactions, pagination changes the rows returned, not the definition of the total for the full query.

Example: A query matches 100 transactions. Showing 20 per page must not make the full-query total equal only the first 20 transactions.

The pagination method, page size, and parameter names remain open.

### 5. Handle retries, concurrent edits, and changes safely

Repeating the same business operation must not accidentally create duplicate records. A timeout does not prove that the original request failed. The retry mechanism must handle that uncertainty.

If two people edit the same record, the later update must not silently overwrite the earlier change. Return a clear conflict so the caller can refresh or resolve it.

API changes need a compatibility and migration plan when they change field meanings, required input, or response formats. MCP tool changes follow the same business compatibility requirements.

Example: Retrying a successful create request after a lost response must not create a second copy of the same business operation.

## Proposed

The [Phase 1 technical contract](design/phase-1-technical-contract.md) already proposes paths, fields, schemas, and mechanisms for the initial backend. Those details remain proposals. Review each module contract for inputs, outputs, permissions, validation, errors, retry behavior, and examples.

The MCP tool list should map each tool to an approved backend operation. Tool names and parameters should follow those contracts rather than define new business rules.

Idempotency keys and record versions are possible ways to handle retries and conflicting edits. They are proposals, not selected mechanisms. An idempotency key identifies a retry of one operation. A record version helps detect an edit based on an old copy.

## Open Questions

- Which authentication service and login flow will be used?
- What are the API paths, field names, error codes, and response shapes?
- How will money, dates, time zones, and missing fields be represented?
- How will pagination behave when records change between page requests?
- How will duplicate operations and conflicting edits be detected?
- How will API and MCP versions remain compatible?
- Which MCP tools will be available first?

The five principles and shared backend are already confirmed. C#, Entity Framework Core, and PostgreSQL are the confirmed technical direction. These questions concern the proposed contracts and implementation, not whether to adopt that direction. Technical reviewers should resolve the detailed choices; the business owner does not need to answer each one individually.

No MCP service, connection, credential, or actual permission is created by this document. Exact database schemas, provider choices, and deployment details remain proposed or open.

[Testing and acceptance](08-testing-acceptance.md) · [Back to documentation](README.md)
