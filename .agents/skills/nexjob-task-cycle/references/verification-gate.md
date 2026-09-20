# Automated Verification Gate Reference

This reference describes the verification commands, common linting errors, and remediation patterns for the NexJob codebase.

---

## Verification Pipeline

Always execute in this exact order:

```bash
# Step 1: Format & StyleCop Check
dotnet format --verify-no-changes

# Step 2: Release Build with Zero Warnings (TreatWarningsAsErrors = true)
dotnet build -c Release

# Step 3: Run Unit Tests
dotnet test --no-build
```

---

## Common StyleCop Rules in NexJob

NexJob enforces strict StyleCop analyzers. Common rules and their fixes:

### 1. SA1202: Elements should be ordered by access
* **Issue:** A `private` or `protected` member appears before a `public` or `internal` member.
* **Fix:** Order members by accessibility: `public` -> `internal` -> `protected` -> `private`.

### 2. SA1204: Static elements should appear before non-static elements
* **Issue:** An instance member is placed above a static member with the same accessibility.
* **Fix:** Group `static` members at the top of each accessibility group.

### 3. SA1413: Use trailing comma in multi-line initializers
* **Issue:** An array, collection, or object initializer spanning multiple lines lacks a trailing comma on the last element.
* **Fix:** Ensure the last item ends with a comma `,`.

### 4. SA1508: Closing brace should not be preceded by blank line
* **Issue:** A blank line immediately precedes a `}` closing brace.
* **Fix:** Remove the empty line before the closing brace.

---

## Code Quality Standards Checklist

Before running the gate, double-check:

- [ ] All new concrete classes are marked `sealed`.
- [ ] Every asynchronous call in `src/NexJob*` uses `.ConfigureAwait(false)`.
- [ ] `CancellationToken` is accepted and forwarded to all inner async calls.
- [ ] No occurrences of `DateTime.Now` (use `DateTime.UtcNow`).
- [ ] No occurrences of `.Result` or `.Wait()`.
- [ ] All public types, methods, properties, and constructors have complete XML documentation (`///`).
- [ ] String comparisons explicitly specify `StringComparison.Ordinal` or `StringComparison.OrdinalIgnoreCase`.
