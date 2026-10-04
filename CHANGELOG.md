# Changelog

## 0.1.0

- Initial pure C# libtab package.
- Supports ndb-shaped table parsing, serialization, mutation, search, commit,
  and streaming writes.
- Supports compatible plain text, `HASHED`, `SIGNED`, and `ENCRYPTED` cells.
- Enforces the C library's `TabMaxCell` encoded-line write guard on plain,
  `SIGNED`, and `ENCRYPTED` cells.
- Adds differential C libtab compatibility checks, checked-in corpus fixtures,
  coverage, CRAP, mutation, fuzz, and behaviour-spec gates.
