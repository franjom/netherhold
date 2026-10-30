# Save loading hardening (ADR-0588, first part)

Scope: implement the supplied task specification; keep snapshot writes, version 336,
Hash64 and the determinism pin unchanged. Work on codex/harden-save-loading.

1. Add regression tests for truncation and negative counts and verify they fail.
2. Add Core save reason codes, an IOException-derived exception, a bounded binary
   reader and a separate payload checksum (exception-base decision: ADR-0588).
   Update exact exception assertions.
3. Bound every snapshot/nested count by remaining encoded bytes and existing
   collection limits. Validate dimensions before arithmetic and bound party names.
   Wrap malformed-input exceptions at Read with reason, position and inner error.
4. Upgrade only the session envelope to v2; checksum snapshot and profile before
   parsing the profile. Normalize header/text/length/profile errors.
5. Add focused corruption and sampled fuzz tests, including nested navigation,
   malformed UTF-8, duplicate keys and non-seekable streams. Verify the harness.
6. Run Release solution build/tests and Unity compile; inspect unchanged pin/writer.
7. Run reviewer, resolve objections, run documenter, read its edits, run docs/kb
   gates and commit named files with Assisted-by trailer. Keep the worktree.

Review focus: byte-derived allocation sizes; restoration through duplicate ids;
7-bit string prefixes; mesh index/capacity multiplication; header-only listings.

Verification: steps 1-6 complete; reviewer reports no remaining findings after
non-seekable header and hand-capacity regressions were fixed. Release build and
Unity adapter build: zero warnings/errors. Solution tests: 7,160 passed, 30 skipped,
0 failed. Documentation pass: docs gate clean; kb check: 2,670 records, no findings.
Remaining ADR scope: see ADR-0588.
