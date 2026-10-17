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
    td.name { font-weight: 600; max-width: 340px; }
    .file-name { display: block; width: 100%; max-width: 340px; padding: 0; border: 0;
                 background: transparent; color: #155bbc; text-align: left;
                 font: inherit; font-weight: 600; text-decoration: underline;
                 overflow-wrap: anywhere; cursor: pointer; }
    .file-name:hover, .file-name:focus-visible { text-decoration-thickness: 2px; }
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
    .skipped-report-actions { display: flex; justify-content: flex-end; margin-top: 12px; }
    .skipped-report-download { display: inline-block; border-radius: 8px; color: white; font-weight: 600; text-decoration: none; cursor: pointer; }
    .skipped-report-download:hover, .skipped-report-download:focus-visible { background: #344052; }
    .file-actions { display: flex; align-items: center; flex-wrap: wrap; gap: 5px; min-width: 240px; }
    .file-actions button { font-size: .78rem; }
    .catalog-list { display: grid; gap: 8px; margin-top: 12px; }
    .catalog-item { display: flex; align-items: flex-start; gap: 10px; overflow-wrap: anywhere; }
    .catalog-item input { margin-top: 4px; flex-shrink: 0; }
    .catalog-details { font-size: .84rem; color: GrayText; }
    .catalog-controls { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
    .saved-searches { display: grid; gap: 8px; margin: 10px 0 14px; }
    .saved-search { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .saved-search .saved-label { flex: 1; min-width: 170px; overflow-wrap: anywhere; }
    .saved-search button { font-size: .8rem; }
    .saved-controls { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
    .header-controls { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
    .indexing-list { display: grid; gap: 10px; margin-top: 12px; }
    .indexing-item { padding: 10px 0; border-top: 1px solid color-mix(in srgb, CanvasText 12%, Canvas); overflow-wrap: anywhere; }
    .indexing-item:first-child { border-top: none; }
    .indexing-item strong { margin-right: 8px; }
    .indexing-item .error { color: #ba3636; }
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
  <header><h1>Wimf <span class="muted">2</span></h1><div class="header-controls"><button id="indexing-toggle" type="button" class="copy" aria-controls="indexing-panel" aria-expanded="false">Catalogues et actualisation</button><span id="health" class="status" role="status">Connexion…</span></div></header>
  <section id="indexing-panel" class="panel" aria-label="Catalogues et actualisation" hidden>
    <div class="bar"><h2>Catalogues et actualisation</h2><button id="refresh" type="button" class="copy">Actualiser les catalogues</button></div>
    <p class="status"><span id="catalogs"></span><span id="refresh-time"></span><span id="refresh-errors" class="status" role="status"></span></p>
    <h3>Indexations planifiées <span id="indexing-summary" class="status" role="status" aria-live="polite"></span></h3>
    <div id="indexing-list" class="indexing-list" aria-label="État des indexations"><span class="status">Chargement des indexations…</span></div>
    <p id="indexing-error" class="status" role="status"></p>
  </section>
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
      <label for="min-size">Taille min. (Ko) <input id="min-size" type="number" min="0" step="1" placeholder="0"></label>
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
    <details id="saved-search-panel" class="advanced">
      <summary>Historique et favoris de recherche</summary>
      <p class="status">Enregistrés uniquement dans ce navigateur, sur cet appareil.</p>
      <div class="saved-controls">
        <button id="save-favorite" type="button" class="copy">Ajouter la recherche aux favoris</button>
        <button id="clear-history" type="button" class="copy">Effacer l’historique</button>
        <span id="saved-status" class="status" role="status"></span>
      </div>
      <h3>Favoris</h3>
      <div id="favorites-list" class="saved-searches"></div>
      <h3>Recherches récentes</h3>
      <div id="history-list" class="saved-searches"></div>
    </details>
    <p id="status" class="status" role="status" aria-live="polite">Prêt.</p>
  </section>
  <section class="panel" aria-label="Résultats">
    <div class="bar"><h2>Résultats <span id="count" class="muted"></span></h2></div>
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
        <thead><tr><th>Fichier</th><th>Taille</th><th>Modifié le</th><th>Catalogue</th><th>Action</th></tr></thead>
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
  const refreshErrors = byId("refresh-errors");
  const indexingPanel = byId("indexing-panel");
  const indexingToggle = byId("indexing-toggle");
  indexingToggle.addEventListener("click", () => {
    indexingPanel.hidden = !indexingPanel.hidden;
    indexingToggle.setAttribute("aria-expanded", String(!indexingPanel.hidden));
    if (!indexingPanel.hidden) pollIndexingStatus();
  });
  const indexingList = byId("indexing-list");
  const indexingSummary = byId("indexing-summary");
  const indexingError = byId("indexing-error");
  let indexingCheckInProgress = false;
  const indexingLabels = {
    pending: "Non démarrée", queued: "En file d’attente", running: "En cours",
    completed: "Terminée", failed: "Échec"
  };
  function renderIndexingStatus(catalogs) {
    const expandedPaths = new Set(Array.from(indexingList.querySelectorAll("details[data-catalog-path]"))
      .filter(details => details.open).map(details => details.dataset.catalogPath));
    indexingList.replaceChildren();
    indexingError.textContent = "";
    const active = catalogs.filter(c => c.state === "running").length;
    const errors = catalogs.filter(c => c.state === "failed").length;
    indexingSummary.textContent = catalogs.length + " catalogue(s)" +
      (active ? " · " + active + " en cours" : "") +
      (errors ? " · " + errors + " en échec" : "");
    if (catalogs.length === 0) {
      const note = document.createElement("p");
      note.className = "status";
      note.textContent = "Aucun catalogue configuré dans wimf.json.";
      indexingList.appendChild(note);
      return;
    }
    const fragment = document.createDocumentFragment();
    const formatDate = date => date ? new Date(date).toLocaleString("fr-FR") : "Jamais";
    for (const catalog of catalogs) {
      const item = document.createElement("div");
      item.className = "indexing-item";
      const heading = document.createElement("div");
      const name = document.createElement("strong");
      name.textContent = catalog.name || catalog.path || "Catalogue";
      const state = document.createElement("span");
      state.className = catalog.state === "failed" ? "status error" : "status";
      state.textContent = catalog.indexingJobIndex === null
        ? (catalog.available ? "Chargé" : "Indisponible")
        : (indexingLabels[catalog.state] || "En attente");
      heading.append(name, state);
      const path = document.createElement("div");
      path.className = "catalog-details";
      path.textContent = (catalog.root || "Racine inconnue") + " → " + catalog.path;
      const metadata = document.createElement("div");
      metadata.className = "catalog-details";
      metadata.textContent = "Dernière actualisation : " + formatDate(catalog.lastUpdate) +
        " · Fichiers : " + (catalog.fileCount === null ? "—" : catalog.fileCount.toLocaleString("fr-FR"));
      item.append(heading, path, metadata);
      if (Array.isArray(catalog.effectiveExclusions) && catalog.effectiveExclusions.length) {
        const exclusions = document.createElement("details");
        exclusions.className = "catalog-details";
        const summary = document.createElement("summary");
        summary.textContent = "Répertoires et fichiers exclus (" + catalog.effectiveExclusions.length + ")";
        exclusions.appendChild(summary);
        const excludedList = document.createElement("ul");
        for (const excludedPath of catalog.effectiveExclusions) {
          const entry = document.createElement("li");
          entry.textContent = excludedPath;
          excludedList.appendChild(entry);
        }
        exclusions.appendChild(excludedList);
        item.appendChild(exclusions);
      }
      if (catalog.state === "running") {
        const progress = document.createElement("div");
        progress.className = "catalog-details";
        const elapsed = catalog.startedAt
          ? Math.max(0, Math.floor((Date.now() - new Date(catalog.startedAt).getTime()) / 1000))
          : 0;
        progress.textContent = "Progression : " + Number(catalog.scannedEntries || 0).toLocaleString("fr-FR") +
          " éléments parcourus · " + Math.floor(elapsed / 60) + " min " + (elapsed % 60) + " s";
        item.appendChild(progress);
      }
      if ((catalog.state === "running" || catalog.state === "completed") && Number(catalog.skippedEntries) > 0) {
        const skipped = document.createElement("div");
        skipped.className = "catalog-details";
        skipped.textContent = "Éléments ignorés (inaccessibles ou non pris en charge) : " +
          Number(catalog.skippedEntries).toLocaleString("fr-FR");
        item.appendChild(skipped);
        if (Array.isArray(catalog.skippedDetails) && catalog.skippedDetails.length) {
          const details = document.createElement("details");
          details.dataset.catalogPath = catalog.path;
          details.open = expandedPaths.has(catalog.path);
          const heading = document.createElement("summary");
          heading.textContent = "Voir les éléments ignorés (" + catalog.skippedDetails.length +
            " premiers sur " + catalog.skippedEntries + ")";
          details.appendChild(heading);
          const list = document.createElement("ul");
          for (const message of catalog.skippedDetails) {
            const entry = document.createElement("li");
            entry.textContent = message;
            list.appendChild(entry);
          }
          details.appendChild(list);
          if (catalog.state === "completed" && catalog.indexingJobIndex !== null) {
            const actions = document.createElement("div");
            actions.className = "skipped-report-actions";
            const download = document.createElement("a");
            download.className = "copy skipped-report-download";
            download.textContent = "Télécharger le rapport TXT complet";
            download.href = "/api/indexing/" + catalog.indexingJobIndex + "/skipped-report";
            download.download = "wimf2-elements-ignores-" + catalog.indexingJobIndex + ".txt";
            actions.appendChild(download);
            details.appendChild(actions);
          }
          item.appendChild(details);
        }
      }
      if (catalog.state === "completed" && catalog.startedAt && catalog.lastSuccess) {
        const started = new Date(catalog.startedAt).getTime();
        const finished = new Date(catalog.lastSuccess).getTime();
        if (Number.isFinite(started) && Number.isFinite(finished) && finished >= started) {
          const duration = Math.floor((finished - started) / 1000);
          const summary = document.createElement("div");
          summary.className = "catalog-details";
          summary.textContent = "Dernière indexation : " +
            Number(catalog.scannedEntries || 0).toLocaleString("fr-FR") +
            " éléments parcourus · " + Math.floor(duration / 60) + " min " + (duration % 60) + " s";
          item.appendChild(summary);
        }
      }
      if (catalog.indexingJobIndex !== null) {
        const schedule = document.createElement("div");
        schedule.className = "catalog-details";
        schedule.textContent = "Prochaine actualisation prévue : " +
          (catalog.state === "running" ? "En cours" : catalog.state === "queued" ? "Indexation demandée, en attente du tour" : formatDate(catalog.nextUpdate));
        const start = document.createElement("button");
        start.type = "button";
        start.className = "copy";
        start.textContent = "Lancer une indexation";
        start.disabled = !catalog.canStart;
        start.setAttribute("aria-label", "Indexer " + catalog.name);
        start.addEventListener("click", async () => {
          start.disabled = true;
          indexingError.textContent = "";
          try {
            if (!confirm("Lancer maintenant l’indexation complète de " + catalog.name + " ?")) return;
            const response = await fetch("/api/indexing/" + catalog.indexingJobIndex + "/start", {
              method: "POST", headers: { "Content-Type": "application/json" }
            });
            if (!response.ok) throw new Error(response.status === 409
              ? "Indexation déjà en cours ou en attente." : "Impossible de lancer l’indexation.");
            indexingError.textContent = "Indexation demandée pour " + catalog.name + ".";
            await pollIndexingStatus();
          } catch (error) {
            indexingError.textContent = error.message || "Erreur réseau.";
            indexingError.classList.add("error");
          } finally { start.disabled = false; }
        });
        item.append(schedule, start);
      }
      if (catalog.lastError) {
        const failure = document.createElement("div");
        failure.className = "status error";
        failure.textContent = "Erreur : " + catalog.lastError;
        item.appendChild(failure);
      }
      fragment.appendChild(item);
    }
    indexingList.appendChild(fragment);
  }
  async function pollIndexingStatus() {
    if (indexingCheckInProgress || document.hidden) return;
    indexingCheckInProgress = true;
    try {
      const response = await fetch("/api/catalog-management", { cache: "no-store" });
      if (!response.ok) throw new Error("État des catalogues indisponible.");
      const catalogs = await response.json();
      if (!Array.isArray(catalogs)) throw new Error("Réponse des catalogues invalide.");
      renderIndexingStatus(catalogs);
    } catch {
      indexingError.textContent = "État des catalogues indisponible : vérifiez la version du serveur.";
      indexingError.classList.add("error");
    } finally { indexingCheckInProgress = false; }
  }
  let lastObservedRefresh = null;
  let refreshCheckInProgress = false;
  let refreshRequestInProgress = false;
  function showRefreshStatus(data) {
    showTime(data.lastRefresh || data.completedAt);
    const failedLocal = Number(data.failedLocalCatalogs) || 0;
    const failedSources = Number(data.failedSources) || 0;
    const errors = [];
    if (failedLocal) errors.push(failedLocal + " catalogue(s) local(aux) non rechargé(s)");
    if (failedSources) errors.push(failedSources + " source(s) réseau indisponible(s)");
    refreshErrors.textContent = errors.length ? " · " + errors.join(" · ") : "";
    refreshErrors.classList.toggle("error", errors.length > 0);
  }
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
    byId("catalogs").textContent = catalogs.length + " catalogue(s) chargé(s)";
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

  const historyKey = "wimf2.history.v2";
  const favoritesKey = "wimf2.favorites.v2";
  const maxSaved = 20;
  function readSaved(key) {
    try {
      const stored = JSON.parse(localStorage.getItem(key) || "[]");
      return Array.isArray(stored) ? stored.filter(entry =>
        entry && typeof entry === "object" &&
        typeof entry.text === "string" && entry.text.length <= 1024 &&
        Array.isArray(entry.catalogKeys) && Array.isArray(entry.extensions)
      ).slice(0, maxSaved) : [];
    } catch { return []; }
  }
  let history = readSaved(historyKey);
  let favorites = readSaved(favoritesKey);
  function persistSaved(key, entries) {
    try {
      localStorage.setItem(key, JSON.stringify(entries));
      return true;
    } catch {
      byId("saved-status").textContent = "Stockage local indisponible : modifications non conservées.";
      return false;
    }
  }
  function captureSearch() {
    return {
      text: byId("query").value,
      regex: byId("regex").checked,
      extensions: byId("extensions").value.split(",").map(x => x.trim()).filter(Boolean),
      maxResults: byId("max-results").value,
      minSizeKo: byId("min-size").value,
      modifiedFrom: byId("modified-from").value,
      modifiedTo: byId("modified-to").value,
      catalogKeys: loadedCatalogs.filter(c => catalogSelection.has(c.index)).map(catalogKey)
    };
  }
  function searchIdentity(entry) {
    return JSON.stringify(entry);
  }
  function searchTitle(entry) {
    const description = entry.text.trim() || "(tous les fichiers)";
    return description + (entry.extensions.length ? " · " + entry.extensions.join(", ") : "") +
      (entry.regex ? " · regex" : "");
  }
  function restoreSearch(entry) {
    byId("query").value = entry.text;
    byId("regex").checked = Boolean(entry.regex);
    byId("extensions").value = entry.extensions.join(", ");
    byId("max-results").value = entry.maxResults;
    byId("min-size").value = entry.minSizeKo || "";
    byId("modified-from").value = entry.modifiedFrom || "";
    byId("modified-to").value = entry.modifiedTo || "";
    const keys = new Set(entry.catalogKeys);
    catalogSelection = new Set(loadedCatalogs.filter(c => keys.has(catalogKey(c))).map(c => c.index));
    renderSelectionCheckboxes();
    byId("saved-status").textContent = "Critères restaurés. Lancez la recherche pour les appliquer.";
    byId("query").focus();
  }
  function renderSavedList(containerId, entries, isFavorite) {
    const container = byId(containerId);
    container.replaceChildren();
    if (!entries.length) {
      const placeholder = document.createElement("span");
      placeholder.className = "status";
      placeholder.textContent = isFavorite ? "Aucun favori." : "Aucune recherche récente.";
      container.appendChild(placeholder);
      return;
    }
    for (const entry of entries) {
      const row = document.createElement("div");
      row.className = "saved-search";
      const label = document.createElement("span");
      label.className = "saved-label";
      label.textContent = searchTitle(entry);
      label.title = searchTitle(entry);
      const restore = document.createElement("button");
      restore.type = "button";
      restore.className = "copy";
      restore.textContent = "Restaurer";
      restore.addEventListener("click", () => restoreSearch(entry));
      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "copy";
      remove.textContent = "Supprimer";
      remove.setAttribute("aria-label", "Supprimer " + searchTitle(entry));
      remove.addEventListener("click", () => {
        const list = isFavorite ? favorites : history;
        const updated = list.filter(item => searchIdentity(item) !== searchIdentity(entry));
        if (isFavorite) { favorites = updated; persistSaved(favoritesKey, favorites); }
        else { history = updated; persistSaved(historyKey, history); }
        renderSavedSearches();
      });
      row.append(label, restore, remove);
      container.appendChild(row);
    }
  }
  function renderSavedSearches() {
    renderSavedList("favorites-list", favorites, true);
    renderSavedList("history-list", history, false);
  }
  function addToHistory(entry) {
    history = [entry, ...history.filter(item => searchIdentity(item) !== searchIdentity(entry))].slice(0, maxSaved);
    persistSaved(historyKey, history);
    renderSavedSearches();
  }
  byId("save-favorite").addEventListener("click", () => {
    const entry = captureSearch();
    if (favorites.some(item => searchIdentity(item) === searchIdentity(entry))) {
      byId("saved-status").textContent = "Cette recherche figure déjà dans les favoris.";
      return;
    }
    favorites = [entry, ...favorites].slice(0, maxSaved);
    if (persistSaved(favoritesKey, favorites))
      byId("saved-status").textContent = "Recherche ajoutée aux favoris.";
    renderSavedSearches();
  });
  byId("clear-history").addEventListener("click", () => {
    if (!history.length || !confirm("Effacer tout l’historique de recherche local ?")) return;
    history = [];
    persistSaved(historyKey, history);
    renderSavedSearches();
  });
  renderSavedSearches();

  refreshButton.addEventListener("click", async () => {
    refreshButton.disabled = true;
    refreshRequestInProgress = true;
    message("Actualisation des catalogues en cours…");
    try {
      const response = await fetch("/api/refresh", { method: "POST" });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Erreur d’actualisation.");
      const catalogsResponse = await fetch("/api/catalogs");
      if (!catalogsResponse.ok) throw new Error("Impossible de charger les catalogues actualisés.");
      renderCatalogs(await catalogsResponse.json());
      showRefreshStatus(data);
      lastObservedRefresh = data.completedAt;
      const failures = (data.failedLocalCatalogs || 0) + (data.failedSources || 0);
      message(failures ? "Actualisation partielle : certains catalogues précédents ont été conservés." : "Catalogues actualisés.", Boolean(failures));
    } catch (error) { message(error.message || "Erreur réseau.", true); }
    finally { refreshRequestInProgress = false; refreshButton.disabled = false; }
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
  const byteFormatter = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 1 });
  const exactByteFormatter = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 0 });
  function formatSize(bytes) {
    if (!Number.isFinite(bytes) || bytes < 0) return "—";
    const units = ["o", "Ko", "Mo", "Go", "To", "Po", "Eo"];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    return byteFormatter.format(value) + " " + units[unit];
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
      const nameCell = document.createElement("td");
      nameCell.className = "name";
      const fileName = document.createElement("button");
      fileName.type = "button";
      fileName.className = "file-name";
      fileName.textContent = hit.name;
      fileName.title = hit.fullPath;
      fileName.setAttribute("aria-label", "Ouvrir " + hit.name + " — " + hit.fullPath);
      fileName.addEventListener("click", () => fileAction("open", "Ouvrir ce fichier"));
      nameCell.appendChild(fileName);
      row.appendChild(nameCell);
      const sizeCell = makeCell(row, formatSize(hit.size));
      sizeCell.title = exactByteFormatter.format(hit.size) + " octets";
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
        if (actionName !== "open" && !confirm(buttonLabel + " sur l’ordinateur exécutant Wimf ?\\n" + hit.fullPath)) return;
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
      function parseMinSizeKo(value) {
        if (!/^\d+$/.test(value)) throw new Error("La taille minimale doit être un entier positif ou nul en Ko.");
        const kilobytes = Number(value);
        const bytes = kilobytes * 1024;
        if (!Number.isSafeInteger(bytes))
          throw new Error("La taille minimale dépasse la précision maximale du navigateur.");
        return bytes;
      }
      if (minSizeText) payload.minSize = parseMinSizeKo(minSizeText);
      // Catalog timestamps use UTC days; include every file on the selected final date.
      if (modifiedFromText) payload.modifiedFrom = modifiedFromText + "T00:00:00.000Z";
      if (modifiedToText) payload.modifiedTo = modifiedToText + "T23:59:59.999Z";
      if (modifiedFromText && modifiedToText && modifiedFromText > modifiedToText)
        throw new Error("La date de début ne peut pas dépasser la date de fin.");
      const submittedSearch = captureSearch();
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
      addToHistory(submittedSearch);
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

  // Observe automatic refreshes without interrupting an active search.
  async function pollRefreshStatus() {
    if (refreshCheckInProgress || refreshRequestInProgress || document.hidden) return;
    refreshCheckInProgress = true;
    try {
      const response = await fetch("/api/refresh-status", { cache: "no-store" });
      if (!response.ok) throw new Error("État du rafraîchissement indisponible.");
      const latest = await response.json();
      if (latest.lastRefresh !== lastObservedRefresh) {
        const catalogsResponse = await fetch("/api/catalogs", { cache: "no-store" });
        if (!catalogsResponse.ok) throw new Error("Liste des catalogues indisponible.");
        renderCatalogs(await catalogsResponse.json());
        lastObservedRefresh = latest.lastRefresh;
      }
      showRefreshStatus(latest);
    } catch {
      refreshErrors.textContent = " · État du rafraîchissement indisponible";
      refreshErrors.classList.add("error");
    } finally {
      refreshCheckInProgress = false;
    }
  }
  setInterval(pollRefreshStatus, 15000);
  setInterval(pollIndexingStatus, 2000);
  pollIndexingStatus();

  Promise.all([
    fetch("/api/health").then(r => { if (!r.ok) throw new Error(); return r.json(); }),
    fetch("/api/catalogs").then(r => { if (!r.ok) throw new Error(); return r.json(); }),
    fetch("/api/refresh-status").then(r => { if (!r.ok) throw new Error(); return r.json(); })
  ]).then(([health, catalogs, refreshStatus]) => {
    byId("health").textContent = "Connecté";
    renderCatalogs(catalogs);
    showRefreshStatus(refreshStatus);
    lastObservedRefresh = refreshStatus.lastRefresh;
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
