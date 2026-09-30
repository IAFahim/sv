#!/usr/bin/env bash
# Runs the sv benchmark suite in markdown mode and refreshes the auto-generated
# table between the bench markers in README.md. Prints the per-scenario deltas
# versus the numbers previously published in the README (regressions flagged) to
# stdout and to the GitHub step summary. Exits nonzero if any scenario FAILED —
# the table is left untouched in that case.
set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly README="$ROOT/README.md"
readonly BEGIN='<!-- bench:begin (auto-generated — CI refreshes this on every push to main; do not edit by hand) -->'
readonly END='<!-- bench:end -->'
readonly THRESHOLD="${BENCH_REGRESSION_THRESHOLD:-1.25}"

cd "$ROOT"
awk '/bench:begin/{f=1;next} /bench:end/{f=0;next} f' "$README" > /tmp/bench-old.txt || true
dotnet run -c Release --project benchmarks/Sv.Bench -- all --md > /tmp/bench-md.txt

if grep -q 'FAIL' /tmp/bench-md.txt; then
    echo "bench scenarios failed — README left untouched:"
    cat /tmp/bench-md.txt
    exit 1
fi

when="$(date -u '+%Y-%m-%d %H:%M UTC')"
commit="$(git rev-parse --short HEAD)"
cpu="$(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2- | xargs)"
[ -n "$cpu" ] || cpu="unknown cpu"
cores="$(nproc)"
meta="$(grep -m1 'sv-bench' /tmp/bench-md.txt | sed 's/<!-- //; s/ -->//')"

section="Last refreshed $when · \`main@$commit\` · $cpu ($cores cores) · $meta

| Scenario | µs/frame | rate | Managed alloc |
|---|---:|---:|---|
$(grep '^|' /tmp/bench-md.txt)"

awk -v begin="$BEGIN" -v end="$END" -v section="$section" '
    $0 == begin { print; inblock = 1; print ""; print section; print ""; next }
    $0 == end { inblock = 0; print; next }
    !inblock { print }
' "$README" > /tmp/readme-new.md
mv /tmp/readme-new.md "$README"

summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
echo "### Benchmark refresh ($when, $cpu, $cores cores)" >> "$summary"
regressions=0
while IFS='|' read -r _ name new_us _; do
    name="$(echo "$name" | xargs)"
    new_us="$(echo "$new_us" | xargs)"
    case "$name" in ''|Scenario*) continue ;; esac
    old_us="$(awk -F'|' -v n="$name" '
        $0 ~ "^\\| "n" \\|" { gsub(/ /, "", $3); print $3; exit }
    ' /tmp/bench-old.txt)"
    [ -n "$old_us" ] || continue
    old="$(awk -v a="$old_us" -v b="$new_us" 'BEGIN { print (b > a) }')"
    if [ "$old" = "1" ]; then
        ratio="$(awk -v a="$old_us" -v b="$new_us" 'BEGIN { printf "%.2f", b / a }')"
        worse="$(awk -v r="$ratio" -v t="$THRESHOLD" 'BEGIN { print (r > t) }')"
        if [ "$worse" = "1" ]; then
            echo "⚠️ regression: **$name** $old_us → $new_us µs/frame (×$ratio, threshold ×$THRESHOLD)" >> "$summary"
            regressions=$((regressions + 1))
        else
            echo "$name: $old_us → $new_us µs/frame (×$ratio, within noise)" >> "$summary"
        fi
    fi
done < <(grep '^|' /tmp/bench-md.txt)

if [ "$regressions" -gt 0 ]; then
    echo "**$regressions scenario(s) slower than ×$THRESHOLD of the published numbers — take a look.**" >> "$summary"
else
    echo "No regressions beyond ×$THRESHOLD versus the previously published numbers." >> "$summary"
fi
echo "README table refreshed."
