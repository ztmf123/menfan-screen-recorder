# Contributing

Use Windows 10/11 x64 and .NET 8 SDK. Run `scripts/setup.ps1`, then
`scripts/test.ps1` before submitting changes. Keep raw capture separate from
non-destructive edits, pass process arguments using `ArgumentList`, and preserve
the original media on errors and cancellations.

Update `docs/FEATURE_PARITY.md` when implementing a previously missing feature.
Report Windows version, display DPI, monitor layout, audio devices, and short
reproduction steps for capture problems. Do not attach private recordings or
unredacted logs to public issues.
