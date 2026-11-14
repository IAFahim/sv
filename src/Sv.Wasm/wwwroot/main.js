import { dotnet } from './_framework/dotnet.js';

const files = [
  { key: 'values', name: 'values.sv.json' },
  { key: 'rules', name: 'rules.sv.json' },
  { key: 'cards', name: 'cards.sv.json' },
  { key: 'templates', name: 'templates.sv.json' },
  { key: 'scenario', name: 'scenario.json' },
];
const sources = {};
let current = 'values';
let api = null;
let lastRun = null;

const $ = (id) => document.getElementById(id);

function saved(key) {
  try { return localStorage.getItem('sv.' + key); } catch { return null; }
}

function save(key, text) {
  try { localStorage.setItem('sv.' + key, text); } catch { }
}

async function loadSamples(force) {
  for (const f of files) {
    const stored = force ? null : saved(f.key);
    if (stored !== null) { sources[f.key] = stored; continue; }
    const path = f.key === 'scenario' ? 'samples/worked_example.scenario.json' : 'samples/' + f.name;
    sources[f.key] = await (await fetch(path)).text();
    save(f.key, sources[f.key]);
  }
}

function showTabs() {
  const tabs = $('tabs');
  tabs.replaceChildren();
  for (const f of files) {
    const b = document.createElement('button');
    b.textContent = f.name;
    b.setAttribute('aria-selected', String(f.key === current));
    b.onclick = () => { current = f.key; showTabs(); $('text').value = sources[current]; };
    tabs.append(b);
  }
}

function pack() {
  return [sources.values, sources.rules, sources.cards, sources.templates];
}

function diagnostics(list) {
  const ul = $('diagnostics');
  ul.replaceChildren();
  for (const d of list || []) {
    const li = document.createElement('li');
    li.className = d.severity;
    li.textContent = `${d.file}(${d.offset}) ${d.severity} ${d.code}: ${d.message}`;
    ul.append(li);
  }
}

function summary(text, ok) {
  const s = $('summary');
  s.textContent = text;
  s.className = ok ? 'ok' : 'bad';
}

function renderTrace() {
  if (!lastRun) return;
  const entity = $('entity').value;
  const frame = $('frame').value;
  const token = entity === '*' ? '' : lastRun.entities.find((e) => e.name === entity).token;
  const out = [];
  for (const f of lastRun.frames) {
    if (frame !== '*' && String(f.frame) !== frame) continue;
    f.trace.forEach((line, i) => {
      if (!token || i === 0 || line.includes(token)) out.push(line);
    });
  }
  $('trace').textContent = out.join('\n');
}

function check() {
  const r = JSON.parse(api.Check(...pack()));
  diagnostics(r.diagnostics);
  summary(r.ok ? `pack compiles, PackHash ${r.hash}` : 'pack does not compile', r.ok);
  $('trace').textContent = '';
  lastRun = null;
}

function simulate() {
  const r = JSON.parse(api.Simulate(...pack(), sources.scenario));
  diagnostics(r.diagnostics);
  if (!r.ok) {
    summary(r.scenarioError || 'pack does not compile', false);
    $('trace').textContent = r.scenarioError || '';
    lastRun = null;
    return;
  }
  lastRun = r;
  summary(`${r.name}: ${r.passed ? 'passed' : 'FAILED'}, digest ${r.digest}, PackHash ${r.hash}`, r.passed);
  const entity = $('entity');
  entity.replaceChildren(new Option('all', '*'), ...r.entities.map((e) => new Option(e.name, e.name)));
  const frame = $('frame');
  frame.replaceChildren(new Option('all', '*'), ...r.frames.map((f) => new Option(String(f.frame), String(f.frame))));
  renderTrace();
  const extra = [...r.lines.filter((l) => !l.startsWith('digest')), ...r.failures.map((f) => 'FAIL ' + f)];
  if (extra.length) $('trace').textContent += '\n\n' + extra.join('\n');
}

function canonical() {
  const r = JSON.parse(api.Canonical(...pack()));
  diagnostics(r.diagnostics);
  if (!r.ok) { summary('pack does not compile', false); return null; }
  summary(`canonical pack, PackHash ${r.hash}`, true);
  return r;
}

function download() {
  const r = canonical();
  if (!r) return;
  const body = JSON.stringify({ hash: r.hash, 'values.sv.json': r.values, 'rules.sv.json': r.rules, 'cards.sv.json': r.cards, 'templates.sv.json': r.templates }, null, 2);
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([body], { type: 'application/json' }));
  a.download = `pack-${r.hash}.json`;
  a.click();
  URL.revokeObjectURL(a.href);
}

async function main() {
  showTabs();
  const { getAssemblyExports, getConfig } = await dotnet.withDiagnosticTracing(false).create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  api = exports.Sv.Wasm.Program;
  await loadSamples(false);
  $('text').value = sources[current];
  $('text').oninput = (e) => { sources[current] = e.target.value; save(current, e.target.value); };
  $('check').onclick = check;
  $('simulate').onclick = simulate;
  $('canonical').onclick = () => { const r = canonical(); if (r) $('trace').textContent = r.values; };
  $('download').onclick = download;
  $('reset').onclick = async () => { await loadSamples(true); $('text').value = sources[current]; summary('samples restored', true); };
  $('entity').onchange = renderTrace;
  $('frame').onchange = renderTrace;
  $('status').textContent = 'ready';
  $('status').dataset.ready = 'true';
}

main();
