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
    .options input[type=number] { width: 90px; }
    .status { font-size: .9rem; color: GrayText; }
    .status.error { color: #ba3636; }
    .bar { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .table-wrap { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; font-size: .92rem; }
    th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, CanvasText 15%, Canvas); }
    td.path { overflow-wrap: anywhere; max-width: 520px; }
    td.name { font-weight: 600; }
    th { color: GrayText; font-size: .82rem; }
    .empty { padding: 28px 0; color: GrayText; text-align: center; }
    .copy { font-size: .8rem; padding: 6px 9px; white-space: nowrap; background: #424f62; }
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
    <p id="status" class="status" role="status" aria-live="polite">Prêt.</p>
  </section>
  <section class="panel" aria-label="Résultats">
    <div class="bar"><h2>Résultats <span id="count" class="muted"></span></h2><span id="catalogs" class="status"></span></div>
    <div class="table-wrap">
      <table id="results-table" hidden>
        <thead><tr><th>Fichier</th><th>Chemin</th><th>Taille</th><th>Catalogue</th><th>Action</th></tr></thead>
        <tbody id="results"></tbody>
      </table>
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
    byId("count").textContent = "";
    empty.hidden = false;
  }
  function displayResults(data) {
    clearResults();
    const hits = Array.isArray(data.results) ? data.results : [];
    byId("count").textContent = "(" + hits.length + ")";
    if (hits.length === 0) { empty.textContent = "Aucun résultat."; return; }
    const fragment = document.createDocumentFragment();
    for (const hit of hits) {
      const row = document.createElement("tr");
      makeCell(row, hit.name, "name");
      makeCell(row, hit.fullPath, "path");
      makeCell(row, new Intl.NumberFormat("fr-FR").format(hit.size) + " o");
      makeCell(row, hit.catalog);
      const action = document.createElement("td");
      const copy = document.createElement("button");
      copy.type = "button";
      copy.className = "copy";
      copy.textContent = "Copier";
      copy.setAttribute("aria-label", "Copier le chemin de " + hit.name);
      copy.addEventListener("click", async () => {
        try { await navigator.clipboard.writeText(hit.fullPath); message("Chemin copié."); }
        catch { message("Impossible de copier automatiquement le chemin.", true); }
      });
      action.appendChild(copy);
      row.appendChild(action);
      fragment.appendChild(row);
    }
    tbody.appendChild(fragment);
    empty.hidden = true;
    table.hidden = false;
  }

  form.addEventListener("submit", async event => {
    event.preventDefault();
    if (activeController) activeController.abort();
    activeController = new AbortController();
    submit.disabled = true;
    message("Recherche en cours…");
    const extensions = byId("extensions").value.split(",").map(x => x.trim()).filter(Boolean);
    const maxResults = Number(byId("max-results").value);
    const payload = {
      text: byId("query").value, useRegex: byId("regex").checked,
      extensions, maxResults
    };
    try {
      if (!Number.isInteger(maxResults) || maxResults < 1 || maxResults > 1000)
        throw new Error("Le nombre de résultats doit être compris entre 1 et 1000.");
      const response = await fetch("/api/search", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        signal: activeController.signal
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Erreur de recherche.");
      displayResults(data);
      message(data.count + " résultat(s) affiché(s).");
    } catch (error) {
      if (error.name !== "AbortError") {
        clearResults();
        empty.textContent = "La recherche a échoué.";
        message(error.message || "Erreur réseau.", true);
      }
    } finally {
      submit.disabled = false;
    }
  });

  Promise.all([
    fetch("/api/health").then(r => { if (!r.ok) throw new Error(); return r.json(); }),
    fetch("/api/catalogs").then(r => { if (!r.ok) throw new Error(); return r.json(); })
  ]).then(([health, catalogs]) => {
    byId("health").textContent = "Connecté";
    byId("catalogs").textContent = catalogs.length + " catalogue(s) chargé(s)";
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
