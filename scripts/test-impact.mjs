#!/usr/bin/env node
// Selective test runner: pick the test classes a diff can actually reach.
//
// Why: the full server suite is ~320s because 104 E2E classes each boot a
// WebApplicationFactory host and parallelization is disabled on purpose
// (tests/QuickShare.Server.Tests/AssemblyInfo.cs explains the static-cache
// race). The inner loop must not pay for classes the change cannot touch.
//
// Selection is a UNION of independent signals, never an intersection — a
// missed signal must widen the run, never narrow it:
//   S1 explicit prefix rules      (scripts/test-impact.config.json)
//   S2 route literals             (src file mentions /api/uploads/* ->
//                                  tests that request /api/uploads/*)
//   S3 declared symbols           (class/record/interface/enum names and the
//                                  file name, matched as words in test sources)
//   S4 changed test files         (the class they declare runs)
//   S5 invariant rules            (permanent cross-cutting debts from
//                                  docs/TESTING.md's regression matrix)
//
// Safety valve: a changed source file that no signal reaches prints UNMAPPED
// and the run escalates to the full suite. Silence here would be a false green.
//
// Every selected class is verified to exist (class declarations are scanned);
// a renamed/removed class fails loudly instead of matching zero tests, which
// is how a stale doc map produces a green run that tested nothing.

import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..');
const CONFIG_PATH = path.join(ROOT, 'scripts', 'test-impact.config.json');

const TEST_DIRS = {
  server: 'tests/QuickShare.Server.Tests',
  browser: 'tests/QuickShare.Browser.Tests',
};

const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', '.git']);
const _testTextCache = new Map();
const CLASS_DECL = /^\s*(?:public|internal|private|protected)?\s*(?:sealed|static|abstract|partial)?\s*(?:sealed\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)/;
const TYPE_DECL = /\b(?:class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)/g;
const ROUTE_LITERAL = /"(\/[A-Za-z0-9\-_./{}$]*)"/g;

function main() {
  const args = parseArgs(process.argv.slice(2));
  const config = loadConfig();

  if (args.help) {
    printHelp();
    return;
  }

  const changed = collectChangedFiles(args, config);
  if (changed.length === 0) {
    console.log('No changed files. Nothing to run.');
    return;
  }

  const index = buildIndex();
  assertConfigClassesExist(config, index);

  const plan = args.full
    ? fullPlan(index, args)
    : buildPlan(changed, config, index);

  if (args.json) {
    console.log(JSON.stringify(plan.summary, null, 2));
    if (!args.run) return;
  } else {
    printPlan(plan, args, changed);
  }

  if (!args.run) {
    if (!args.json) console.log('\nDry run. Add --run to build and execute.');
    return;
  }

  process.exitCode = runPlan(plan, args);
}

// ── args ────────────────────────────────────────────────────────────────

function parseArgs(argv) {
  const args = {
    run: false,
    full: false,
    browser: false,
    verbose: false,
    json: false,
    help: false,
    noBuild: false,
    base: null,
    files: null,
    max: 45,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--run') args.run = true;
    else if (a === '--full') args.full = true;
    else if (a === '--browser') args.browser = true;
    else if (a === '--verbose' || a === '-v') args.verbose = true;
    else if (a === '--json') args.json = true;
    else if (a === '--help' || a === '-h') args.help = true;
    else if (a === '--no-build') args.noBuild = true;
    else if (a === '--base') args.base = argv[++i];
    else if (a === '--max') args.max = Number(argv[++i]);
    else if (a === '--files') {
      args.files = [];
      while (argv[i + 1] && !argv[i + 1].startsWith('-')) args.files.push(argv[++i]);
    } else {
      console.error(`Unknown argument: ${a}`);
      process.exit(2);
    }
  }
  return args;
}

function printHelp() {
  console.log(`test-impact — run only the tests a diff can reach

  node scripts/test-impact.mjs                  # plan for working tree (dry run)
  node scripts/test-impact.mjs --run            # plan + build + run
  node scripts/test-impact.mjs --run --browser  # include browser suite classes
  node scripts/test-impact.mjs --base origin/master --run
  node scripts/test-impact.mjs --files src/QuickShare/Features/Uploads/X.cs --run
  node scripts/test-impact.mjs --full --run     # everything (pre-push)
  node scripts/test-impact.mjs -v               # why each class was selected

Exit code is dotnet test's: 0 pass, non-zero fail.`);
}

// ── config ──────────────────────────────────────────────────────────────

function loadConfig() {
  const raw = JSON.parse(readFileSync(CONFIG_PATH, 'utf8'));
  return {
    ignorePaths: raw.ignorePaths ?? [],
    ignoreSymbols: new Set(raw.ignoreSymbols ?? []),
    rules: raw.rules ?? [],
    always: raw.always ?? [],
    alwaysForAddedFiles: raw.alwaysForAddedFiles ?? [],
  };
}

// ── git ─────────────────────────────────────────────────────────────────

function git(args) {
  return execFileSync('git', args, { cwd: ROOT, encoding: 'utf8' });
}

function collectChangedFiles(args, config) {
  if (args.files) {
    return args.files
      .map((f) => ({ path: toPosix(f), status: 'M' }))
      .filter((f) => !isIgnored(f.path, config));
  }

  const set = new Map(); // path -> status letter (A/R/M/D)

  if (args.base) {
    for (const line of git(['diff', '--name-status', `${args.base}...HEAD`]).split('\n')) {
      const [status, ...rest] = line.trim().split('\t');
      if (!status || rest.length === 0) continue;
      set.set(toPosix(rest[rest.length - 1]), status[0]);
    }
  } else {
    // --porcelain is the only form that includes untracked files, which is
    // exactly the case a "new endpoint" rule exists for.
    for (const line of git(['status', '--porcelain']).split('\n')) {
      if (!line.trim()) continue;
      const status = line[0] === '?' ? 'A' : line[0];
      const rest = line.slice(3);
      const name = toPosix(rest.includes(' -> ') ? rest.split(' -> ')[1] : rest);
      set.set(name, status === '?' ? 'A' : status);
    }
  }

  return [...set.entries()]
    .map(([p, s]) => ({ path: p, status: s }))
    .filter((f) => !isIgnored(f.path, config) && f.status !== 'D');
}

function toPosix(p) {
  return p.replace(/\\/g, '/');
}

function isIgnored(file, config) {
  return config.ignorePaths.some((glob) => globMatch(file, glob));
}

function globMatch(file, glob) {
  if (glob.endsWith('/**')) return file.startsWith(glob.slice(0, -2));
  if (glob.startsWith('*.')) return file.endsWith(glob.slice(1));
  return file === glob || file.startsWith(glob);
}

// ── test source index ───────────────────────────────────────────────────

function buildIndex() {
  const index = { classes: new Map(), byProject: new Map(), routes: new Map(), files: [] };
  for (const [project, dir] of Object.entries(TEST_DIRS)) {
    for (const file of walk(dir)) {
      if (!file.endsWith('.cs')) continue;
      const text = readFileSync(path.join(ROOT, file), 'utf8');
      const classes = extractClasses(text);
      const routes = extractRoutes(text);
      index.files.push({ project, file, classes, routes });
      for (const c of classes) {
        index.classes.set(c, { project, file });
        index.byProject.set(c, project);
      }
      for (const r of routes) {
        if (!index.routes.has(r)) index.routes.set(r, new Set());
        for (const c of classes) index.routes.get(r).add(c);
      }
    }
  }
  return index;
}

function extractClasses(text) {
  // Only top-level test classes matter; nested helper classes are not filters.
  const out = [];
  for (const line of text.split('\n')) {
    if (!/^\s*(?:public|internal)\s/.test(line)) continue;
    const m = CLASS_DECL.exec(line);
    if (m && m[1].endsWith('Tests')) out.push(m[1]);
  }
  return out;
}

function extractRoutes(text) {
  const out = new Set();
  for (const m of text.matchAll(ROUTE_LITERAL)) {
    const route = normalizeRoute(m[1]);
    if (route.split('/').length >= 3) out.add(route);
  }
  return out;
}

/** `/api/uploads/start-r2?x` -> `/api/uploads`; `/d/{code}` -> `/d`. */
function normalizeRoute(route) {
  const clean = route.split('?')[0];
  const parts = clean.split('/').filter(Boolean);
  const keep = parts[0] === 'api' ? 2 : 1;
  const trimmed = parts.slice(0, Math.max(keep, parts.length > keep ? keep : keep));
  return '/' + trimmed.join('/');
}

function walk(relDir) {
  const out = [];
  const abs = path.join(ROOT, relDir);
  const visit = (dir, rel) => {
    let entries;
    try {
      entries = readdirSync(dir);
    } catch {
      return;
    }
    for (const e of entries) {
      if (SKIP_DIRS.has(e)) continue;
      const absChild = path.join(dir, e);
      const relChild = `${rel}/${e}`;
      if (statSync(absChild).isDirectory()) visit(absChild, relChild);
      else out.push(relChild);
    }
  };
  visit(abs, relDir);
  return out;
}

// ── selection ───────────────────────────────────────────────────────────

function buildPlan(changed, config, index) {
  const selected = { server: new Map(), browser: new Map() }; // class -> reason
  const unmapped = [];

  const add = (project, cls, reason) => {
    const where = index.byProject.get(cls);
    if (where === undefined) {
      throw new Error(
        `Config/adapter named test class "${cls}" which no test file declares. ` +
          `Fix scripts/test-impact.config.json (docs/TESTING.md has drifted before).`,
      );
    }
    if (where === 'browser' && !selected.browser.has(cls)) selected.browser.set(cls, reason);
    else if (where === 'server' && !selected.server.has(cls)) selected.server.set(cls, reason);
  };

  const isTestFile = (p) => p.startsWith(TEST_DIRS.server) || p.startsWith(TEST_DIRS.browser);
  const isSourceFile = (p) =>
    p.startsWith('src/') || p.startsWith('vendor/') || p.startsWith('tests/QuickShare.Server.Tests/E2E/') === false;

  for (const entry of changed) {
    const { path: file } = entry;

    // S4 — a changed test file runs its own classes.
    if (isTestFile(file)) {
      const hit = index.files.find((f) => f.file === file);
      if (hit) for (const c of hit.classes) add(hit.project, c, 'test file changed');
      continue;
    }

    let reached = false;

    // S1 — explicit prefix rules.
    for (const rule of config.rules) {
      if (!rule.paths.some((p) => globMatch(file, p))) continue;
      for (const t of rule.tests ?? []) {
        add('server', t, `rule: ${rule.paths[0]}`);
        reached = true;
      }
      for (const t of rule.browser ?? []) {
        add('browser', t, `rule: ${rule.paths[0]}`);
        reached = true;
      }
    }

    // S5 — permanent cross-cutting rules, plus the added-file rules that exist
    // because "a new service is written and never called" and "a new endpoint
    // no screen uses" are both invisible to symbol matching.
    const invariantRules = entry.status === 'A'
      ? [...config.always, ...config.alwaysForAddedFiles]
      : config.always;
    for (const rule of invariantRules) {
      if (!rule.paths.some((p) => globMatch(file, p))) continue;
      for (const t of rule.tests ?? []) add('server', t, `always: ${rule.note ?? rule.paths[0]}`);
      for (const t of rule.browser ?? []) add('browser', t, `always: ${rule.note ?? rule.paths[0]}`);
      reached = true;
    }

    // S2 — route literals in the changed file.
    const routes = extractRoutesSafe(file);
    for (const route of routes) {
      const hits = index.routes.get(route);
      if (!hits) continue;
      for (const c of hits) {
        add(index.byProject.get(c), c, `route ${route}`);
        reached = true;
      }
    }

    // S3 — declared symbols and file name.
    const symbols = extractSymbolsSafe(file, config);
    for (const sym of symbols) {
      const re = new RegExp(`\\b${escapeRegex(sym)}\\b`);
      for (const t of index.files) {
        if (!re.test(readTestText(t.file))) continue;
        for (const c of t.classes) {
          add(t.project, c, `symbol ${sym}`);
          reached = true;
        }
      }
    }

    if (!reached && (file.startsWith('src/') || file.startsWith('vendor/'))) unmapped.push(file);
  }

  // A source file nobody can explain means we cannot trust a narrow run.
  if (unmapped.length > 0) {
    for (const f of unmapped) {
      console.error(`UNMAPPED: ${f}`);
      console.error('  No rule, route or symbol reaches a test class.');
      console.error('  Add a rule to scripts/test-impact.config.json, then re-run.');
    }
    console.error('\nEscalating to the full server suite (a narrow run would be a guess).');
    return fullPlan(index, { browser: false });
  }

  return finishPlan(selected, index);
}

function fullPlan(index) {
  const selected = { server: new Map(), browser: new Map() };
  for (const [cls, meta] of index.classes) {
    selected[meta.project].set(cls, 'full suite');
  }
  return finishPlan(selected, index);
}

function finishPlan(selected, index) {
  const summary = {
    server: [...selected.server.keys()].sort(),
    browser: [...selected.browser.keys()].sort(),
    reasons: Object.fromEntries([...selected.server, ...selected.browser]),
    totalClasses:
      selected.server.size + selected.browser.size,
    totalTestMethods: 0,
  };
  return { selected, summary, index };
}

function readTestText(file) {
  if (!_testTextCache.has(file)) {
    _testTextCache.set(file, readFileSync(path.join(ROOT, file), 'utf8'));
  }
  return _testTextCache.get(file);
}

function extractRoutesSafe(file) {
  try {
    return extractRoutes(readFileSync(path.join(ROOT, file), 'utf8'));
  } catch {
    return [];
  }
}

function extractSymbolsSafe(file, config) {
  const symbols = new Set();
  const base = path.basename(file).replace(/\.(cs|razor|ts|css|js)$/, '');
  if (isUsefulSymbol(base, config)) symbols.add(base);
  let text = '';
  try {
    text = readFileSync(path.join(ROOT, file), 'utf8');
  } catch {
    return symbols;
  }
  for (const m of text.matchAll(TYPE_DECL)) {
    if (isUsefulSymbol(m[1], config)) symbols.add(m[1]);
  }
  return symbols;
}

function isUsefulSymbol(name, config) {
  if (name.length < 5) return false;
  if (config.ignoreSymbols.has(name)) return false;
  return true;
}

function escapeRegex(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function assertConfigClassesExist(config, index) {
  const declared = (cls) => index.byProject.has(cls);
  const bad = [];
  for (const rule of [...config.rules, ...config.always, ...config.alwaysForAddedFiles]) {
    for (const t of [...(rule.tests ?? []), ...(rule.browser ?? [])]) {
      if (!declared(t)) bad.push(t);
    }
  }
  if (bad.length > 0) {
    console.error('Config references test classes that do not exist:');
    for (const b of [...new Set(bad)]) console.error(`  - ${b}`);
    console.error('Fix scripts/test-impact.config.json — a filter naming a dead class runs 0 tests silently.');
    process.exit(2);
  }
}

// ── output ──────────────────────────────────────────────────────────────

function printPlan(plan, args, changed) {
  const { summary } = plan;
  console.log(`Changed files: ${changed.length}`);
  if (args.verbose) {
    for (const c of changed) console.log(`  ${c.path}`);
  }
  console.log('');
  console.log(`Server classes: ${summary.server.length}${summary.server.length ? '' : ' (none)'}`);
  for (const c of summary.server) {
    console.log(args.verbose ? `  ${c}  <- ${summary.reasons[c]}` : `  ${c}`);
  }
  if (args.browser || summary.browser.length) {
    console.log(`Browser classes: ${summary.browser.length}`);
    for (const c of summary.browser) {
      console.log(args.verbose ? `  ${c}  <- ${summary.reasons[c]}` : `  ${c}`);
    }
  }
  if (summary.server.length > args.max) {
    console.log(
      `\nWARNING: ${summary.server.length} server classes selected (> ${args.max}). ` +
        'That is close to the full suite; prefer --full for honesty.',
    );
  }
  console.log('');
  console.log(`Filter: ${serverFilter(summary)}`);
}

function serverFilter(summary) {
  if (summary.server.length === 0) return '(no server tests)';
  return summary.server.map((c) => `FullyQualifiedName~${c}`).join('|');
}

function browserFilter(summary) {
  if (summary.browser.length === 0) return '(no browser tests)';
  return summary.browser.map((c) => `FullyQualifiedName~${c}`).join('|');
}

// ── execution ───────────────────────────────────────────────────────────

function runPlan(plan, args) {
  const { summary } = plan;
  const projects = [];
  if (summary.server.length > 0) projects.push(['server', summary]);
  if (args.browser && summary.browser.length > 0) projects.push(['browser', summary]);

  if (projects.length === 0) {
    console.log('Nothing selected — no tests are reachable from this diff.');
    return 0;
  }

  let worst = 0;
  for (const [project, s] of projects) {
    const projPath = `${TEST_DIRS[project]}/QuickShare.${project === 'server' ? 'Server' : 'Browser'}.Tests.csproj`;
    if (!args.noBuild) {
      console.log(`\n> dotnet build ${projPath}`);
      const buildCode = run('dotnet', ['build', projPath, '-v', 'q', '--nologo'], ROOT);
      if (buildCode !== 0) return buildCode;
    }
    const filter = project === 'server' ? serverFilter(s) : browserFilter(s);
    console.log(`\n> dotnet test ${projPath} --no-build --filter "<${project} selection>"`);
    const code = run(
      'dotnet',
      ['test', projPath, '--no-build', '--nologo', '--filter', filter],
      ROOT,
    );
    worst = Math.max(worst, code);
  }
  return worst;
}

function run(cmd, cmdArgs, cwd) {
  try {
    execFileSync(cmd, cmdArgs, { cwd, stdio: 'inherit' });
    return 0;
  } catch (err) {
    return typeof err.status === 'number' ? err.status : 1;
  }
}

main();
