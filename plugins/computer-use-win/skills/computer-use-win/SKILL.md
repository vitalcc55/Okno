---
name: "computer-use-win"
description: "Use when an agent needs to operate Windows apps through the public Computer Use for Windows surface for the first time: discover apps, fetch screenshot-first state, choose the right action, interpret results, and stay inside the shipped safety/verification model."
---

# Computer Use for Windows

## Overview

This skill is not for developing `Okno`. It is for **quick onboarding of a new
agent** that is seeing the public `computer-use-win` plugin surface for the
first time.

The main working model is:

```text
list_apps -> get_app_state -> action -> verify
```

Important:

- work through the public `computer-use-win` surface, not through internal
  `windows.*` engine tools;
- treat the product as **screenshot-first and state-first**;
- do not confuse successful dispatch with semantic success;
- for low-confidence actions, use `observeAfter=true` or an explicit follow-up
  `get_app_state`.

## When to Use

- You need to operate Windows GUI through the `computer-use-win` plugin.
- You need to choose a window, fetch screenshot/state, and perform an action.
- You need to decide which tool to use: `click`, `set_value`, `type_text`,
  `press_key`, `scroll`, `perform_secondary_action`, or `drag`.
- You need to interpret `verify_needed`, `successorState`,
  `successorStateFailure`, `windowId`, `stateToken`, and other product-level
  fields.

## What Is Available Right Now

The public surface currently consists of 9 tools:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

`observeAfter=true` is currently supported by:

- `click`
- `press_key`
- `type_text`
- `scroll`
- `drag`

Semantic-only actions without `observeAfter`:

- `set_value`
- `perform_secondary_action`

## Where to Start

### 1. Find the app

Call `list_apps` first.

Use it to:

- find the relevant app group;
- choose the concrete window from `windows[]`;
- take `windowId` as the main public selector.

Do not treat `windowId` as an eternal identifier.
It is discovery-scoped, although repeated unchanged snapshots now try to reuse
it.

### 2. Get action-ready state

Then call `get_app_state`.

Look for:

- `session`
- `stateToken`
- `capture`
- `accessibilityTree`
- `warnings`
- screenshot image content

If the client does not show the screenshot inline, use `artifactPath` as an
operator/debug fallback, but `get_app_state` is still the source of truth for
action planning.

### 3. Choose the action

Use this ladder:

1. semantic action when proof is strong;
2. explicit physical path when UIA is weak but the target is well localized;
3. after a low-confidence action, get fresh state immediately through
   `observeAfter=true` or a separate `get_app_state`.

## How to Choose a Tool

### `click`

Use:

- `elementIndex` when a semantic target is available;
- `point` + `confirm=true` when you need a coordinate path.

Prefer `elementIndex`.
Coordinate click is a low-confidence path.

### `set_value`

This is the preferred semantic path for settable controls.

Use it when:

- the control really looks settable;
- the tree/affordance exposes a strong semantic signal;
- you want a value change, not a physical typing simulation.

Do not replace `set_value` with `type_text` if the semantic path looks
credible.

### `type_text`

This tool is for text, but it has **multiple modes**.

#### Ordinary path

Use ordinary `type_text` when:

- there is focused editable proof;
- or there is a strong focused writable target.

#### Focused fallback

Use:

- `allowFocusedFallback=true`
- `confirm=true`

only when UIA proof is weak but the runtime can still honestly prove a
target-local focus boundary for a text-entry-like target.

This is not generic typing into any focused window.

#### Coordinate-confirmed fallback

Use an explicit `point` only for poor-UIA / top-level-focus paths when there
is no other honest text-entry proof.

Form:

```json
{
  "stateToken": "<latest token>",
  "point": { "x": 386, "y": 805 },
  "coordinateSpace": "capture_pixels",
  "text": "Test MCP",
  "allowFocusedFallback": true,
  "confirm": true,
  "observeAfter": true
}
```

Rules:

- take `point` from the **latest** screenshot/app state;
- the coordinate space for this branch is `capture_pixels`;
- do not use `screen` for this branch;
- this is a dispatch-only path and usually returns `verify_needed`;
- there is no hidden clipboard/paste path here.

### `press_key`

Use it for:

- `Enter`
- `Tab`
- navigation keys
- modifier combos

Do not use it for arbitrary printable text when you really need text
insertion.

### `scroll`

Use:

- a semantic scroll path when the target is semantically clear;
- `point` + `confirm=true` when you need a coordinate wheel path.

If you immediately need a new visual state, add `observeAfter=true`.

### `perform_secondary_action`

This is a semantic secondary action.

Do not think of it as a generic right-click alias.

If the target is poor-UIA and you specifically need a physical context-menu
style path, expect that this tool may not be the right one.

### `drag`

Treat `drag` as a low-confidence physical action.

Good practice:

- do it only on fresh state;
- after it, use `observeAfter=true` or a separate `get_app_state`.

## How to Read Results

### `done`

A stronger result, but not the common default for coordinate or poor-UIA
paths.

### `verify_needed`

This is a **normal** result for low-confidence actions.

It does not mean "the tool is broken."

It means:

- dispatch happened;
- but semantic outcome was not automatically proven.

If there is also `successorState`, then:

- fresh state has already been fetched;
- another immediate `get_app_state` may not be necessary;
- but the top-level action still does not pretend to be semantic `done`.

### `failed`

Usually means:

- stale state;
- missing target;
- blocked / integrity / foreground problem;
- invalid request;
- weak proof that the runtime honestly could not lift into an action-ready
  path.

### `approval_required`

Do not bypass this with a "clever" fallback path.
This is a separate product gate.

### `blocked`

Do not automate blocked targets.
