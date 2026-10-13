# Tasks

## 1. Core failure contract

- [x] 1.1 Add finite categories/codes, immutable coded diagnostics, and typed exceptions; verify constructor/serialization contract tests distinguish every catalog category without message parsing.
- [x] 1.2 Add JSON Pointer path construction with root/escaping support and bounded diagnostic collections; verify nested all/any/not, selection/sort/key, and escaped-segment golden paths.
- [x] 1.3 Document the code catalog and breaking exception/path migration; verify documented names match contract tests and messages are explicitly non-contractual.

## 2. Library failure surfaces

- [x] 2.1 Migrate validator/expression/literal/schema errors and input roots, replacing flattened/message-parsed detection; verify malformed policy and client filters return precise codes/paths and conditional field inputs are detected structurally.
- [x] 2.2 Migrate compiler/read/context/token and SQLite capture/publication/rollback failures; verify denied fields, absent policy, incompatible schema, revision conflicts, invalid cursors, and export lifecycle failures remain distinct and fail closed.
- [x] 2.3 Migrate explicit known provider/projection/seek/snapshot checks without wrapping arbitrary infrastructure exceptions as denial; verify unexpected database errors retain causes and no fallback occurs.
- [x] 2.4 Update read/lifecycle/provider diagnostic documentation with those changes; verify inaccessible/absent detail equivalence and sentinel privacy tests across public diagnostics.

## 3. Optional HTTP package and example

- [x] 3.1 Add Kkts.DataAccess.AspNetCore and safe Problem Details handling with the design's status/code catalog; verify known errors, malformed JSON, caller cancellation, and generic unexpected 500 responses without internal values.
- [x] 3.2 Replace overlapping EmployeeApp handlers and migrate designer/HTTP consumers to structured diagnostics; verify pre-start responses and post-start NDJSON termination without appended error rows.
- [x] 3.3 Document package registration, response shapes, and streaming limits; verify example acceptance and sample registration compile with core still free of ASP.NET dependencies.

## 4. Integration

- [x] 4.1 Run build, focused contract/validator/read/store/export/HTTP tests, and package packing; verify existing authorization/provider behavior and strict OpenSpec validation remain passing.
