# LibTab for .NET

Pure C# implementation of `libtab`, the Plan 9 `ndb`-shaped table format.

`LibTab` reads and writes the same text table files as the C library while
keeping the crypto wire format compatible for `HASHED`, `SIGNED`, and
`ENCRYPTED` cells. It has no native runtime dependency; cryptographic
primitives use Bouncy Castle plus managed compatibility code where libtab's
Monocypher wire format requires it.

## Install

```sh
dotnet add package LibTab --version 0.1.0
```

## Format

A table starts with a schema entry, followed by rows made of `attr=value`
tuples:

```text
schema=users
	col=name
	col=uid
	col=password type=HASHED

name=alice
	uid=1000
	password=hashed:Aa5Kr7PH2GFrc58Fow0GtOIjb96nEhaBWfNG6KPv1X2e
```

Plain cells preserve libtab's ndb text semantics: semantic nil and the literal
string `nil` are distinct, unsafe ndb text is entity-escaped, and legacy
`__libtab_text64_v1:` cells remain readable.

## Basic Use

```csharp
using System.Text;
using LibTab;

var columns = new[]
{
    new TabColumn("name"),
    new TabColumn("uid"),
    new TabColumn("password", "HASHED"),
};

TabTable table = TabTable.Create("users.tab", "users", columns);
TabRow alice = table.AddRow("name", "alice");
table.Set(alice, "uid", "1000");
table.SetHashed(alice, "password", Encoding.UTF8.GetBytes("secret"));
table.Commit();

TabTable reopened = TabTable.Open("users.tab");
TabRow found = reopened.Search("name", "alice").Single();
bool passwordMatches = reopened.VerifyHash(found, "password", Encoding.UTF8.GetBytes("secret"));
```

## Signed Cells

```csharp
using System.Collections.Generic;
using System.Text;
using LibTab;

TabKeyPair keyPair = TabKeyPair.Generate();
var columns = new[]
{
    new TabColumn("id"),
    new TabColumn("payload", "SIGNED", new Dictionary<string, string> { ["signer"] = "local" }),
};

TabTable table = TabTable.Create("audit.tab", "audit", columns);
TabRow row = table.AddRow("id", "event-1");
table.SetSigned(row, "payload", Encoding.UTF8.GetBytes("created"), keyPair.SecretKey);
table.Commit();

byte[] body = table.VerifySigned(row, "payload", keyPair.PublicKey);
```

## Encrypted Cells

```csharp
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using LibTab;

byte[] key = RandomNumberGenerator.GetBytes(TabTable.EncryptedKeyLength);
var columns = new[]
{
    new TabColumn("id"),
    new TabColumn("secret", "ENCRYPTED", new Dictionary<string, string> { ["key"] = "local" }),
};

TabTable table = TabTable.Create("secrets.tab", "secrets", columns);
TabRow row = table.AddRow("id", "entry-1");
table.SetEncrypted(row, "secret", Encoding.UTF8.GetBytes("hunter2"), key);
table.Commit();

byte[] plaintext = table.Decrypt(row, "secret", key);
```

`ENCRYPTED` cells use the C library's wire format:
`encrypted:<base64url(ver | nonce[24] | mac[16] | ciphertext)>`. The cipher is
Monocypher-compatible XChaCha20-Poly1305, not AES-GCM. Keys are raw 32-byte
values supplied by the caller; schema `key=<name>` attributes are labels only.

## Compatibility

The regular gate compiles a temporary C harness against the local C `libtab`
archive, creates equivalent C and C# tables, byte-compares deterministic output,
and cross-verifies files in both directions. Argon2id and ENCRYPTED cells are
checked by verification instead of byte comparison because correct
implementations generate fresh salts and nonces.

Current implemented cell types:

- plain ndb text
- `HASHED` with BLAKE2b and Argon2id verification
- `SIGNED` with Monocypher-compatible EdDSA
- `ENCRYPTED` with Monocypher-compatible XChaCha20-Poly1305

`TabTable.MaxCellLineBytes` exposes the C library's `TabMaxCell` value. Plain
setters, row heads, streaming writer cells, `SIGNED`, and `ENCRYPTED` refuse
values whose emitted ndb line would exceed that encoded limit; `HASHED`
preimages remain unbounded because the stored digest is fixed-size.

## Build And Test

```sh
dotnet restore LibTab.slnx
scripts/test.sh
scripts/full-gates.sh
```

The full gate requires plan9port tools and a checkout of the C libtab library,
named by `LIBTAB_C_REPO`:

```sh
LIBTAB_C_REPO=/path/to/libtab scripts/full-gates.sh
```
