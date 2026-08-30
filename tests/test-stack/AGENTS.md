# Disposable Test Stack Directory Contract

## Purpose

This subtree owns deterministic, disposable running-environment support for Bibliophilarr behavioral validation.

## Safety invariants

Never mount production Bibliophilarr config, mount a real ebook/audiobook library for destructive testing, connect to a real download client by default, reuse production databases, clean directories outside the active test run, or publish generated test images as production releases.

Runtime state belongs under `.test-env/<run-id>`.

## Expected capabilities

The stack may provide a candidate Bibliophilarr container, isolated config, synthetic ebook/audio fixtures, deterministic metadata/indexer replay, disposable download client, and evidence/log capture.

## Lifecycle

Prefer:

```bash
./tests/test-stack/test-env.sh prepare
./tests/test-stack/test-env.sh up
./tests/test-stack/test-env.sh status
```

Capture evidence before reset/down/cleanup.

## Change ownership

Compose/fixture definitions -> `test-infrastructure-engineer`.

Runtime lifecycle/config/evidence -> `test-environment-operator`.

Normal PR target is `develop`.
