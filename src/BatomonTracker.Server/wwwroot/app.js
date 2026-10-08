const state = { phase: 'all', datasetId: '', scope: 'global', top: [], rounds: [] };
const $ = (id) => document.getElementById(id);

const number = (value) => new Intl.NumberFormat('it-IT').format(value ?? 0);
const percent = (wins, total) => total ? `${((wins / total) * 100).toFixed(1)}%` : 'n/d';
const query = () => {
  const params = new URLSearchParams({ phase: state.phase });
  if (state.datasetId) params.set('datasetId', state.datasetId);
  params.set('scope', state.scope);
  return params.toString();
};
const get = async (path) => {
  const token = $('api-token')?.value.trim();
  const response = await fetch(path, token && state.scope === 'mine'
    ? { headers: { Authorization: `Bearer ${token}` } }
    : undefined);
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json();
};
const escapeHtml = (value) => String(value).replace(/[&<>'"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[char]));

function renderOverview(data) {
  $('dataset-count').textContent = number(data.datasetCount);
  $('snapshot-count').textContent = number(data.snapshotCount);
  $('complete-run-count').textContent = number(data.completeRunCount);
  $('extended-run-count').textContent = number(data.extendedRunCount);
  $('ten-win-count').textContent = `${number(data.tenWinRunCount)} raggiunte a 10 vittorie`;
  $('round-range').textContent = `Round ${data.firstRound ?? '--'} → ${data.lastRound ?? '--'}`;
  $('filter-context').textContent = `${number(data.observedRunCount)} run osservate · vista ${state.scope === 'mine' ? 'personale' : 'globale'} · fase ${data.phase}`;
}

function renderTop(rows) {
  const max = rows[0]?.battleCount || 1;
  $('top-batomon').innerHTML = rows.length ? rows.map((row, index) => `
    <tr>
      <td>${String(index + 1).padStart(2, '0')}</td>
      <td>${escapeHtml(row.speciesId)}</td>
      <td class="bar-cell"><span class="bar" style="width:${Math.max(3, (row.battleCount / max) * 76)}px"></span>${number(row.battleCount)}</td>
      <td>${percent(row.battleWins, row.battleCount)}</td>
      <td>${number(row.runCount)}</td>
      <td>${percent(row.runWins, row.completeRunCount)}</td>
    </tr>`).join('') : '<tr><td colspan="6" class="empty-cell">Nessun dato per questo filtro.</td></tr>';
}

function renderRounds(rows) {
  const max = Math.max(...rows.map((row) => row.battleCount), 1);
  $('round-chart').innerHTML = rows.length ? rows.map((row) => `
    <div class="round-row"><span>R${row.round}</span><span class="round-track"><span class="round-fill" style="width:${Math.max(2, (row.battleCount / max) * 100)}%"></span></span><strong>${percent(row.battleWins, row.battleCount)}</strong></div>`).join('') : '<div class="empty-cell">Nessun round per questo filtro.</div>';
}

function renderDatasets(rows) {
  const select = $('dataset-filter');
  select.innerHTML = '<option value="">Tutti i dataset</option>' + rows.map((row) => `<option value="${escapeHtml(row.datasetId)}">${escapeHtml(row.fileName)}</option>`).join('');
  select.value = state.datasetId;
}

async function loadOverview() {
  $('connection-label').textContent = 'Aggiornamento...';
  try {
    const suffix = query();
    const [overview, top, rounds] = await Promise.all([
      get(`/api/analytics/overview?${suffix}`),
      get(`/api/analytics/top-batomon?${suffix}`),
      get(`/api/analytics/rounds?${suffix}`)
    ]);
    state.top = top.batomon;
    state.rounds = rounds.rounds;
    renderOverview(overview);
    renderTop(state.top);
    renderRounds(state.rounds);
    $('connection-label').textContent = 'API online';
  } catch (error) {
    $('connection-label').textContent = 'API offline';
    $('filter-context').textContent = error.message;
    $('top-batomon').innerHTML = '<tr><td colspan="6" class="empty-cell">Impossibile leggere i dati.</td></tr>';
    $('round-chart').innerHTML = '<div class="empty-cell">Impossibile leggere i dati.</div>';
  }
}

async function analyzeSpecies() {
  const species = $('species-input').value.trim().toLowerCase();
  if (!species) return;
  $('explorer-status').textContent = 'Analisi in corso...';
  $('companions-grid').innerHTML = '';
  try {
    const suffix = query();
    const [summary, companions] = await Promise.all([
      get(`/api/analytics/batomon/${encodeURIComponent(species)}?${suffix}`),
      get(`/api/analytics/batomon/${encodeURIComponent(species)}/companions?${suffix}`)
    ]);
    $('species-summary').innerHTML = `<div class="species-name">${escapeHtml(summary.speciesId)}</div><dl>
      <dt>Battle osservate</dt><dd>${number(summary.battleCount)}</dd>
      <dt>Battle winrate</dt><dd>${percent(summary.battleWins, summary.battleCount)}</dd>
      <dt>Run osservate</dt><dd>${number(summary.runCount)}</dd>
      <dt>Run complete</dt><dd>${number(summary.completedRunCount)}</dd>
      <dt>Run winrate</dt><dd>${percent(summary.runWins, summary.completedRunCount)}</dd>
    </dl>`;
    $('companions-grid').innerHTML = companions.companions.length ? companions.companions.slice(0, 20).map((row) => `<article class="companion-card"><strong>${escapeHtml(row.speciesId)}</strong><small>${number(row.sharedRunCount)} run condivise · ${number(row.completedSharedRunCount)} complete · winrate ${percent(row.sharedRunWins, row.completedSharedRunCount)}</small></article>`).join('') : '<div class="empty-cell">Nessun compagno osservato.</div>';
    $('explorer-status').textContent = `${number(companions.companions.length)} compagni trovati`;
  } catch (error) {
    $('species-summary').innerHTML = '<span class="summary-placeholder">Analisi non disponibile.</span>';
    $('explorer-status').textContent = error.message;
  }
}

async function boot() {
  $('phase-filter').addEventListener('change', (event) => { state.phase = event.target.value; loadOverview(); analyzeSpecies(); });
  $('dataset-filter').addEventListener('change', (event) => { state.datasetId = event.target.value; loadOverview(); analyzeSpecies(); });
  $('scope-filter').addEventListener('change', (event) => { state.scope = event.target.value; $('token-field').hidden = state.scope !== 'mine'; loadOverview(); analyzeSpecies(); });
  $('api-token').value = localStorage.getItem('batomon-api-token') || '';
  $('api-token').addEventListener('change', (event) => localStorage.setItem('batomon-api-token', event.target.value.trim()));
  $('refresh-button').addEventListener('click', loadOverview);
  $('analyze-button').addEventListener('click', analyzeSpecies);
  $('species-input').addEventListener('keydown', (event) => { if (event.key === 'Enter') analyzeSpecies(); });
  try { renderDatasets(await get('/api/analytics/datasets')); } catch { $('connection-label').textContent = 'API offline'; }
  await loadOverview();
  await analyzeSpecies();
}

document.addEventListener('DOMContentLoaded', boot);
