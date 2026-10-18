# Contributing to Serein

Ideas and feedback are welcome, including from people who do not write code.
English, Russian and German reports are welcome.

## Suggest a change or report a bug

Open an issue and explain the task you want to make easier, or the problem you
found. Include the Serein/Windows versions, steps, expected behavior and actual
result. Screenshots help. Remove usernames, personal paths, serial numbers and
private logs before posting. Review diagnostic files before uploading them.

## Code and translations

1. Fork the repository and make a branch.
2. Build using the README command.
3. Run relevant tests and explain what you checked in your pull request.
4. Keep the change focused and describe the user-visible result in plain language.

App text is in `src/Serein.Core/Translations/en.json`, `ru.json` and `de.json`.
English is the default. Keep keys and placeholders such as `{0}` unchanged.
For a new language, add a full catalog, update `UiStrings`/the language picker
and check native layout. Hardware names, paths and Windows messages are not translated.

Device-recognition rules need official evidence and false-positive tests.
A support page alone does not establish driver compatibility.

Use mocks and test-owned apps. Automated tests must not install drivers, stop
real services, suspend unrelated desktop apps or change security settings.
See `docs/ARCHITECTURE_AND_RECOVERY.md` for recovery constraints.

Contributions use GPL-3.0-only. Dependencies retain their own licenses.
Do not add proprietary SDK binaries or driver packages.
