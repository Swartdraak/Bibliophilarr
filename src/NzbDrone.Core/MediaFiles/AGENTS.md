# MediaFiles Directory Contract

## Purpose

This subtree owns media/file representation and important parts of Bibliophilarr's file lifecycle.

File lifecycle correctness and ebook/audiobook classification are protected product invariants.

## Must preserve

- supported ebook extensions;
- supported audiobook extensions;
- intended case-insensitive classification;
- correct quality/media-type association;
- no ebook/audiobook cross-association;
- safe file discovery;
- path correctness;
- existing file tracking semantics.

## Change discipline

Do not add an extension or classification rule without tests demonstrating intended format, case behavior where relevant, no collision with the opposite media class, and import impact.

Avoid broad file-movement changes from this subtree without `import-file-lifecycle-engineer` and architectural review.

## Validation

Use targeted file/media tests plus `qa-library-workflow-validator` for behavior-changing work. For R3 changes, validate synthetic ebook+audiobook fixtures in the disposable stack.

Normal PR target is `develop`.
