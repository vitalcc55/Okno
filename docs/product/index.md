# Okno Product Docs

This directory is the product source of truth for `Okno`.

## Source of truth

- [okno-vision.md](okno-vision.md)
- [okno-spec.md](okno-spec.md)
- [okno-roadmap.md](okno-roadmap.md)
- [../generated/computer-use-win-interfaces.md](../generated/computer-use-win-interfaces.md)

## How to use these docs

- `okno-vision.md` — product direction and long-term framing
- `okno-spec.md` — current product contract
- `okno-roadmap.md` — delivery order and current capability map

## Transport policy

- The current product-ready target is a local `STDIO` MCP process.
- HTTP / URL transport is not part of the current delivery scope.
- A future HTTP mode, if it is ever added, should be designed as a separate
  stage after the local `STDIO` story is stable.

## OpenAI interop note

- `shell`, `skills`, `MCP`, and `computer use` are treated as adjacent layers,
  not as the same feature under different names.
- For the current product, the public Codex path is `computer-use-win`, while
  `Okno` remains the internal Windows-native runtime/engine.
- The current local integration path for Codex goes through the
  `computer-use-win` plugin/MCP surface on top of this repository.
- Built-in OpenAI `computer use` remains a compatibility track, not a change to
  the near-term product roadmap.
- The source of truth for this topic lives in
  [../architecture/openai-computer-use-interop.md](../architecture/openai-computer-use-interop.md)
  and is complemented by [okno-roadmap.md](okno-roadmap.md).

## Codename note

Some internal projects, namespaces, and paths still use the name `WinBridge`.
That is an intentional internal codename and not the product-facing source of
truth.
