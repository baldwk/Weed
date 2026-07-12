# Query Routing and Hotkeys

> [Back to Developer Documentation](README.md)

## Activation Paths

Weed supports three activation types:

```text
Keyword
Hotkey
ImplicitQuery
```

The router considers only enabled plugins and passes a cancellation token through every asynchronous query or command dispatch.

## QueryContext

`QueryContext` identifies the raw and normalized input, activation type, matched keyword or provider, and any plugin-specific routing information.

Normalization happens before routing and covers trimming, whitespace, case-insensitive matching, and common full-width input forms. Plugins still receive the original text for display or exact parsing.

## Keyword Routing

Given:

```text
clip project plan
```

the router:

1. Matches the first token against enabled keyword activations.
2. Selects the longest valid keyword when multiple entries overlap.
3. Removes the keyword and leading whitespace.
4. Sends `project plan` to the matching provider.

Keyword matching is case-insensitive. Plugins should not reparse the keyword unless they intentionally expose multiple aliases with different behavior.

When only a keyword is present, the plugin receives an empty query and may return recent items, common actions, or a help result.

## Hotkey Routing

The Host registers global hotkeys after plugin initialization and user settings are loaded.

```text
Physical key press
  -> Windows hotkey service
  -> Activation command lookup
  -> Show launcher with an initial query, or execute the command directly
```

The main launcher hotkey is application-level. Plugin hotkeys are keyed by `<pluginId>:<command>` and stored in `hotkeys.json`.

Registration failures must be logged and surfaced in Settings. One failed plugin hotkey must not prevent other hotkeys from registering.

## ImplicitQuery Routing

Every enabled implicit provider can inspect unprefixed input. Current providers include App Launcher, Calculator, and Run Command.

Providers should return no result when the input is clearly irrelevant:

- Calculator accepts only expression-like input.
- Run Command searches only its allowlist.
- App Launcher avoids low-confidence results for empty or insignificant input.

Implicit queries are where plugin priority and cross-plugin scoring matter most.

## Ranking

The final score combines three concepts:

```text
FinalScore = PluginMatchScore + UsageScore + PriorityScore
```

Exact numeric weights are implementation details and may evolve. The invariants are more important:

- A strong exact match should outrank a loose frequent match.
- Usage history may refine close results but should not dominate relevance.
- User priority affects only implicit competition.
- Stable tie-breakers keep result order predictable.

### PluginMatchScore

The plugin owns semantic relevance. Suggested ordering:

```text
Exact match
Prefix match
Token or acronym match
Substring match
Subsequence or fuzzy match
Fallback result
```

Scores should be internally consistent and leave enough space for the Host to apply usage and priority adjustments.

### UsageScore

Usage history is keyed by plugin, result, and command. It uses selection count and recency with bounded influence so repeated use cannot permanently override a better exact match.

### PriorityScore

Users set plugin priority from `0` to `100`. Priority applies only to ImplicitQuery results and should act as a preference, not as an unconditional override.

### Tie-Breakers

Use deterministic secondary ordering such as:

1. Final score descending.
2. Plugin match score descending.
3. Stable plugin registration order.
4. Result ID or title ordinal order.

## Cancellation and Result Updates

- Every input change cancels the previous query token.
- Plugins should check cancellation before and after expensive I/O.
- Results from a cancelled query must never replace results for newer input.
- Exceptions are isolated per plugin and logged; other providers should still return results.
- The UI shows only a bounded initial result set and may load more as selection approaches the end.

## Hotkey Manifest

```json
{
  "type": "hotkey",
  "command": "screenshot.region",
  "defaultKeys": "Shift+Alt+A",
  "configurable": true,
  "behavior": "executeCommand"
}
```

Supported behavior concepts:

- Open the launcher with a plugin query.
- Execute a command without first showing results.
- Reopen the launcher with a query after a command completes.

## User Overrides

```json
{
  "weed.screenshot:screenshot.region": {
    "keys": "Shift+Alt+A",
    "enabled": true
  }
}
```

Manifest defaults seed missing values. Existing user overrides remain authoritative across plugin updates.

## Hotkey Text Format

Canonical modifier order:

```text
Ctrl+Shift+Alt+Win+Key
```

Examples:

```text
Alt+Space
Ctrl+Shift+C
Shift+Alt+A
Win+Shift+S
```

The parser normalizes `Control` to `Ctrl`, title-cases named keys, and uppercases single-character keys. A shortcut must contain one non-modifier key.

## Validation Checklist

- Keyword aliases do not overlap unexpectedly.
- Empty keyword queries return intentional content.
- Implicit providers reject irrelevant input quickly.
- Cancelling and immediately typing a new query never displays stale results.
- Exact matches remain above looser frequently used results.
- Hotkey conflicts are visible and do not disable unrelated shortcuts.
- User overrides survive plugin and Host updates.
