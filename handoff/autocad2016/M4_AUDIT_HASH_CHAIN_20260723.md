# M4: AgentHost Audit Hash Chain

Last updated: 2026-07-23 (China Standard Time)

## Scope

`AgentHostAuditLog` now writes the production `bootstrap-serve` audit file using
`codex.autocad.agenthost.audit/2`. This is a real extension of the existing session audit path,
not an unused interface or a second log. It does not start or control AutoCAD, and it does not
enable CAD writes.

The per-session file remains at:

```text
%LOCALAPPDATA%\OpenAI\CodexForAutoCAD\audit\agenthost\<bootstrap-session-id>.jsonl
```

The existing private ACL, `CreateNew`, bounded retention, write-through and fail-closed behavior
remain unchanged.

## Chain Contract

Each line is canonical compact UTF-8 JSON. Required fields are ordered as follows, with nullable
business fields omitted in their listed position:

```text
schema, sequence, timestampUtc, sessionId, eventType,
systemConversationId, systemRequestId, bridgeRequestId,
providerThreadId, providerTurnId, method, approvalKind,
resolution, outcomeCode, errorCode, previousRecordHash, recordHash
```

- `previousRecordHash` on record 1 is exactly 64 lowercase `0` characters.
- Each later `previousRecordHash` equals the immediately preceding `recordHash`.
- `recordHash` is lowercase SHA-256 over the canonical record excluding `recordHash` itself.
- A verification pass also requires strict UTF-8, the exact schema, exact field names, no duplicate
  fields, canonical byte layout, one consistent session, monotonic sequence, and a final
  `session_stopped` or `session_failed` record.
- The verifier is bounded by the caller-provided record/byte limits and rejects oversized input
  before parsing it into an unbounded structure.

The verifier is internal to AgentHost and is exposed to the Bridge Specs through the existing
`InternalsVisibleTo` test boundary. There is no user-facing verification command yet.

## What This Proves

The automated contract proves that a produced chain validates and that the verifier rejects:

- a changed business field without a matching hash;
- a changed sequence;
- a removed middle record;
- a wrong predecessor hash; and
- a missing terminal record.

The existing audit specs continue to prove content omission, stable error codes, cancellation and
approval correlation, capacity fail-closed behavior, private storage and normal Bridge operation.

## Explicit Security Boundary

This is a hash chain, not signed or externally immutable audit storage. A party that can replace
the whole local file and recompute every SHA-256 value can produce another self-consistent chain.
Private ACLs reduce ordinary local access but do not change that property. No credential, token,
prompt, CAD canonical JSON, drawing path, exception text, environment variable, command text or
raw provider payload was added to the audit schema.

Future work must choose and validate an appropriate protected anchor, signature or append-only
storage mechanism before claiming resistance to a privileged local writer. CAD approval-resolution
and write terminal events are also still outside the current read-only scope.

## Evidence

See `evidence/m4-agenthost-audit-hash-chain-20260723.json` for the exact build and test result.
The prior `/1` baseline evidence is retained as a historical record in
`evidence/m4-agenthost-runtime-audit-20260723.json`; it is not evidence for the `/2` chain.
