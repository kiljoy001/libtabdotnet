#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
libtab_c_repo="${LIBTAB_C_REPO:-/home/scott/Repo/libtab}"
tmp="${TMPDIR:-/tmp}/libtab-equivalence.$$"

mkdir -p "$tmp/cs"
trap 'rm -rf "$tmp"' EXIT

require_tool() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "missing required tool: $1" >&2
    exit 1
  fi
}

run_case() {
  local case_name="$1"
  local c_tab="$tmp/${case_name}.c.tab"
  local cs_tab="$tmp/${case_name}.cs.tab"

  "$tmp/c_equiv" write "$case_name" "$c_tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- write "$case_name" "$cs_tab"

  if ! cmp -s "$c_tab" "$cs_tab"; then
    echo "C and C# generated libtab files differ for case '$case_name'" >&2
    diff -u "$c_tab" "$cs_tab" >&2 || true
    exit 1
  fi

  "$tmp/c_equiv" verify "$case_name" "$cs_tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- verify "$case_name" "$c_tab"

  sha256sum "$c_tab" | awk -v name="$case_name" '{print "equivalence[" name "]: " $1}'
}

run_argon2id_case() {
  local c_tab="$tmp/argon2id.c.tab"
  local cs_tab="$tmp/argon2id.cs.tab"

  "$tmp/c_equiv" write argon2id "$c_tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- write argon2id "$cs_tab"
  "$tmp/c_equiv" verify argon2id "$cs_tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- verify argon2id "$c_tab"

  echo "interop[argon2id]: verified C-generated and C#-generated salted hashes"
}

run_encrypted_case() {
  local c_tab="$tmp/encrypted.c.tab"
  local cs_tab="$tmp/encrypted.cs.tab"

  "$tmp/c_equiv" write encrypted "$c_tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- write encrypted "$cs_tab"
  "$tmp/c_equiv" verify encrypted "$cs_tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- verify encrypted "$c_tab"

  echo "interop[encrypted]: verified C-generated and C#-generated XChaCha20-Poly1305 cells"
}

run_limit_case() {
  "$tmp/c_equiv" expect-limits "$tmp/limits.c.tab"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- expect-limits "$tmp/limits.cs.tab"

  echo "interop[cell-limits]: C and C# both enforce TabMaxCell write guards"
}

run_malformed_case() {
  local name="$1"
  local expectation="$2"
  local path="$tmp/malformed-$name.tab"

  printf '%b' "$3" > "$path"
  "$tmp/c_equiv" "$expectation" "$path"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- "$expectation" "$path"
  echo "malformed[$name]: $expectation"
}

run_decrypt_fail_case() {
  local name="$1"
  local path="$tmp/malformed-$name.tab"

  printf '%b' "$2" > "$path"
  "$tmp/c_equiv" expect-decrypt-fail "$path"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- expect-decrypt-fail "$path"
  echo "malformed[$name]: expect-decrypt-fail"
}

verify_versioned_fixture() {
  local case_name="$1"
  local fixture="$repo_root/LibTab.DifferentialTests/Corpus/${case_name}.tab"
  local c_tab="$tmp/versioned-${case_name}.c.tab"

  "$tmp/c_equiv" write "$case_name" "$c_tab"
  if ! cmp -s "$c_tab" "$fixture"; then
    echo "versioned fixture '$fixture' differs from fresh C libtab output" >&2
    diff -u "$c_tab" "$fixture" >&2 || true
    exit 1
  fi

  "$tmp/c_equiv" verify "$case_name" "$fixture"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- verify "$case_name" "$fixture"
  echo "fixture[$case_name]: matches fresh C generation"
}

verify_versioned_malformed() {
  local name="$1"
  local expectation="$2"
  local fixture="$repo_root/LibTab.DifferentialTests/Corpus/Malformed/${name}.tab"

  "$tmp/c_equiv" "$expectation" "$fixture"
  dotnet run --no-restore --project "$tmp/cs/LibTabEquiv.csproj" -- "$expectation" "$fixture"
  echo "fixture-malformed[$name]: $expectation"
}

require_tool 9c
require_tool 9l
require_tool dotnet
require_tool sha256sum

if [[ ! -d "$libtab_c_repo" ]]; then
  echo "missing C libtab repo: $libtab_c_repo" >&2
  exit 1
fi

if [[ ! -f "$libtab_c_repo/libtab.a" ]]; then
  require_tool mk
  (cd "$libtab_c_repo" && mk)
fi

cat > "$tmp/c_equiv.c" <<'C'
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "libtab.h"
#include "monocypher.h"

static const char *const edge_values[] = {
	"Divine Comedy",
	"The Number \"e\"",
	"line one\nline two",
	"left\tright",
	"nil",
	"AT&T",
	"#not-a-comment",
	"__libtab_text64_v1:not-user-visible",
	"",
	"caf\303\251",
	"plain-token",
	"semi;colon"
};

static int
fail(const char *what)
{
	fprintf(stderr, "%s: %s\n", what, tab_lasterror());
	return 1;
}

static int
fail_msg(const char *what)
{
	fprintf(stderr, "%s\n", what);
	return 1;
}

static int
set_plain(Tab *t, TabRow *r, const char *col, const char *value)
{
	if(tab_set(t, r, col, value) < 0)
		return fail("tab_set");
	return 0;
}

static int
add_plain_row(Tab *t, const char *key, const char *value)
{
	TabRow *r;

	r = tab_add_row(t, "k", key);
	if(r == NULL)
		return fail("tab_add_row");
	return set_plain(t, r, "v", value);
}

static void
init_seed(uint8_t seed[32])
{
	int i;

	for(i = 0; i < 32; i++)
		seed[i] = (uint8_t)i;
}

static void
init_key_pair(uint8_t secret_key[64], uint8_t public_key[32])
{
	uint8_t seed[32];

	init_seed(seed);
	crypto_eddsa_key_pair(secret_key, public_key, seed);
}

static char *
bigval(int n, char c)
{
	char *s;

	s = malloc(n + 1);
	if(s == NULL)
		return NULL;
	memset(s, c, n);
	s[n] = 0;
	return s;
}

static Tab *
create_plain_table(const char *path, int ncols)
{
	TabColSpec cols[3];

	memset(cols, 0, sizeof cols);
	cols[0].name = "k";
	cols[1].name = "v";
	cols[2].name = "w";
	remove(path);
	return tab_create(path, "test", cols, ncols);
}

static int
write_basic(const char *path)
{
	Tab *t;

	t = create_plain_table(path, 2);
	if(t == NULL)
		return fail("tab_create");
	if(add_plain_row(t, "alpha", "one") != 0)
		return 1;
	if(add_plain_row(t, "beta", "two") != 0)
		return 1;
	if(tab_commit(t) < 0)
		return fail("tab_commit");
	tab_close(t);
	return 0;
}

static int
write_text(const char *path)
{
	Tab *t;
	char key[32];
	int i;
	int count;

	t = create_plain_table(path, 2);
	if(t == NULL)
		return fail("tab_create");
	count = (int)(sizeof edge_values / sizeof edge_values[0]);
	for(i = 0; i < count; i++){
		snprintf(key, sizeof key, "text-%02d", i);
		if(add_plain_row(t, key, edge_values[i]) != 0)
			return 1;
	}
	if(tab_commit(t) < 0)
		return fail("tab_commit");
	tab_close(t);
	return 0;
}

static int
write_mixed(const char *path)
{
	TabColSpec cols[4];
	Tab *t;
	TabRow *r;
	uint8_t secret_key[64];
	uint8_t public_key[32];

	memset(cols, 0, sizeof cols);
	cols[0].name = "k";
	cols[1].name = "v";
	cols[2].name = "digest";
	cols[2].type = "HASHED";
	cols[3].name = "proof";
	cols[3].type = "SIGNED";
	cols[3].signer = "pinned";
	remove(path);
	t = tab_create(path, "test", cols, 4);
	if(t == NULL)
		return fail("tab_create");

	init_key_pair(secret_key, public_key);
	r = tab_add_row(t, "k", "plain");
	if(r == NULL)
		return fail("tab_add_row");
	if(set_plain(t, r, "v", "Divine Comedy") != 0)
		return 1;
	if(tab_set_hashed(t, r, "digest", (const unsigned char *)"secret", 6) < 0)
		return fail("tab_set_hashed");
	if(tab_set_signed(t, r, "proof", (const unsigned char *)"vector", 6, secret_key) < 0)
		return fail("tab_set_signed");

	if(add_plain_row(t, "quote", "The Number \"e\"") != 0)
		return 1;
	if(add_plain_row(t, "newline", "line one\nline two") != 0)
		return 1;
	if(add_plain_row(t, "literal-nil", "nil") != 0)
		return 1;
	if(add_plain_row(t, "amp", "AT&T") != 0)
		return 1;
	if(add_plain_row(t, "hash", "#not-a-comment") != 0)
		return 1;
	if(add_plain_row(t, "prefix", "__libtab_text64_v1:not-user-visible") != 0)
		return 1;
	if(add_plain_row(t, "empty", "") != 0)
		return 1;

	if(tab_commit(t) < 0)
		return fail("tab_commit");
	tab_close(t);
	return 0;
}

static int
write_writer(const char *path)
{
	TabColSpec cols[3];
	TabWriter *w;

	memset(cols, 0, sizeof cols);
	cols[0].name = "k";
	cols[1].name = "v";
	cols[2].name = "w";
	remove(path);
	w = tab_writer_create(path, "test", cols, 3);
	if(w == NULL)
		return fail("tab_writer_create");
	if(tab_writer_add_row(w, "k", "space") < 0)
		return fail("tab_writer_add_row");
	if(tab_writer_set(w, "v", "Divine Comedy") < 0)
		return fail("tab_writer_set");
	if(tab_writer_set(w, "w", "nil") < 0)
		return fail("tab_writer_set");
	if(tab_writer_add_row(w, "k", "quote") < 0)
		return fail("tab_writer_add_row");
	if(tab_writer_set(w, "v", "The Number \"e\"") < 0)
		return fail("tab_writer_set");
	if(tab_writer_set(w, "w", "line one\nline two") < 0)
		return fail("tab_writer_set");
	if(tab_writer_add_row(w, "k", "clear") < 0)
		return fail("tab_writer_add_row");
	if(tab_writer_set(w, "v", "gone") < 0)
		return fail("tab_writer_set");
	if(tab_writer_clear(w, "v") < 0)
		return fail("tab_writer_clear");
	if(tab_writer_add_row(w, "k", "space") < 0)
		return fail("tab_writer_add_row");
	if(tab_writer_set(w, "v", "Divine Comedy") < 0)
		return fail("tab_writer_set");
	if(tab_writer_set(w, "w", "nil") < 0)
		return fail("tab_writer_set");
	if(tab_writer_commit(w) < 0)
		return fail("tab_writer_commit");
	tab_writer_close(w);
	return 0;
}

static int
write_corpus(const char *path, int variant)
{
	Tab *t;
	TabRow *r;
	char key[32];
	int i;
	int count;

	t = create_plain_table(path, 3);
	if(t == NULL)
		return fail("tab_create");
	count = (int)(sizeof edge_values / sizeof edge_values[0]);
	for(i = 0; i < 12; i++){
		snprintf(key, sizeof key, "row-%02d-%02d", variant, i);
		r = tab_add_row(t, "k", key);
		if(r == NULL)
			return fail("tab_add_row");
		if(set_plain(t, r, "v", edge_values[(variant + i) % count]) != 0)
			return 1;
		if(i % 4 == 0){
			if(tab_set(t, r, "w", NULL) < 0)
				return fail("tab_set");
		}else if(set_plain(t, r, "w", edge_values[(variant * 3 + i) % count]) != 0){
			return 1;
		}
	}
	if(tab_commit(t) < 0)
		return fail("tab_commit");
	tab_close(t);
	return 0;
}

static int
write_argon2id(const char *path)
{
	TabColSpec cols[2];
	Tab *t;
	TabRow *r;

	memset(cols, 0, sizeof cols);
	cols[0].name = "k";
	cols[1].name = "secret";
	cols[1].type = "HASHED";
	cols[1].algo = "argon2id";
	remove(path);
	t = tab_create(path, "accounts", cols, 2);
	if(t == NULL)
		return fail("tab_create");
	r = tab_add_row(t, "k", "scott");
	if(r == NULL)
		return fail("tab_add_row");
	if(tab_set_hashed(t, r, "secret", (const unsigned char *)"secret", 6) < 0)
		return fail("tab_set_hashed");
	if(tab_commit(t) < 0)
		return fail("tab_commit");
	tab_close(t);
	return 0;
}

static int
write_encrypted(const char *path)
{
	TabColSpec cols[2];
	Tab *t;
	TabRow *r;
	uint8_t key[32];

	memset(cols, 0, sizeof cols);
	cols[0].name = "k";
	cols[1].name = "secret";
	cols[1].type = "ENCRYPTED";
	init_seed(key);
	remove(path);
	t = tab_create(path, "secrets", cols, 2);
	if(t == NULL)
		return fail("tab_create");
	r = tab_add_row(t, "k", "message");
	if(r == NULL)
		return fail("tab_add_row");
	if(tab_set_encrypted(t, r, "secret",
			(const unsigned char *)"line one\nline two", 17, key) < 0)
		return fail("tab_set_encrypted");
	r = tab_add_row(t, "k", "empty");
	if(r == NULL)
		return fail("tab_add_row");
	if(tab_set_encrypted(t, r, "secret", (const unsigned char *)"", 0, key) < 0)
		return fail("tab_set_encrypted");
	if(tab_commit(t) < 0)
		return fail("tab_commit");
	tab_close(t);
	return 0;
}

static TabRow *
find_one(Tab *t, const char *key)
{
	TabIter *it;
	TabRow *r;
	TabRow *extra;

	it = tab_search(t, "k", key);
	if(it == NULL)
		return NULL;
	r = tab_iter_next(it);
	extra = tab_iter_next(it);
	tab_iter_close(it);
	if(r == NULL || extra != NULL)
		return NULL;
	return r;
}

static int
count_key(Tab *t, const char *key)
{
	TabIter *it;
	int count;

	it = tab_search(t, "k", key);
	if(it == NULL)
		return -1;
	count = 0;
	while(tab_iter_next(it) != NULL)
		count++;
	tab_iter_close(it);
	return count;
}

static int
expect_cell(TabRow *r, const char *col, const char *want)
{
	const char *got;

	got = tab_get(r, col);
	if(want == NULL){
		if(got == NULL)
			return 0;
		fprintf(stderr, "cell %s: got %s want nil\n", col, got);
		return 1;
	}
	if(got != NULL && strcmp(got, want) == 0)
		return 0;
	fprintf(stderr, "cell %s: got %s want %s\n", col, got == NULL ? "(nil)" : got, want);
	return 1;
}

static int
verify_basic_tab(Tab *t)
{
	TabRow *r;

	if(tab_ncolumns(t) != 2)
		return fail_msg("basic: wrong column count");
	r = find_one(t, "alpha");
	if(r == NULL || expect_cell(r, "v", "one") != 0)
		return fail_msg("basic: alpha mismatch");
	r = find_one(t, "beta");
	if(r == NULL || expect_cell(r, "v", "two") != 0)
		return fail_msg("basic: beta mismatch");
	return 0;
}

static int
verify_text_tab(Tab *t)
{
	TabRow *r;
	char key[32];
	int i;
	int count;

	count = (int)(sizeof edge_values / sizeof edge_values[0]);
	for(i = 0; i < count; i++){
		snprintf(key, sizeof key, "text-%02d", i);
		r = find_one(t, key);
		if(r == NULL || expect_cell(r, "v", edge_values[i]) != 0)
			return fail_msg("text: row mismatch");
	}
	return 0;
}

static int
verify_mixed_tab(Tab *t)
{
	TabRow *r;
	uint8_t secret_key[64];
	uint8_t public_key[32];
	unsigned char *body;
	int body_len;

	init_key_pair(secret_key, public_key);
	r = find_one(t, "plain");
	if(r == NULL)
		return fail_msg("mixed: missing plain row");
	if(expect_cell(r, "v", "Divine Comedy") != 0)
		return 1;
	if(tab_verify_hash(r, "digest", (const unsigned char *)"secret", 6) != 1)
		return fail("tab_verify_hash");
	body = tab_verify_signed(r, "proof", public_key, &body_len);
	if(body == NULL)
		return fail("tab_verify_signed");
	if(body_len != 6 || memcmp(body, "vector", 6) != 0){
		free(body);
		return fail_msg("mixed: signed body mismatch");
	}
	free(body);
	r = find_one(t, "quote");
	if(r == NULL || expect_cell(r, "v", "The Number \"e\"") != 0)
		return fail_msg("mixed: quote mismatch");
	r = find_one(t, "newline");
	if(r == NULL || expect_cell(r, "v", "line one\nline two") != 0)
		return fail_msg("mixed: newline mismatch");
	r = find_one(t, "literal-nil");
	if(r == NULL || expect_cell(r, "v", "nil") != 0)
		return fail_msg("mixed: literal nil mismatch");
	r = find_one(t, "empty");
	if(r == NULL || expect_cell(r, "v", "") != 0)
		return fail_msg("mixed: empty mismatch");
	return 0;
}

static int
verify_writer_tab(Tab *t)
{
	TabRow *r;

	if(count_key(t, "space") != 1)
		return fail_msg("writer: duplicate row was not deduped");
	r = find_one(t, "space");
	if(r == NULL || expect_cell(r, "v", "Divine Comedy") != 0 ||
	   expect_cell(r, "w", "nil") != 0)
		return fail_msg("writer: space row mismatch");
	r = find_one(t, "quote");
	if(r == NULL || expect_cell(r, "v", "The Number \"e\"") != 0 ||
	   expect_cell(r, "w", "line one\nline two") != 0)
		return fail_msg("writer: quote row mismatch");
	r = find_one(t, "clear");
	if(r == NULL || expect_cell(r, "v", NULL) != 0)
		return fail_msg("writer: clear row mismatch");
	return 0;
}

static int
verify_corpus_tab(Tab *t, int variant)
{
	TabRow *r;
	char key[32];
	int i;
	int count;

	count = (int)(sizeof edge_values / sizeof edge_values[0]);
	for(i = 0; i < 12; i++){
		snprintf(key, sizeof key, "row-%02d-%02d", variant, i);
		r = find_one(t, key);
		if(r == NULL || expect_cell(r, "v", edge_values[(variant + i) % count]) != 0)
			return fail_msg("corpus: value mismatch");
		if(i % 4 == 0){
			if(expect_cell(r, "w", NULL) != 0)
				return fail_msg("corpus: nil mismatch");
		}else if(expect_cell(r, "w", edge_values[(variant * 3 + i) % count]) != 0){
			return fail_msg("corpus: extra mismatch");
		}
	}
	return 0;
}

static int
verify_argon2id_tab(Tab *t)
{
	TabRow *r;

	r = find_one(t, "scott");
	if(r == NULL)
		return fail_msg("argon2id: missing row");
	if(tab_verify_hash(r, "secret", (const unsigned char *)"secret", 6) != 1)
		return fail("tab_verify_hash");
	return 0;
}

static int
verify_encrypted_tab(Tab *t)
{
	TabRow *r;
	uint8_t key[32], wrong[32];
	unsigned char *out;
	int outlen;

	init_seed(key);
	init_seed(wrong);
	wrong[0] ^= 0xff;

	r = find_one(t, "message");
	if(r == NULL)
		return fail_msg("encrypted: missing message row");
	if(tab_cell_has_tag(tab_get(r, "secret"), "ENCRYPTED") != 1)
		return fail_msg("encrypted: missing tag");
	out = tab_decrypt(r, "secret", key, &outlen);
	if(out == NULL)
		return fail("tab_decrypt");
	if(outlen != 17 || memcmp(out, "line one\nline two", 17) != 0){
		free(out);
		return fail_msg("encrypted: plaintext mismatch");
	}
	free(out);
	out = tab_decrypt(r, "secret", wrong, &outlen);
	if(out != NULL){
		free(out);
		return fail_msg("encrypted: wrong key unexpectedly decrypted");
	}

	r = find_one(t, "empty");
	if(r == NULL)
		return fail_msg("encrypted: missing empty row");
	out = tab_decrypt(r, "secret", key, &outlen);
	if(out == NULL)
		return fail("tab_decrypt");
	if(outlen != 0){
		free(out);
		return fail_msg("encrypted: empty plaintext length mismatch");
	}
	free(out);
	return 0;
}

static int
open_and_verify(const char *case_name, const char *path)
{
	Tab *t;
	int rc;

	t = tab_open(path);
	if(t == NULL)
		return fail("tab_open");
	if(strcmp(case_name, "basic") == 0)
		rc = verify_basic_tab(t);
	else if(strcmp(case_name, "text") == 0)
		rc = verify_text_tab(t);
	else if(strcmp(case_name, "mixed") == 0)
		rc = verify_mixed_tab(t);
	else if(strcmp(case_name, "writer") == 0)
		rc = verify_writer_tab(t);
	else if(strcmp(case_name, "argon2id") == 0)
		rc = verify_argon2id_tab(t);
	else if(strcmp(case_name, "encrypted") == 0)
		rc = verify_encrypted_tab(t);
	else if(strncmp(case_name, "corpus-", 7) == 0)
		rc = verify_corpus_tab(t, atoi(case_name + 7));
	else
		rc = fail_msg("unknown verify case");
	tab_close(t);
	return rc;
}

static int
write_case(const char *case_name, const char *path)
{
	if(strcmp(case_name, "basic") == 0)
		return write_basic(path);
	if(strcmp(case_name, "text") == 0)
		return write_text(path);
	if(strcmp(case_name, "mixed") == 0)
		return write_mixed(path);
	if(strcmp(case_name, "writer") == 0)
		return write_writer(path);
	if(strcmp(case_name, "argon2id") == 0)
		return write_argon2id(path);
	if(strcmp(case_name, "encrypted") == 0)
		return write_encrypted(path);
	if(strncmp(case_name, "corpus-", 7) == 0)
		return write_corpus(path, atoi(case_name + 7));
	return fail_msg("unknown write case");
}

static int
expect_open(const char *path, int should_open)
{
	Tab *t;

	t = tab_open(path);
	if(should_open){
		if(t == NULL)
			return fail("tab_open");
		tab_close(t);
		return 0;
	}
	if(t != NULL){
		tab_close(t);
		return fail_msg("tab_open unexpectedly accepted file");
	}
	return 0;
}

static int
expect_decrypt_fail(const char *path)
{
	Tab *t;
	TabRow *r;
	uint8_t key[32];
	unsigned char *out;
	int outlen;

	t = tab_open(path);
	if(t == NULL)
		return fail("tab_open");
	r = find_one(t, "bad");
	if(r == NULL){
		tab_close(t);
		return fail_msg("decrypt-fail: missing bad row");
	}
	init_seed(key);
	out = tab_decrypt(r, "secret", key, &outlen);
	if(out != NULL){
		free(out);
		tab_close(t);
		return fail_msg("decrypt-fail: malformed cell unexpectedly decrypted");
	}
	tab_close(t);
	return 0;
}

static int
expect_limits(const char *path)
{
	TabColSpec cols[4];
	TabColSpec writer_cols[2];
	Tab *t;
	TabRow *r;
	TabWriter *w;
	char *ok_val, *big_val, *amps;
	uint8_t key[32], secret_key[64], public_key[32];
	unsigned char *big_body;

	ok_val = bigval(TabMaxCell - 64, 'x');
	big_val = bigval(TabMaxCell + 1, 'x');
	amps = bigval(TabMaxCell / 2, '&');
	big_body = (unsigned char *)bigval(TabMaxCell, 'x');
	if(ok_val == NULL || big_val == NULL || amps == NULL || big_body == NULL)
		return fail_msg("limits: allocation failed");

	t = create_plain_table(path, 2);
	if(t == NULL)
		return fail("tab_create");
	r = tab_add_row(t, "k", "a");
	if(r == NULL)
		return fail("tab_add_row");
	if(tab_set(t, r, "v", ok_val) < 0)
		return fail("tab_set accepted value");
	if(tab_set(t, r, "v", big_val) >= 0)
		return fail_msg("limits: oversized plain cell accepted");
	if(strcmp(tab_get(r, "v"), ok_val) != 0)
		return fail_msg("limits: refused plain write changed old value");
	if(tab_set(t, r, "v", amps) >= 0)
		return fail_msg("limits: encoded expansion was not counted");
	if(tab_add_row(t, "k", big_val) != NULL)
		return fail_msg("limits: oversized row head accepted");
	tab_close(t);

	memset(cols, 0, sizeof cols);
	cols[0].name = "k";
	cols[1].name = "e";
	cols[1].type = "ENCRYPTED";
	cols[2].name = "s";
	cols[2].type = "SIGNED";
	cols[3].name = "h";
	cols[3].type = "HASHED";
	init_seed(key);
	init_key_pair(secret_key, public_key);
	remove(path);
	t = tab_create(path, "test", cols, 4);
	if(t == NULL)
		return fail("tab_create typed");
	r = tab_add_row(t, "k", "a");
	if(r == NULL)
		return fail("tab_add_row typed");
	if(tab_set_encrypted(t, r, "e", big_body, TabMaxCell, key) >= 0)
		return fail_msg("limits: oversized encrypted body accepted");
	if(tab_set_signed(t, r, "s", big_body, TabMaxCell, secret_key) >= 0)
		return fail_msg("limits: oversized signed body accepted");
	if(tab_set_hashed(t, r, "h", big_body, TabMaxCell) < 0)
		return fail("tab_set_hashed large preimage");
	tab_close(t);

	memset(writer_cols, 0, sizeof writer_cols);
	writer_cols[0].name = "k";
	writer_cols[1].name = "v";
	w = tab_writer_create(path, "test", writer_cols, 2);
	if(w == NULL)
		return fail("tab_writer_create");
	if(tab_writer_add_row(w, "k", "a") < 0)
		return fail("tab_writer_add_row");
	if(tab_writer_set(w, "v", big_val) >= 0)
		return fail_msg("limits: writer accepted oversized value");
	if(tab_writer_add_row(w, "k", big_val) >= 0)
		return fail_msg("limits: writer accepted oversized head");
	tab_writer_close(w);

	free(ok_val);
	free(big_val);
	free(amps);
	free(big_body);
	return 0;
}

int
main(int argc, char **argv)
{
	if(argc == 4 && strcmp(argv[1], "write") == 0)
		return write_case(argv[2], argv[3]);
	if(argc == 4 && strcmp(argv[1], "verify") == 0)
		return open_and_verify(argv[2], argv[3]);
	if(argc == 3 && strcmp(argv[1], "expect-open-fail") == 0)
		return expect_open(argv[2], 0);
	if(argc == 3 && strcmp(argv[1], "expect-open-ok") == 0)
		return expect_open(argv[2], 1);
	if(argc == 3 && strcmp(argv[1], "expect-decrypt-fail") == 0)
		return expect_decrypt_fail(argv[2]);
	if(argc == 3 && strcmp(argv[1], "expect-limits") == 0)
		return expect_limits(argv[2]);
	fprintf(stderr, "usage: c_equiv write|verify case path | expect-open-fail|expect-open-ok|expect-decrypt-fail|expect-limits path\n");
	return 2;
}
C

cat > "$tmp/cs/LibTabEquiv.csproj" <<CSHARP_PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$repo_root/LibTab/LibTab.csproj" />
  </ItemGroup>
</Project>
CSHARP_PROJ

cat > "$tmp/cs/Program.cs" <<'CSHARP'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LibTab;

try
{
    return Dispatch(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Dispatch(string[] args)
{
    if (args.Length == 3 && args[0] == "write")
    {
        WriteCase(args[1], args[2]);
        return 0;
    }

    if (args.Length == 3 && args[0] == "verify")
    {
        VerifyCase(args[1], args[2]);
        return 0;
    }

    if (args.Length == 2 && args[0] == "expect-open-fail")
    {
        ExpectOpen(args[1], shouldOpen: false);
        return 0;
    }

    if (args.Length == 2 && args[0] == "expect-open-ok")
    {
        ExpectOpen(args[1], shouldOpen: true);
        return 0;
    }

    if (args.Length == 2 && args[0] == "expect-decrypt-fail")
    {
        ExpectDecryptFail(args[1]);
        return 0;
    }

    if (args.Length == 2 && args[0] == "expect-limits")
    {
        ExpectLimits(args[1]);
        return 0;
    }

    Console.Error.WriteLine("usage: LibTabEquiv write|verify case path | expect-open-fail|expect-open-ok|expect-decrypt-fail|expect-limits path");
    return 2;
}

static void WriteCase(string caseName, string path)
{
    switch (caseName)
    {
        case "basic":
            WriteBasic(path);
            return;
        case "text":
            WriteText(path);
            return;
        case "mixed":
            WriteMixed(path);
            return;
        case "writer":
            WriteWriter(path);
            return;
        case "argon2id":
            WriteArgon2id(path);
            return;
        case "encrypted":
            WriteEncrypted(path);
            return;
        default:
            if (caseName.StartsWith("corpus-", StringComparison.Ordinal))
            {
                WriteCorpus(path, int.Parse(caseName[7..], CultureInfo.InvariantCulture));
                return;
            }

            throw new InvalidOperationException($"unknown write case '{caseName}'");
    }
}

static void VerifyCase(string caseName, string path)
{
    TabTable table = TabTable.Open(path);
    switch (caseName)
    {
        case "basic":
            VerifyBasic(table);
            return;
        case "text":
            VerifyText(table);
            return;
        case "mixed":
            VerifyMixed(table);
            return;
        case "writer":
            VerifyWriter(table);
            return;
        case "argon2id":
            VerifyArgon2id(table);
            return;
        case "encrypted":
            VerifyEncrypted(table);
            return;
        default:
            if (caseName.StartsWith("corpus-", StringComparison.Ordinal))
            {
                VerifyCorpus(table, int.Parse(caseName[7..], CultureInfo.InvariantCulture));
                return;
            }

            throw new InvalidOperationException($"unknown verify case '{caseName}'");
    }
}

static void ExpectOpen(string path, bool shouldOpen)
{
    try
    {
        _ = TabTable.Open(path);
        if (!shouldOpen)
        {
            throw new InvalidOperationException("TabTable.Open unexpectedly accepted file");
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TabException)
    {
        if (shouldOpen)
        {
            throw;
        }
    }
}

static void ExpectDecryptFail(string path)
{
    TabTable table = TabTable.Open(path);
    TabRow row = FindOne(table, "bad");

    MustThrow(() => table.Decrypt(row, "secret", EncryptionKey()), "malformed encrypted cell decrypted");
}

static void ExpectLimits(string path)
{
    string accepted = new('x', TabTable.MaxCellLineBytes - 64);
    string oversized = new('x', TabTable.MaxCellLineBytes + 1);
    string ampersands = new('&', TabTable.MaxCellLineBytes / 2);
    byte[] body = Encoding.ASCII.GetBytes(new string('x', TabTable.MaxCellLineBytes));

    File.Delete(path);
    TabTable plain = TabTable.Create(path, "test", PlainColumns("k", "v"));
    TabRow plainRow = plain.AddRow("k", "a");
    plain.Set(plainRow, "v", accepted);
    MustThrow(() => plain.Set(plainRow, "v", oversized), "oversized plain cell accepted");
    if (!string.Equals(accepted, plainRow.Get("v"), StringComparison.Ordinal))
    {
        throw new InvalidOperationException("refused plain write changed old value");
    }

    MustThrow(() => plain.Set(plainRow, "v", ampersands), "encoded expansion was not counted");
    MustThrow(() => plain.AddRow("k", oversized), "oversized row head accepted");

    var typedColumns = new[]
    {
        new TabColumn("k"),
        new TabColumn("e", "ENCRYPTED"),
        new TabColumn("s", "SIGNED"),
        new TabColumn("h", "HASHED"),
    };
    TabKeyPair keyPair = PinnedKeyPair();
    TabTable typed = TabTable.Create(path, "test", typedColumns);
    TabRow typedRow = typed.AddRow("k", "a");
    MustThrow(() => typed.SetEncrypted(typedRow, "e", body, EncryptionKey()), "oversized encrypted body accepted");
    MustThrow(() => typed.SetSigned(typedRow, "s", body, keyPair.SecretKey), "oversized signed body accepted");
    typed.SetHashed(typedRow, "h", body);
    if (!typed.VerifyHash(typedRow, "h", body))
    {
        throw new InvalidOperationException("large HASHED preimage did not verify");
    }

    using TabWriter writer = TabWriter.Create(path, "test", PlainColumns("k", "v"));
    writer.AddRow("k", "a");
    MustThrow(() => writer.Set("v", oversized), "writer accepted oversized value");
    MustThrow(() => writer.AddRow("k", oversized), "writer accepted oversized row head");
}

static void MustThrow(Action action, string message)
{
    try
    {
        action();
    }
    catch (TabException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static IReadOnlyList<string> EdgeValues()
{
    return new[]
    {
        "Divine Comedy",
        "The Number \"e\"",
        "line one\nline two",
        "left\tright",
        "nil",
        "AT&T",
        "#not-a-comment",
        "__libtab_text64_v1:not-user-visible",
        string.Empty,
        "café",
        "plain-token",
        "semi;colon",
    };
}

static TabColumn[] PlainColumns(params string[] names)
{
    return names.Select(name => new TabColumn(name)).ToArray();
}

static void WriteBasic(string path)
{
    File.Delete(path);
    TabTable table = TabTable.Create(path, "test", PlainColumns("k", "v"));
    AddPlainRow(table, "alpha", "one");
    AddPlainRow(table, "beta", "two");
    table.Commit();
}

static void WriteText(string path)
{
    File.Delete(path);
    TabTable table = TabTable.Create(path, "test", PlainColumns("k", "v"));
    IReadOnlyList<string> values = EdgeValues();
    for (int i = 0; i < values.Count; i++)
    {
        AddPlainRow(table, string.Create(CultureInfo.InvariantCulture, $"text-{i:00}"), values[i]);
    }

    table.Commit();
}

static void WriteMixed(string path)
{
    File.Delete(path);
    TabKeyPair keyPair = PinnedKeyPair();
    var columns = new[]
    {
        new TabColumn("k"),
        new TabColumn("v"),
        new TabColumn("digest", "HASHED"),
        new TabColumn("proof", "SIGNED", new Dictionary<string, string> { ["signer"] = "pinned" }),
    };

    TabTable table = TabTable.Create(path, "test", columns);
    TabRow row = table.AddRow("k", "plain");
    table.Set(row, "v", "Divine Comedy");
    table.SetHashed(row, "digest", Encoding.UTF8.GetBytes("secret"));
    table.SetSigned(row, "proof", Encoding.UTF8.GetBytes("vector"), keyPair.SecretKey);

    AddPlainRow(table, "quote", "The Number \"e\"");
    AddPlainRow(table, "newline", "line one\nline two");
    AddPlainRow(table, "literal-nil", "nil");
    AddPlainRow(table, "amp", "AT&T");
    AddPlainRow(table, "hash", "#not-a-comment");
    AddPlainRow(table, "prefix", "__libtab_text64_v1:not-user-visible");
    AddPlainRow(table, "empty", string.Empty);
    table.Commit();
}

static void WriteWriter(string path)
{
    File.Delete(path);
    using TabWriter writer = TabWriter.Create(path, "test", PlainColumns("k", "v", "w"));
    writer.AddRow("k", "space");
    writer.Set("v", "Divine Comedy");
    writer.Set("w", "nil");
    writer.AddRow("k", "quote");
    writer.Set("v", "The Number \"e\"");
    writer.Set("w", "line one\nline two");
    writer.AddRow("k", "clear");
    writer.Set("v", "gone");
    writer.Clear("v");
    writer.AddRow("k", "space");
    writer.Set("v", "Divine Comedy");
    writer.Set("w", "nil");
    writer.Commit();
}

static void WriteCorpus(string path, int variant)
{
    File.Delete(path);
    TabTable table = TabTable.Create(path, "test", PlainColumns("k", "v", "w"));
    IReadOnlyList<string> values = EdgeValues();
    for (int i = 0; i < 12; i++)
    {
        TabRow row = table.AddRow("k", string.Create(CultureInfo.InvariantCulture, $"row-{variant:00}-{i:00}"));
        table.Set(row, "v", values[(variant + i) % values.Count]);
        table.Set(row, "w", i % 4 == 0 ? null : values[((variant * 3) + i) % values.Count]);
    }

    table.Commit();
}

static void WriteArgon2id(string path)
{
    File.Delete(path);
    var columns = new[]
    {
        new TabColumn("k"),
        new TabColumn("secret", "HASHED", new Dictionary<string, string> { ["algo"] = "argon2id" }),
    };

    TabTable table = TabTable.Create(path, "accounts", columns);
    TabRow row = table.AddRow("k", "scott");
    table.SetHashed(row, "secret", Encoding.UTF8.GetBytes("secret"));
    table.Commit();
}

static void WriteEncrypted(string path)
{
    File.Delete(path);
    var columns = new[]
    {
        new TabColumn("k"),
        new TabColumn("secret", "ENCRYPTED"),
    };

    TabTable table = TabTable.Create(path, "secrets", columns);
    TabRow row = table.AddRow("k", "message");
    table.SetEncrypted(row, "secret", Encoding.UTF8.GetBytes("line one\nline two"), EncryptionKey());
    row = table.AddRow("k", "empty");
    table.SetEncrypted(row, "secret", Array.Empty<byte>(), EncryptionKey());
    table.Commit();
}

static void VerifyBasic(TabTable table)
{
    if (table.Columns.Count != 2)
    {
        throw new InvalidOperationException("basic: wrong column count");
    }

    ExpectCell(FindOne(table, "alpha"), "v", "one");
    ExpectCell(FindOne(table, "beta"), "v", "two");
}

static void VerifyText(TabTable table)
{
    IReadOnlyList<string> values = EdgeValues();
    for (int i = 0; i < values.Count; i++)
    {
        ExpectCell(FindOne(table, string.Create(CultureInfo.InvariantCulture, $"text-{i:00}")), "v", values[i]);
    }
}

static void VerifyMixed(TabTable table)
{
    TabKeyPair keyPair = PinnedKeyPair();
    TabRow row = FindOne(table, "plain");
    ExpectCell(row, "v", "Divine Comedy");
    if (!table.VerifyHash(row, "digest", Encoding.UTF8.GetBytes("secret")))
    {
        throw new InvalidOperationException("mixed: hash mismatch");
    }

    string body = Encoding.UTF8.GetString(table.VerifySigned(row, "proof", keyPair.PublicKey));
    if (body != "vector")
    {
        throw new InvalidOperationException("mixed: signed body mismatch");
    }

    ExpectCell(FindOne(table, "quote"), "v", "The Number \"e\"");
    ExpectCell(FindOne(table, "newline"), "v", "line one\nline two");
    ExpectCell(FindOne(table, "literal-nil"), "v", "nil");
    ExpectCell(FindOne(table, "empty"), "v", string.Empty);
}

static void VerifyWriter(TabTable table)
{
    if (table.Search("k", "space").Count() != 1)
    {
        throw new InvalidOperationException("writer: duplicate row was not deduped");
    }

    TabRow row = FindOne(table, "space");
    ExpectCell(row, "v", "Divine Comedy");
    ExpectCell(row, "w", "nil");
    row = FindOne(table, "quote");
    ExpectCell(row, "v", "The Number \"e\"");
    ExpectCell(row, "w", "line one\nline two");
    ExpectCell(FindOne(table, "clear"), "v", null);
}

static void VerifyCorpus(TabTable table, int variant)
{
    IReadOnlyList<string> values = EdgeValues();
    for (int i = 0; i < 12; i++)
    {
        TabRow row = FindOne(table, string.Create(CultureInfo.InvariantCulture, $"row-{variant:00}-{i:00}"));
        ExpectCell(row, "v", values[(variant + i) % values.Count]);
        ExpectCell(row, "w", i % 4 == 0 ? null : values[((variant * 3) + i) % values.Count]);
    }
}

static void VerifyArgon2id(TabTable table)
{
    TabRow row = FindOne(table, "scott");
    if (!table.VerifyHash(row, "secret", Encoding.UTF8.GetBytes("secret")))
    {
        throw new InvalidOperationException("argon2id: hash mismatch");
    }
}

static void VerifyEncrypted(TabTable table)
{
    byte[] key = EncryptionKey();
    byte[] wrong = EncryptionKey();
    wrong[0] ^= 0xff;

    TabRow row = FindOne(table, "message");
    string? cell = row.Get("secret");
    if (!TabCodec.CellHasTag(cell, "ENCRYPTED"))
    {
        throw new InvalidOperationException("encrypted: missing tag");
    }

    string plaintext = Encoding.UTF8.GetString(table.Decrypt(row, "secret", key));
    if (plaintext != "line one\nline two")
    {
        throw new InvalidOperationException("encrypted: plaintext mismatch");
    }

    MustThrow(() => table.Decrypt(row, "secret", wrong), "encrypted: wrong key unexpectedly decrypted");

    row = FindOne(table, "empty");
    if (table.Decrypt(row, "secret", key).Length != 0)
    {
        throw new InvalidOperationException("encrypted: empty plaintext length mismatch");
    }
}

static void AddPlainRow(TabTable table, string key, string value)
{
    TabRow row = table.AddRow("k", key);
    table.Set(row, "v", value);
}

static TabRow FindOne(TabTable table, string key)
{
    TabRow[] rows = table.Search("k", key).ToArray();
    if (rows.Length != 1)
    {
        throw new InvalidOperationException($"key '{key}' matched {rows.Length} rows");
    }

    return rows[0];
}

static void ExpectCell(TabRow row, string column, string? expected)
{
    string? actual = row.Get(column);
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"cell {column}: got '{actual ?? "(nil)"}' want '{expected ?? "(nil)"}'");
    }
}

static TabKeyPair PinnedKeyPair()
{
    return TabKeyPair.FromSeed(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
}

static byte[] EncryptionKey()
{
    return Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
}
CSHARP

9c -I"$libtab_c_repo" -o "$tmp/c_equiv.o" "$tmp/c_equiv.c"
9l -o "$tmp/c_equiv" "$tmp/c_equiv.o" "$libtab_c_repo/libtab.a"
dotnet restore "$tmp/cs/LibTabEquiv.csproj" -v quiet /p:NuGetAudit=false

for case_name in basic text mixed writer; do
  run_case "$case_name"
done

for variant in $(seq 0 31); do
  run_case "corpus-$variant"
done

run_argon2id_case
run_encrypted_case
run_limit_case

run_malformed_case missing-schema expect-open-fail 'k=row\n\n'
run_malformed_case duplicate-column expect-open-fail 'schema=test\n\tcol=k\n\tcol=k\n\n'
run_malformed_case undeclared-column expect-open-fail 'schema=test\n\tcol=k\n\nk=row\n\tv=one\n\n'
run_malformed_case missing-typed-tag expect-open-fail 'schema=test\n\tcol=k\n\tcol=digest type=HASHED\n\nk=row\n\tdigest=plain\n\n'
run_malformed_case tagged-bad-payload expect-open-ok 'schema=test\n\tcol=k\n\tcol=digest type=HASHED\n\nk=row\n\tdigest=hashed:@@@@\n\n'
run_malformed_case missing-encrypted-tag expect-open-fail 'schema=test\n\tcol=k\n\tcol=secret type=ENCRYPTED\n\nk=bad\n\tsecret=plain\n\n'
run_decrypt_fail_case truncated-encrypted 'schema=test\n\tcol=k\n\tcol=secret type=ENCRYPTED\n\nk=bad\n\tsecret=encrypted:AQID\n\n'

for case_name in basic text mixed writer; do
  verify_versioned_fixture "$case_name"
done

verify_versioned_malformed duplicate-column expect-open-fail
verify_versioned_malformed missing-schema expect-open-fail
verify_versioned_malformed missing-typed-tag expect-open-fail
verify_versioned_malformed undeclared-column expect-open-fail
verify_versioned_malformed tagged-bad-payload expect-open-ok
