# tests Directory Contract

## Purpose

`tests/` contains cross-cutting deterministic fixtures, profiles, test-stack assets, integration helpers, and test evidence infrastructure not better owned inside a specific source test project.

## Rules

Tests must be deterministic where release-blocking, isolated, safe to rerun, explicit about fixtures/expected behavior, and incapable of damaging real libraries/config by default.

Do not change expected results solely to accommodate an incorrect implementation.

Generated evidence/runtime output must not become source fixtures accidentally.

## Fixture policy

Use synthetic or sanitized fixtures.

Do not commit personal media, real user databases, secrets/API tokens, or copyrighted full media inappropriate for repository distribution.

## Test stack

`tests/test-stack/` is governed by its nested `AGENTS.md`.

## Branching

Test infrastructure work normally branches from and PRs to `develop`.

## Preferred agents

`test-infrastructure-engineer`, applicable QA validators, and `test-environment-operator` for runtime lifecycle.
