> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [QUICKSTART.md](../../../QUICKSTART.md)
> Reason: .NET 8 migration planning is no longer active and baseline requirements now live in canonical setup and status documents.
> Deprecation date: 2026-03-17

# .NET Modernization Project

## Issue

The Backend CI workflow is failing due to .NET 6.0 being End of Life (EOL) and End of Support (EOS).

### Current Status

- Current Framework: .NET 6.0
- EOL Date: November 12, 2024
- Security Status: No longer receiving security updates
- CI Status: Failing with warnings about EOL framework

## Recommended Solution

### Target Framework: .NET 8.0 LTS

- Status: Long Term Support (LTS)
- Support End Date: November 10, 2026

### Migration Path

1. Assess dependency and compatibility impact.
2. Update project target frameworks and CI toolchains.
3. Execute full test and integration validation.
4. Update contributor and operator documentation.

### Success Criteria

1. All projects build successfully on .NET 8.
2. Unit and integration tests pass.
3. CI pipelines are green.
4. Documentation reflects current runtime baseline.
