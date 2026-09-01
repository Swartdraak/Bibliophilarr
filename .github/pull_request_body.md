## Summary

This migration updates the legacy Ical.Net 4.x calendar dependency to the supported stable 5.2.x series and adjusts the calendar feed to the current API contract.

## Why

- Historical deferred migration: #59
- Recovery epic: #93
- Current stable upstream target: Ical.Net 5.2.3
- Legacy v4 usage depended on removed writable properties (`HasTime`, `IsAllDay`) and outdated calendar semantics.

## Changes

- bump `Ical.Net` to `5.2.3`
- update `CalendarFeedController` for the 5.x `CalDateTime` constructor pattern
- preserve all-day calendar semantics for release dates
- add a dedicated `CalendarFeedControllerFixture` round-trip regression covering `DTSTART`, `DTEND`, summary, description, and parsed all-day semantics
- preserve UTC/date-boundary behavior and remove the duplicate calendar regression from the generic event-guard fixture

## Validation

Candidate SHA: `da689f71bd3bcffa0c2a0efe2786c8bbb31fc8b0`

- `dotnet restore Bibliophilarr.sln --nologo`
- `dotnet build Bibliophilarr.sln --nologo --no-restore`
- `dotnet test NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --nologo --no-restore`

### Evidence

- Ical.Net: `5.2.3`
- Full solution restore: PASS
- Full solution build: PASS
- Full API test project: PASS (52 passed, 0 failed)
- Focused: `CalendarFeedControllerFixture` and `BookControllerEventGuardFixture`: PASS
- Calendar round-trip: PASS
- `DTSTART`: PASS
- `DTEND`: PASS
- Parsed all-day semantics: PASS
- UTC date-boundary preservation: PASS
- Duplicate migration ledger: ABSENT
- Duplicate calendar test: REMOVED

## Rollback

- revert the package version pin and restore the earlier `CalendarFeedController` implementation in the candidate branch

## References

- #59
- #93
- #95
- NuGet: Ical.Net 5.2.3
