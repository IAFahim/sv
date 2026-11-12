import re
import sys
from collections import defaultdict

stages = ["svMain", "scatter", "fold", "kinds", "derive", "digest", "total", "apply", "recalc"]
cells = defaultdict(list)
extra = defaultdict(list)
for path in sys.argv[1:]:
    for line in open(path):
        if not line.startswith("cell "):
            continue
        f = dict(re.findall(r"(\w+)=(\S+)", line))
        key = (f["shape"], f["W"], f["cross"], f["window"])
        cells[key].append({s: float(f[s]) for s in stages})
        extra[key].append((int(f["mainBytes"]), int(f["allBytes"]), int(f["svProbeManaged"]), int(f["kindProbeManaged"]), f["statOps"]))


def med(values):
    values = sorted(values)
    return values[len(values) // 2]


print("| shape | W | cross | window | n | sv-main (apply/recalc) | scatter | fold | kinds* | derive | digest | sv total | total [range] | main B | all B | probes |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
order = {"P1": 0, "P2": 1, "P3": 2}
for key in sorted(cells, key=lambda k: (order[k[0]], k[3], float(k[1]), k[2])):
    runs = cells[key]
    m = {s: med([r[s] for r in runs]) for s in stages}
    totals = sorted(r["total"] for r in runs)
    sv = m["svMain"] + m["scatter"] + m["fold"] + m["derive"] + m["digest"]
    e = extra[key]
    main = max(x[0] for x in e)
    allb = max(x[1] for x in e)
    probes = "ok" if all(x[2] == 0 and x[3] == 0 for x in e) else "MANAGED"
    print(f"| {key[0]} | {key[1]} | {key[2]} | {key[3]} | {len(runs)} | {m['svMain']:.3f} ({m['apply']:.2f}/{m['recalc']:.2f}) | {m['scatter']:.3f} | {m['fold']:.3f} | {m['kinds']:.3f} | {m['derive']:.3f} | {m['digest']:.3f} | {sv:.3f} | {m['total']:.3f} [{totals[0]:.3f}-{totals[-1]:.3f}] | {main} | {allb} | {probes} |")
