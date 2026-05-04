# Security Policy

## Supported Version

Security reports are accepted for the current `main` branch and for the current
public plugin/runtime surface shipped from this repository.

## Reporting a Vulnerability

Please report security issues privately by email:

- `vital.cc55@gmail.com`

When possible, include:

- the affected branch or commit;
- the Windows version and environment details;
- the target app class involved (`Win32`, `WPF`, `Qt`, `Electron`, browser,
  custom UI, and so on);
- reproduction steps;
- expected impact;
- whether the issue involves screenshot data, approval boundaries, physical
  input, or privilege boundaries.

Please do **not** open a public issue for an unpatched security problem.

## Scope Notes

Relevant reports may include issues involving:

- unintended desktop control outside the expected target boundary;
- approval or confirmation bypasses;
- screenshot or artifact leakage;
- privilege or process-boundary mistakes;
- stale-state or identity-proof failures that can be turned into unsafe action
  execution.
