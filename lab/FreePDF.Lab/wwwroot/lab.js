'use strict';
const $ = id => document.getElementById(id);
let report, current, mode = 'split', pageIndex = 0, request = 0;
const labels = {match: 'Match', review: 'Review difference', failed: 'Failed'};
const artifact = path => `/artifacts/${encodeURIComponent(report.id)}/${path.split('/').map(encodeURIComponent).join('/')}`;
function node(tag, text, className) { const el = document.createElement(tag); if (text !== undefined) el.textContent = text; if (className) el.className = className; return el; }
async function json(url) { const response = await fetch(url); if (!response.ok) throw new Error(`Could not load evidence (${response.status}).`); return response.json(); }
function fail(error) { $('error').textContent = error.message; $('error').hidden = false; }
async function load(id) {
  const serial = ++request;
  try {
    const result = await json(`/api/runs/${encodeURIComponent(id)}`); if (serial !== request) return; report = result;
    $('error').hidden = true; $('empty').hidden = true; $('evidence').hidden = false; $('cases').replaceChildren();
    const counts = Object.fromEntries(['match','review','failed'].map(s => [s, report.cases.filter(c => c.status === s).length]));
    $('count').textContent = report.cases.length; $('summary').textContent = `${counts.match} match · ${counts.review} review · ${counts.failed} failed`;
    for (const test of report.cases) { const button = node('button'); button.dataset.id = test.id; button.append(node('span', undefined, `dot ${test.status}`), node('span', test.title)); button.title = labels[test.status]; button.setAttribute('aria-label', `${test.title}: ${labels[test.status]}`); button.addEventListener('click', () => select(test.id)); $('cases').append(button); }
    $('provenance').textContent = `Source ${report.sourceRevision.slice(0,12)} · ${report.rendererVersion}`;
    const priority = report.cases.find(c => c.status === 'failed') || report.cases.find(c => c.status === 'review') || report.cases[0];
    select(report.cases.some(c => c.id === current?.id) ? current.id : priority.id);
  } catch (error) { fail(error); }
}
function select(id) {
  current = report.cases.find(c => c.id === id); pageIndex = 0;
  for (const button of $('cases').children) button.setAttribute('aria-current', String(button.dataset.id === id));
  $('title').textContent = current.title; $('status').textContent = labels[current.status]; $('status').className = `badge ${current.status}`;
  const notices = [current.knownReferenceDefect, current.referenceError && `Reference: ${current.referenceError}`, current.candidateError && `FreePDF: ${current.candidateError}`, ...current.structuralErrors].filter(Boolean);
  $('notice').hidden = !notices.length; $('notice').textContent = notices.join(' ');
  $('pages').replaceChildren(...current.pages.map((p,i) => { const option = node('option', `${p.number} / ${current.pages.length}`); option.value = i; return option; }));
  $('pages').disabled = !current.pages.length;
  $('details').textContent = JSON.stringify({structuralErrors: current.structuralErrors, sourceRevision: report.sourceRevision, renderer: report.rendererVersion, rendererSha256: report.rendererSha256, fontSha256: report.fontSha256, referenceCommand: report.referenceCommand, candidateCommand: report.candidateCommand, limitations: 'No independent PDF/A, PDF/UA, cryptographic signature or semantic text validation yet.'}, null, 2);
  $('downloads').replaceChildren();
  for (const engine of ['reference','candidate']) for (const [file,label] of [['document.pdf','PDF'],['structure.json','structure'],['generator.log','log']]) { if (file !== 'generator.log' && (engine === 'reference' ? current.referenceError : current.candidateError)) continue; const link = node('a', `${engine === 'candidate' ? 'FreePDF' : 'Reference'} ${label}`); link.href = artifact(`${current.id}/${engine}/${file}`); $('downloads').append(link); }
  render();
}
function render() {
  for (const button of document.querySelectorAll('[data-mode]')) button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
  const page = current.pages[pageIndex]; $('viewport').replaceChildren(); $('viewport').className = `viewport ${mode === 'split' ? '' : 'single'}`;
  $('overlay-control').hidden = mode !== 'overlay' || !page?.reference || !page?.candidate || page?.pixels?.sameDimensions !== true;
  $('metrics').replaceChildren();
  if (page?.pixels) { const m = page.pixels; for (const [value,label] of [[m.sameDimensions ? `${m.width} × ${m.height}` : 'Different dimensions','pixels'],[m.sameDimensions ? `${m.changedPercent.toFixed(4)}%` : '—','changed'],[m.sameDimensions ? m.rootMeanSquareError.toFixed(3) : '—','RMSE'],[m.sameDimensions ? String(m.maximumDelta) : '—','maximum delta']]) { const item = node('span'); item.append(node('b',value), document.createTextNode(label)); $('metrics').append(item); } }
  else $('metrics').textContent = 'Pixel comparison unavailable for this page.';
  function figure(title, path) { const f = node('figure'); f.append(node('figcaption',title)); const paper = node('div',undefined,'paper'); if (path) { const img = node('img'); img.src = artifact(path); img.alt = `${title}, page ${page.number}`; paper.append(img); } else paper.append(node('div','No rendered page available. See execution details and logs.','missing')); f.append(paper); $('viewport').append(f); return paper; }
  if (mode === 'split') { figure('PDFsharp · MIT Preview1',page?.reference); figure('FreePDF · candidate',page?.candidate); }
  else if (mode === 'difference') figure('Changed pixels in red',page?.difference);
  else { const paper = figure('Reference + FreePDF overlay',page?.reference); if (page?.candidate && page?.reference && page.pixels?.sameDimensions) { const img = node('img',undefined,'overlay-image'); img.src = artifact(page.candidate); img.alt = 'FreePDF overlay'; paper.append(img); opacity(); } else paper.append(node('p','Overlay requires two pages with matching dimensions.','missing')); }
}
function opacity() { $('opacity-value').textContent = `${$('opacity').value}%`; const img = document.querySelector('.overlay-image'); if (img) img.style.opacity = $('opacity').value / 100; }
$('runs').addEventListener('change', () => load($('runs').value)); $('pages').addEventListener('change', () => { pageIndex = Number($('pages').value); render(); });
$('opacity').addEventListener('input', opacity); for (const button of document.querySelectorAll('[data-mode]')) button.addEventListener('click', () => { mode = button.dataset.mode; render(); });
async function refresh() { $('refresh').disabled = true; try { const runs = await json('/api/runs'); $('runs').replaceChildren(...runs.map(r => { const option = node('option', `${new Date(r.started).toLocaleString()} · ${r.profile || ".NET 10"} · ${r.matches}/${r.matches+r.reviews+r.failures} match`); option.value = r.id; return option; })); if (runs.length) { $('runs').disabled = false; await load(runs[0].id); } else { $('runs').append(node('option','No runs')); $('runs').disabled = true; $('empty').hidden = false; $('summary').textContent = 'Waiting for the first recorded run.'; } } catch (error) { fail(error); } finally { $('refresh').disabled = false; } }
$('refresh').addEventListener('click', refresh);
refresh();
