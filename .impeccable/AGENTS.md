# WebUI design review configuration

`config.json` records reviewed exceptions to the WebUI design checks. Keep each
exception scoped to one rule and the smallest applicable value and file set.
Document the existing design purpose and review evidence in its reason.

Keep generated reports and tool caches outside this directory. Use the design
check on any changed path and verify the underlying UI behavior when it changes.
