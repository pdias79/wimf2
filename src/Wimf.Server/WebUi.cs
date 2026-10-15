using Microsoft.AspNetCore.Http;

namespace Wimf.Server;

/// <summary>Self-contained UI: no CDN, no runtime asset directory and no build-time JS pipeline.</summary>
public static class WebUi
{
    public const string Html = """
<!doctype html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="color-scheme" content="light dark">
  <title>Wimf 2 — Recherche</title>
  <style>
    :root { font-family: system-ui, -apple-system, Segoe UI, sans-serif; color-scheme: light dark; }
    body { margin: 0; background: Canvas; color: CanvasText; }
    main { max-width: 1100px; margin: auto; padding: 28px 22px 48px; }
    header { display: flex; justify-content: space-between; align-items: baseline; gap: 1rem; }
    h1 { margin: 0; font-size: 1.75rem; }
    .sub { color: GrayText; margin: 6px 0 22px; }
    .panel { border: 1px solid color-mix(in srgb, CanvasText 20%, Canvas);
             border-radius: 12px; padding: 20px; margin-bottom: 18px; }
    form { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 12px; }
    input, select, button { font: inherit; padding: 10px 12px; border-radius: 8px; }
    input, select { background: Canvas; color: CanvasText;
                    border: 1px solid color-mix(in srgb, CanvasText 32%, Canvas); min-width: 0; }
    button { border: none; background: #155bbc; color: white; font-weight: 600; cursor: pointer; }
    button:disabled { opacity: 0.55; cursor: wait; }
    .options { margin-top: 12px; display: flex; gap: 12px; flex-wrap: wrap; align-items: center; }
    .options label { display: flex; align-items: center; gap: 5px; }
    .options input[type=checkbox] { width: auto; }
    .options input[type=number] { width: 110px; }
    .options input[type=date] { width: 148px; }
    .advanced { border-top: 1px solid color-mix(in srgb, CanvasText 15%, Canvas); padding-top: 12px; }
    .advanced label { font-size: .9rem; }
    .status { font-size: .9rem; color: GrayText; }
    .status.error { color: #ba3636; }
    .bar { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .table-wrap { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; font-size: .92rem; }
    th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, CanvasText 15%, Canvas); }
    td.path { overflow-wrap: anywhere; max-width: 520px; }
    td.name { font-weight: 600; }
    th { color: GrayText; font-size: .82rem; }
    tbody tr:hover { background: color-mix(in srgb, CanvasText 6%, Canvas); }
    tbody tr:nth-child(even) { background: color-mix(in srgb, CanvasText 3%, Canvas); }
    .result-tools { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; margin: 8px 0 12px; }
    .result-tools label { font-size: .9rem; color: GrayText; }
    .result-tools select { padding: 6px 10px; }
    .pager { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; margin-top: 14px; }
    .pager button { padding: 7px 12px; }
    .pager select { padding: 7px 10px; }
    .empty { padding: 28px 0; color: GrayText; text-align: center; }
    .copy { font-size: .8rem; padding: 6px 9px; white-space: nowrap; background: #424f62; }
    .file-actions { display: flex; align-items: center; flex-wrap: wrap; gap: 5px; min-width: 240px; }
    .file-actions button { font-size: .78rem; }
    .catalog-list { display: grid; gap: 8px; margin-top: 12px; }
    .catalog-item { display: flex; align-items: flex-start; gap: 10px; overflow-wrap: anywhere; }
    .catalog-item input { margin-top: 4px; flex-shrink: 0; }
    .catalog-details { font-size: .84rem; color: GrayText; }
    .catalog-controls { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
    .muted { color: GrayText; font-weight: 400; }
    @media (max-width: 640px) {
      main { padding: 20px 12px; }
      .panel { padding: 14px; }
      form { grid-template-columns: 1fr; }
      .bar { align-items: flex-start; flex-direction: column; }
    }
  </style>
</head>
<body>
<main>
  <header><h1>Wimf <span class="muted">2</span></h1><span id="health" class="status" role="status">Connexion…</span></header>
  <p class="sub">Rechercher des fichiers dans les catalogues locaux chargés par le daemon.</p>
  <section class="panel" aria-label="Recherche">
    <form id="search-form">
      <input id="query" type="search" maxlength="1024" placeholder="Nom de fichier ou mots à rechercher" aria-label="Nom du fichier" autofocus>
      <button id="submit" type="submit">Rechercher</button>
    </form>
    <div class="options">
      <label for="extensions">Extensions <input id="extensions" placeholder="pdf, docx" aria-label="Extensions (séparées par virgules)"></label>
      <label for="max-results">Max <input id="max-results" type="number" min="1" max="1000" value="100"></label>
      <label for="regex"><input id="regex" type="checkbox"> Expression régulière</label>
    </div>
    <div class="options advanced" aria-label="Filtres supplémentaires">
      <label for="min-size">Taille min. (octets) <input id="min-size" type="number" min="0" step="1" placeholder="0"></label>
      <label for="max-size">Taille max. (octets) <input id="max-size" type="number" min="0" step="1" placeholder="Illimitée"></label>
      <label for="modified-from">Modifié depuis <input id="modified-from" type="date"></label>
      <label for="modified-to">Modifié jusqu’au <input id="modified-to" type="date"></label>
    </div>
    <details id="catalog-panel" class="advanced">
      <summary>Catalogues sélectionnés pour la recherche</summary>
      <div class="catalog-controls">
        <button id="catalog-select-all" class="copy" type="button">Tout sélectionner</button>
        <button id="catalog-select-none" class="copy" type="button">Tout désélectionner</button>
        <span id="catalog-selection-count" class="status" role="status"></span>
      </div>
      <div id="catalog-list" class="catalog-list" aria-label="Catalogues disponibles"></div>
    </details>
    <p id="status" class="status" role="status" aria-live="polite">Prêt.</p>
  </section>
  <section class="panel" aria-label="Résultats">
    <div class="bar"><h2>Résultats <span id="count" class="muted"></span></h2><div><span id="catalogs" class="status"></span> <button id="refresh" type="button" class="copy">Actualiser les catalogues</button><span id="refresh-time" class="status"></span></div></div>
    <div class="result-tools">
      <label for="result-sort">Trier les résultats affichés :</label>
      <select id="result-sort" aria-label="Tri des résultats">
        <option value="original">Ordre du catalogue</option>
        <option value="name-asc">Nom A → Z</option>
        <option value="name-desc">Nom Z → A</option>
        <option value="size-asc">Taille croissante</option>
        <option value="size-desc">Taille décroissante</option>
        <option value="date-desc">Modification récente</option>
        <option value="date-asc">Modification ancienne</option>
      </select>
      <label for="page-size">Par page :</label>
      <select id="page-size" aria-label="Résultats par page">
        <option value="25">25</option>
        <option value="50" selected>50</option>
        <option value="100">100</option>
      </select>
      <button id="export-csv" type="button" class="copy" disabled>Exporter CSV</button>
      <span class="status">Tri et export des résultats reçus uniquement (maximum 1 000).</span>
    </div>
    <div class="table-wrap">
      <table id="results-table" hidden>
        <thead><tr><th>Fichier</th><th>Chemin</th><th>Taille</th><th>Modifié le</th><th>Catalogue</th><th>Action</th></tr></thead>
        <tbody id="results"></tbody>
      </table>
    </div>
    <div id="pagination" class="pager" hidden aria-label="Pagination des résultats">
      <button id="page-prev" class="copy" type="button">Précédent</button>
      <span id="page-status" class="status" role="status"></span>
      <button id="page-next" class="copy" type="button">Suivant</button>
    </div>
    <p id="empty" class="empty">Lancez une recherche pour afficher les résultats.</p>
  </section>
</main>
<script>
(() => {
  "use strict";
  const byId = id => document.getElementById(id);
  const form = byId("search-form");
  const submit = byId("submit");
  const status = byId("status");
  const tbody = byId("results");
  const table = byId("results-table");
  const empty = byId("empty");
  let activeController;
  let currentHits = [];
  let currentPage = 0;
  let loadedCatalogs = [];
  let catalogSelection = new Set();
  const catalogList = byId("catalog-list");
  const pageSizeControl = byId("page-size");
  const pagination = byId("pagination");
  const previousButton = byId("page-prev");
  const nextButton = byId("page-next");
  const csvButton = byId("export-csv");
  const sortControl = byId("result-sort");
  const nameOrder = new Intl.Collator("fr-FR", { sensitivity: "base", numeric: true });
  const dateFormatter = new Intl.DateTimeFormat("fr-FR", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: "UTC" });
  const refreshButton = byId("refresh");
  const refreshTime = byId("refresh-time");
  const showTime = value => { refreshTime.textContent = value ? " · Dernière actualisation : " + new Date(value).toLocaleString("fr-FR") : ""; };
  function catalogKey(catalog) {
    return catalog.name + "\\u0000" + catalog.rootPath;
  }
  function renderCatalogs(catalogs) {
    const oldKeys = new Set(loadedCatalogs.filter(c => catalogSelection.has(c.index)).map(catalogKey));
    const hadCatalogs = loadedCatalogs.length > 0;
    loadedCatalogs = catalogs;
    catalogSelection = new Set(catalogs.filter(c => !hadCatalogs || oldKeys.has(catalogKey(c))).map(c => c.index));
    catalogList.replaceChildren();
    const fragment = document.createDocumentFragment();
    for (const catalog of catalogs) {
      const label = document.createElement("label");
      label.className = "catalog-item";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = catalogSelection.has(catalog.index);
      checkbox.addEventListener("change", () => {
        if (checkbox.checked) catalogSelection.add(catalog.index);
        else catalogSelection.delete(catalog.index);
        updateCatalogSelectionCount();
      });
      const content = document.createElement("span");
      const title = document.createElement("strong");
      title.textContent = catalog.name;
      const details = document.createElement("div");
      details.className = "catalog-details";
      details.textContent = catalog.rootPath + " · " + catalog.recordCount +
        " entrées · " + catalog.diagnosticCount + " diagnostics";
      content.append(title, details);
      label.append(checkbox, content);
      fragment.appendChild(label);
    }
    catalogList.appendChild(fragment);
    renderCatalogs(catalogs);
    updateCatalogSelectionCount();
  }
  function updateCatalogSelectionCount() {
    byId("catalog-selection-count").textContent = catalogSelection.size +
      " / " + loadedCatalogs.length + " sélectionné(s)";
  }
  byId("catalog-select-all").addEventListener("click", () => {
    catalogSelection = new Set(loadedCatalogs.map(c => c.index));
    renderSelectionCheckboxes();
  });
  byId("catalog-select-none").addEventListener("click", () => {
    catalogSelection.clear();
    renderSelectionCheckboxes();
  });
  function renderSelectionCheckboxes() {
    for (const [position, checkbox] of Array.from(catalogList.querySelectorAll("input[type=checkbox]")).entries())
      checkbox.checked = catalogSelection.has(loadedCatalogs[position].index);
    updateCatalogSelectionCount();
  }
  refreshButton.addEventListener("click", async () => {
    refreshButton.disabled = true;
    message("Actualisation des catalogues en cours…");
    try {
      const response = await fetch("/api/refresh", { method: "POST" });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Erreur d’actualisation.");
      const catalogsResponse = await fetch("/api/catalogs");
      if (!catalogsResponse.ok) throw new Error("Impossible de charger les catalogues actualisés.");
      renderCatalogs(await catalogsResponse.json());
      showTime(data.completedAt);
      message(data.failedSources ? data.failedSources + " source(s) indisponible(s), cache conservé." : "Catalogues actualisés.", Boolean(data.failedSources));
    } catch (error) { message(error.message || "Erreur réseau.", true); }
    finally { refreshButton.disabled = false; }
  });

  function message(text, isError = false) {
    status.textContent = text;
    status.classList.toggle("error", isError);
  }
  function makeCell(row, value, className = "") {
    const cell = document.createElement("td");
    cell.textContent = String(value);
    if (className) cell.className = className;
    row.appendChild(cell);
    return cell;
  }
  function clearResults() {
    tbody.replaceChildren();
    table.hidden = true;
    pagination.hidden = true;
    csvButton.disabled = currentHits.length === 0;
    byId("count").textContent = "";
    empty.hidden = false;
  }
  function sortHits(hits) {
    const mode = sortControl.value;
    if (mode === "original") return hits;
    const sorted = hits.map((hit, index) => ({ hit, index }));
    sorted.sort((a, b) => {
      const first = a.hit;
      const second = b.hit;
      let order = 0;
      switch (mode) {
        case "name-asc": order = nameOrder.compare(first.name, second.name); break;
        case "name-desc": order = nameOrder.compare(second.name, first.name); break;
        case "size-asc": order = first.size === second.size ? 0 : first.size < second.size ? -1 : 1; break;
        case "size-desc": order = first.size === second.size ? 0 : first.size > second.size ? -1 : 1; break;
        case "date-asc": order = first.modifiedAt < second.modifiedAt ? -1 : first.modifiedAt > second.modifiedAt ? 1 : 0; break;
        case "date-desc": order = first.modifiedAt > second.modifiedAt ? -1 : first.modifiedAt < second.modifiedAt ? 1 : 0; break;
      }
      return order || a.index - b.index;
    });
    return sorted.map(item => item.hit);
  }
  function displayResults() {
    clearResults();
    const hits = sortHits(currentHits);
    byId("count").textContent = "(" + hits.length + ")";
    if (hits.length === 0) { empty.textContent = "Aucun résultat."; return; }
    const pageSize = Number(pageSizeControl.value);
    const totalPages = Math.ceil(hits.length / pageSize);
    currentPage = Math.min(currentPage, totalPages - 1);
    const start = currentPage * pageSize;
    const fragment = document.createDocumentFragment();
    for (const hit of hits.slice(start, start + pageSize)) {
      const row = document.createElement("tr");
      makeCell(row, hit.name, "name");
      makeCell(row, hit.fullPath, "path");
      makeCell(row, new Intl.NumberFormat("fr-FR").format(hit.size) + " o");
      makeCell(row, dateFormatter.format(new Date(hit.modifiedAt)));
      makeCell(row, hit.catalog);
      const action = document.createElement("td");
      const actions = document.createElement("div");
      actions.className = "file-actions";
      function actionButton(label, onClick) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "copy";
        button.textContent = label;
        button.setAttribute("aria-label", label + " : " + hit.name);
        button.addEventListener("click", onClick);
        actions.appendChild(button);
      }
      actionButton("Copier le chemin", async () => {
        try { await navigator.clipboard.writeText(hit.fullPath); message("Chemin copié en texte."); }
        catch { message("Impossible de copier le chemin en texte.", true); }
      });
      async function fileAction(actionName, buttonLabel) {
        if (!confirm(buttonLabel + " sur l’ordinateur exécutant Wimf ?\\n" + hit.fullPath)) return;
        try {
          const response = await fetch("/api/file-action", {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ action: actionName, catalogIndex: hit.catalogIndex,
              id: hit.id, fullPath: hit.fullPath })
          });
          const data = await response.json();
          if (!response.ok) throw new Error(data.error || "Action impossible.");
          message(actionName === "copy" ? "Fichier copié : collez-le dans l’Explorateur Windows."
            : "Action demandée au système.");
        } catch (error) { message(error.message || "Action impossible.", true); }
      }
      actionButton("Ouvrir", () => fileAction("open", "Ouvrir ce fichier"));
      actionButton("Dossier", () => fileAction("reveal", "Afficher le dossier"));
      actionButton("Copier le fichier", () => fileAction("copy", "Copier ce fichier"));
      action.appendChild(actions);
      row.appendChild(action);
      fragment.appendChild(row);
    }
    tbody.appendChild(fragment);
    empty.hidden = true;
    table.hidden = false;
    pagination.hidden = totalPages <= 1;
    previousButton.disabled = currentPage === 0;
    nextButton.disabled = currentPage >= totalPages - 1;
    byId("page-status").textContent = "Page " + (currentPage + 1) + " / " + totalPages + " · " + (start + 1) + "–" + Math.min(start + pageSize, hits.length) + " sur " + hits.length;
    csvButton.disabled = false;
  }

  sortControl.addEventListener("change", () => { currentPage = 0; displayResults(); });
  pageSizeControl.addEventListener("change", () => { currentPage = 0; displayResults(); });
  previousButton.addEventListener("click", () => { if (currentPage > 0) { currentPage--; displayResults(); } });
  nextButton.addEventListener("click", () => { if ((currentPage + 1) * Number(pageSizeControl.value) < currentHits.length) { currentPage++; displayResults(); } });

  // Quote every CSV field, double embedded quotes and guard against spreadsheet formula injection.
  function csvField(value) {
    const raw = String(value ?? "");
    const safe = /^\s*[=+@-]/.test(raw) ? "'" + raw : raw;
    return '"' + safe.replaceAll('"', '""') + '"';
  }
  csvButton.addEventListener("click", () => {
    if (currentHits.length === 0) return;
    const headers = ["Catalogue", "ID", "Nom", "Chemin", "Extension", "Taille (octets)", "Modifié (UTC)"];
    const rows = sortHits(currentHits).map(hit => [hit.catalog, hit.id, hit.name, hit.fullPath,
      hit.extension, hit.size, hit.modifiedAt]);
    const csv = "\uFEFF" + [headers, ...rows].map(row => row.map(csvField).join(";")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "wimf-resultats.csv";
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
    message(rows.length + " résultat(s) exporté(s) en CSV.");
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    if (activeController) activeController.abort();
    const controller = new AbortController();
    activeController = controller;
    submit.disabled = true;
    message("Recherche en cours…");
    const extensions = byId("extensions").value.split(",").map(x => x.trim()).filter(Boolean);
    const maxResults = Number(byId("max-results").value);
    const minSizeText = byId("min-size").value.trim();
    const maxSizeText = byId("max-size").value.trim();
    const modifiedFromText = byId("modified-from").value;
    const modifiedToText = byId("modified-to").value;
    const payload = {
      text: byId("query").value, useRegex: byId("regex").checked,
      extensions, maxResults,
      catalogIndices: [...catalogSelection]
    };
    try {
      if (!Number.isInteger(maxResults) || maxResults < 1 || maxResults > 1000)
        throw new Error("Le nombre de résultats doit être compris entre 1 et 1000.");
      function parseSize(value, label) {
        if (!/^\d+$/.test(value)) throw new Error(label + " doit être un entier positif ou nul.");
        const size = Number(value);
        if (!Number.isSafeInteger(size)) throw new Error(label + " dépasse la précision maximale du navigateur.");
        return size;
      }
      if (minSizeText) payload.minSize = parseSize(minSizeText, "La taille minimale");
      if (maxSizeText) payload.maxSize = parseSize(maxSizeText, "La taille maximale");
      if (payload.minSize !== undefined && payload.maxSize !== undefined && payload.minSize > payload.maxSize)
        throw new Error("La taille minimale ne peut pas dépasser la taille maximale.");
      // Catalog timestamps use UTC days; include every file on the selected final date.
      if (modifiedFromText) payload.modifiedFrom = modifiedFromText + "T00:00:00.000Z";
      if (modifiedToText) payload.modifiedTo = modifiedToText + "T23:59:59.999Z";
      if (modifiedFromText && modifiedToText && modifiedFromText > modifiedToText)
        throw new Error("La date de début ne peut pas dépasser la date de fin.");
      const response = await fetch("/api/search", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        signal: controller.signal
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Erreur de recherche.");
      if (activeController !== controller) return;
      currentHits = Array.isArray(data.results) ? data.results : [];
      currentPage = 0;
      displayResults();
      message(data.count + " résultat(s) affiché(s).");
    } catch (error) {
      if (activeController === controller && error.name !== "AbortError") {
        currentHits = [];
        clearResults();
        empty.textContent = "La recherche a échoué.";
        message(error.message || "Erreur réseau.", true);
      }
    } finally {
      if (activeController === controller) submit.disabled = false;
    }
  });

  Promise.all([
    fetch("/api/health").then(r => { if (!r.ok) throw new Error(); return r.json(); }),
    fetch("/api/catalogs").then(r => { if (!r.ok) throw new Error(); return r.json(); }),
    fetch("/api/refresh-status").then(r => { if (!r.ok) throw new Error(); return r.json(); })
  ]).then(([health, catalogs, refreshStatus]) => {
    byId("health").textContent = "Connecté";
    byId("catalogs").textContent = catalogs.length + " catalogue(s) chargé(s)";
    showTime(refreshStatus.lastRefresh);
    if (catalogs.length === 0) message("Aucun catalogue chargé : démarrer wimfd avec --catalog.");
  }).catch(() => {
    byId("health").textContent = "Déconnecté";
    message("Impossible de contacter le daemon.", true);
  });
})();
</script>
</body>
</html>
""";

    public static IResult GetPage() =>
        Results.Content(Html, "text/html; charset=utf-8");
}
