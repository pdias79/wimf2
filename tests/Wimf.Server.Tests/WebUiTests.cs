using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class WebUiTests
{
    [Fact]
    public void BirthdayCakeShownOnOctober28OrWhenForced()
    {
        Assert.True(WebUi.ShowBirthdayCake(new DateTime(2026, 10, 28), false));
        Assert.False(WebUi.ShowBirthdayCake(new DateTime(2026, 10, 27), false));
        Assert.False(WebUi.ShowBirthdayCake(new DateTime(2026, 10, 29), false));
        Assert.True(WebUi.ShowBirthdayCake(new DateTime(2026, 1, 1), true));
        Assert.Contains("Philippe DIAS", WebUi.RenderPage(new DateTime(2026, 10, 28)));
        Assert.Contains("chocolatine", WebUi.RenderPage(new DateTime(2026, 1, 1), true));
        Assert.DoesNotContain("birthday-cake\" title=", WebUi.RenderPage(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void FileActionsHavePointerSizedLeaveTolerance()
    {
        Assert.Contains("Math.max(anchor.width, anchor.height)", WebUi.Html);
        Assert.Contains("document.addEventListener(\"pointermove\", trackPointer)", WebUi.Html);
        Assert.Contains("document.removeEventListener(\"pointermove\", trackPointer)", WebUi.Html);
    }

    [Fact]
    public void WideLayoutKeepsFileNamesOnOneLineAndGroupsCatalogButtons()
    {
        Assert.Contains("max-width: 1600px", WebUi.Html);
        Assert.Contains("text-overflow: ellipsis; white-space: nowrap", WebUi.Html);
        Assert.Contains("class=\"catalog-buttons\"", WebUi.Html);
        Assert.DoesNotContain("transform: translateY(-10px)", WebUi.Html);
        Assert.DoesNotContain(".advanced { border-top:", WebUi.Html);
    }

    [Fact]
    public void RestoringSavedSearchClosesPanelAndSubmitsImmediately()
    {
        Assert.Contains("byId(\"saved-search-panel\").open = false;", WebUi.Html);
        Assert.Contains("form.requestSubmit();", WebUi.Html);
        Assert.DoesNotContain("Critères restaurés. Lancez la recherche", WebUi.Html);
        Assert.Contains("label.classList.remove(\"error\")", WebUi.Html);
    }

    [Fact]
    public void BrandExpansionToastFadeAndSizeColumnArePresent()
    {
        Assert.Contains("Where is my file", WebUi.Html);
        Assert.Contains(".wimf-brand:hover .wimf-meaning", WebUi.Html);
        Assert.Contains("toast-fading", WebUi.Html);
        Assert.Contains("dismissToast();", WebUi.Html);
        Assert.Contains("#results-table th:nth-child(2)", WebUi.Html);
        Assert.Contains("#search-options-panel { margin-top: 14px;", WebUi.Html);
        Assert.DoesNotContain("message(data.count +", WebUi.Html);
        Assert.DoesNotContain("transform: translateY(-10px)", WebUi.Html);
    }

    [Fact]
    public void ResultsUseFileTypeIconsAndContextMenu()
    {
        Assert.Contains("/api/file-type-icon/", WebUi.Html);
        Assert.Contains("className = \"name-with-icon\"", WebUi.Html);
        Assert.Contains("className = \"file-actions-menu\"", WebUi.Html);
        Assert.Contains("actions.addEventListener(\"mouseleave\"", WebUi.Html);
        Assert.Contains("const dot = hit.name.lastIndexOf(\".\")", WebUi.Html);
        Assert.Contains("max=\"10000\"", WebUi.Html);
        Assert.Contains("<th aria-label=\"Actions\"></th>", WebUi.Html);
        Assert.Contains("class=\"panel results-panel\"", WebUi.Html);
        Assert.DoesNotContain("Tri et export des résultats reçus uniquement", WebUi.Html);
        Assert.Contains("if (resultsTruncated)", WebUi.Html);
        Assert.Contains("resultsTruncated = data.truncated === true", WebUi.Html);
    }

    [Fact]
    public void ExtensionSortingAndLocalPagingPreferencesAreAvailable()
    {
        Assert.Contains("extension-asc", WebUi.Html);
        Assert.Contains("extension-desc", WebUi.Html);
        Assert.Contains("first.extension === \"folder\"", WebUi.Html);
        Assert.Contains("wimf2.preferences.", WebUi.Html);
        Assert.Contains("savePreference(\"pageSize\"", WebUi.Html);
        Assert.Contains("savePreference(\"maxResults\"", WebUi.Html);
        Assert.Contains("value=\"1000\"", WebUi.Html);
        Assert.Contains("<option value=\"100\" selected>", WebUi.Html);
        Assert.Contains(".file-actions-menu { position: fixed;", WebUi.Html);
        Assert.Contains("menuToggle.getBoundingClientRect()", WebUi.Html);
    }

    [Fact]
    public void OptionalSeparatorInsensitiveSearchIsIncludedInApiAndSavedSearches()
    {
        Assert.Contains("id=\"ignore-separators\"", WebUi.Html);
        Assert.Contains("ignoreSeparators: byId(\"ignore-separators\").checked", WebUi.Html);
        Assert.Contains("byId(\"ignore-separators\").checked = Boolean(entry.ignoreSeparators)", WebUi.Html);
    }

    [Fact]
    public void DirectoryResultsAndLargePageAreVisible()
    {
        Assert.Contains("<option value=\"500\">500</option>", WebUi.Html);
        Assert.Contains("Ouvrir le dossier", WebUi.Html);
        Assert.Contains("#saved-search-panel { transform: translateY(-.5em); }", WebUi.Html);
        Assert.Contains("isDirectory ? \"Dossier\" : formatSize(hit.size)", WebUi.Html);
    }

    [Fact]
    public void CompactSearchAndBulkIndexControlsArePresent()
    {
        Assert.Contains("id=\"search-options-panel\"", WebUi.Html);
        Assert.Contains("<summary>Options et filtres</summary>", WebUi.Html);
        Assert.Contains("Recharger les catalogues", WebUi.Html);
        Assert.Contains("id=\"reindex-all\"", WebUi.Html);
        Assert.Contains("/api/indexing/start-all", WebUi.Html);
        Assert.DoesNotContain("Prochaine tentative incrémentale : chargement", WebUi.Html);
    }

    [Fact]
    public void FileActionsRespectServerCapabilityAndFolderOpensWithoutConfirmation()
    {
        Assert.Contains("health.fileActionsEnabled === true", WebUi.Html);
        Assert.Contains("if (fileActionsEnabled)", WebUi.Html);
        Assert.DoesNotContain("confirm(buttonLabel", WebUi.Html);
        Assert.Contains("id=\"toast\"", WebUi.Html);
        Assert.DoesNotContain("id=\"health\"", WebUi.Html);
        Assert.DoesNotContain("Rechercher des fichiers dans les catalogues locaux", WebUi.Html);
        Assert.DoesNotContain("Prêt.</p>", WebUi.Html);
    }

    [Fact]
    public void PageHasAccessibleSearchAndCatalogDisplay()
    {
        Assert.Contains("lang=\"fr\"", WebUi.Html);
        Assert.Contains("id=\"search-form\"", WebUi.Html);
        Assert.Contains("id=\"results\"", WebUi.Html);
        Assert.Contains("/api/search", WebUi.Html);
        Assert.Contains("/api/catalogs", WebUi.Html);
        Assert.Contains("/api/health", WebUi.Html);
    }

    [Fact]
    public void ResultsAreAddedAsTextNotHtml()
    {
        Assert.Contains("cell.textContent = String(value)", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiDoesNotRequireExternalAssets()
    {
        Assert.DoesNotContain("https://", WebUi.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src=", WebUi.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void ExposesMinimumSizeInKoAndUtcDateFilters()
    {
        foreach (var id in new[] { "min-size", "modified-from", "modified-to" })
            Assert.Contains("id=" + (char)34 + id + (char)34, WebUi.Html);
        Assert.Contains("Taille min. (Ko)", WebUi.Html);
        Assert.DoesNotContain("id=" + (char)34 + "max-size" + (char)34, WebUi.Html);
        Assert.Contains("const bytes = kilobytes * 1024", WebUi.Html);
        Assert.Contains("Number.isSafeInteger(bytes)", WebUi.Html);
        Assert.Contains("payload.minSize = parseMinSizeKo(minSizeText)", WebUi.Html);
        Assert.DoesNotContain("payload.maxSize", WebUi.Html);
        Assert.Contains("payload.modifiedFrom = modifiedFromText", WebUi.Html);
        Assert.Contains("T00:00:00.000Z", WebUi.Html);
        Assert.Contains("T23:59:59.999Z", WebUi.Html);
        Assert.Contains("modifiedFromText > modifiedToText", WebUi.Html);
    }

    [Fact]
    public void ResultTableOffersLocalStableSortingAndDates()
    {
        Assert.Contains("id=" + (char)34 + "result-sort" + (char)34, WebUi.Html);
        foreach (var mode in new[]
        {
            "original", "name-asc", "name-desc", "size-asc", "size-desc",
            "date-asc", "date-desc", "extension-asc", "extension-desc"
        })
            Assert.Contains("value=" + (char)34 + mode + (char)34, WebUi.Html);
        Assert.Contains("Modifié le", WebUi.Html);
        Assert.Contains("new Intl.DateTimeFormat", WebUi.Html);
        Assert.Contains("timeZone: " + (char)34 + "UTC" + (char)34, WebUi.Html);
        Assert.Contains("nameOrder.compare", WebUi.Html);
        Assert.Contains("order || a.index - b.index", WebUi.Html);
        Assert.Contains("sortControl.addEventListener(" + (char)34 + "change" + (char)34, WebUi.Html);
        Assert.Contains("const hits = sortHits(currentHits)", WebUi.Html);
        Assert.Contains("activeController !== controller", WebUi.Html);
    }

    [Fact]
    public void PaginationAndCsvExportAreLocalAndSafe()
    {
        foreach (var id in new[] { "pagination", "page-prev", "page-next", "page-size", "page-status", "export-csv" })
            Assert.Contains("id=" + (char)34 + id + (char)34, WebUi.Html);
        Assert.Contains("hits.slice(start, start + pageSize)", WebUi.Html);
        Assert.Contains("currentPage = Math.min(currentPage, totalPages - 1)", WebUi.Html);
        Assert.Contains("previousButton.disabled = currentPage === 0", WebUi.Html);
        Assert.Contains("sortHits(currentHits).map(hit =>", WebUi.Html);
        Assert.Contains("function csvField(value)", WebUi.Html);
        Assert.Contains("safe.replaceAll", WebUi.Html);
        Assert.Contains("URL.createObjectURL(new Blob", WebUi.Html);
        Assert.Contains("wimf-resultats.csv", WebUi.Html);
        Assert.Contains("text/csv;charset=utf-8", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResultActionsDistinguishPathTextFromFileClipboard()
    {
        Assert.Contains("Copier le chemin", WebUi.Html);
        Assert.Contains("Copier le fichier", WebUi.Html);
        Assert.Contains("Afficher le dossier", WebUi.Html);
        Assert.Contains("Ouvrir", WebUi.Html);
        Assert.Contains("navigator.clipboard.writeText(hit.fullPath)", WebUi.Html);
        Assert.Contains("fileAction(\"copy\"", WebUi.Html);
        Assert.Contains("fileAction(\"open\"", WebUi.Html);
        Assert.Contains("fileAction(\"reveal\"", WebUi.Html);
        Assert.Contains("/api/file-action", WebUi.Html);
        Assert.Contains("catalogIndex: hit.catalogIndex", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogPanelListsMetadataAndSubmitsSelectedIndices()
    {
        foreach (var id in new[] { "catalog-panel", "catalog-list", "catalog-select-all",
            "catalog-select-none", "catalog-selection-count" })
            Assert.Contains("id=" + (char)34 + id + (char)34, WebUi.Html);
        Assert.Contains("catalog.rootPath", WebUi.Html);
        Assert.Contains("catalog.recordCount", WebUi.Html);
        Assert.Contains("catalog.diagnosticCount", WebUi.Html);
        Assert.Contains("catalogIndices: [...catalogSelection]", WebUi.Html);
        Assert.Contains("renderCatalogs(await catalogsResponse.json())", WebUi.Html);
        Assert.Contains("checkbox.checked = catalogSelection.has", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileNameIsClickableWithFullPathTooltipAndNoPathColumn()
    {
        Assert.Contains("fileName.textContent = hit.name", WebUi.Html);
        Assert.Contains("fileName.title = hit.fullPath", WebUi.Html);
        Assert.Contains("fileName.setAttribute(\"aria-label\"", WebUi.Html);
        Assert.Contains("fileName.addEventListener(\"click\", () => fileAction(\"open\"", WebUi.Html);
        Assert.DoesNotContain("confirm(buttonLabel", WebUi.Html);
        Assert.DoesNotContain("<th>Chemin</th>", WebUi.Html);
        Assert.DoesNotContain("makeCell(row, hit.fullPath, \"path\")", WebUi.Html);
        Assert.Contains("navigator.clipboard.writeText(hit.fullPath)", WebUi.Html);
        Assert.Contains("actionButton(\"Copier le fichier\"", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchResultsShowCompactSizesWithExactByteTooltip()
    {
        Assert.Contains("function formatSize(bytes)", WebUi.Html);
        Assert.Contains("const units = [\"o\", \"Ko\", \"Mo\", \"Go\", \"To\", \"Po\", \"Eo\"]", WebUi.Html);
        Assert.Contains("while (value >= 1024", WebUi.Html);
        Assert.Contains("maximumFractionDigits: 1", WebUi.Html);
        Assert.Contains("makeCell(row, isDirectory ? \"Dossier\" : formatSize(hit.size))", WebUi.Html);
        Assert.Contains("sizeCell.title = exactByteFormatter.format(hit.size) + \" octets\"", WebUi.Html);
        Assert.DoesNotContain("new Intl.NumberFormat(\"fr-FR\").format(hit.size) + \" o\"", WebUi.Html);
    }

    [Fact]
    public void SearchHistoryAndFavoritesStayLocalAndRestoreAllFilters()
    {
        foreach (var id in new[] { "saved-search-panel", "save-favorite", "clear-history",
            "history-list", "favorites-list", "saved-status" })
            Assert.Contains("id=" + (char)34 + id + (char)34, WebUi.Html);
        Assert.Contains("wimf2.history.v2", WebUi.Html);
        Assert.Contains("wimf2.favorites.v2", WebUi.Html);
        Assert.Contains("localStorage.getItem", WebUi.Html);
        Assert.Contains("localStorage.setItem", WebUi.Html);
        Assert.Contains("const maxSaved = 20", WebUi.Html);
        Assert.Contains("function restoreSearch(entry)", WebUi.Html);
        Assert.Contains("byId(\"min-size\").value = entry.minSizeKo", WebUi.Html);
        Assert.Contains("byId(\"modified-from\").value = entry.modifiedFrom", WebUi.Html);
        Assert.Contains("catalogSelection = new Set(loadedCatalogs.filter", WebUi.Html);
        Assert.Contains("const submittedSearch = captureSearch()", WebUi.Html);
        Assert.Contains("addToHistory(submittedSearch)", WebUi.Html);
        Assert.Contains("container.replaceChildren()", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshShowsLocalAndNetworkFailuresAndPollsAutomaticUpdates()
    {
        Assert.Contains("id=\"refresh-errors\"", WebUi.Html);
        Assert.Contains("data.failedLocalCatalogs", WebUi.Html);
        Assert.Contains("data.failedSources", WebUi.Html);
        Assert.Contains("showRefreshStatus(refreshStatus)", WebUi.Html);
        Assert.Contains("showRefreshStatus(data)", WebUi.Html);
        Assert.Contains("async function pollRefreshStatus()", WebUi.Html);
        Assert.Contains("setInterval(pollRefreshStatus, 15000)", WebUi.Html);
        Assert.Contains("latest.lastRefresh !== lastObservedRefresh", WebUi.Html);
        Assert.Contains("renderCatalogs(await catalogsResponse.json())", WebUi.Html);
        Assert.Contains("refreshCheckInProgress || refreshRequestInProgress", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexingPanelPollsJobProgressWithoutReloadingSearchResults()
    {
        Assert.Contains("id=\"indexing-panel\"", WebUi.Html);
        Assert.Contains("id=\"indexing-toggle\"", WebUi.Html);
        Assert.Contains("aria-controls=\"indexing-panel\"", WebUi.Html);
        Assert.Contains("indexingToggle.setAttribute(\"aria-expanded\"", WebUi.Html);
        Assert.Contains("if (!indexingPanel.hidden) { pollIndexingStatus(); pollIndexingHistory(); }", WebUi.Html);
        Assert.Contains("Catalogues et actualisation", WebUi.Html);
        Assert.Contains("id=\"refresh\" type=\"button\"", WebUi.Html);
        Assert.Contains("Recharger les catalogues", WebUi.Html);
        Assert.Contains("refreshButton.addEventListener(\"click\"", WebUi.Html);
        Assert.DoesNotContain("<details id=\"indexing-panel\"", WebUi.Html);
        Assert.Contains("<section class=\"panel results-panel\" aria-label=\"Résultats\">", WebUi.Html);
        Assert.Contains("id=\"indexing-list\"", WebUi.Html);
        Assert.Contains("id=\"indexing-history-panel\"", WebUi.Html);
        Assert.Contains("fetch(\"/api/indexing-history\"", WebUi.Html);
        Assert.Contains("id=\"indexing-summary\"", WebUi.Html);
        Assert.Contains("id=\"indexing-error\"", WebUi.Html);
        Assert.Contains("fetch(\"/api/catalog-management\"", WebUi.Html);
        Assert.Contains("setInterval(pollIndexingStatus, 2000)", WebUi.Html);
        Assert.Contains("catalog.state === \"queued\" && runningCatalog", WebUi.Html);
        Assert.Contains("En attente de la fin de l’indexation", WebUi.Html);
        Assert.Contains("Annuler l’indexation", WebUi.Html);
        Assert.Contains("/cancel", WebUi.Html);
        Assert.Contains("cancelled: \"Annulée\"", WebUi.Html);
        Assert.Contains("catalog.queuedAt", WebUi.Html);
        Assert.Contains("catalog.scanMetrics", WebUi.Html);
        Assert.Contains("Énumération :", WebUi.Html);
        Assert.Contains("Tri :", WebUi.Html);
        Assert.Contains("Temps en file d’attente", WebUi.Html);
        Assert.Contains("Dernière indexation :", WebUi.Html);
        Assert.Contains("catalog.lastSuccess", WebUi.Html);
        Assert.Contains("catalog.skippedEntries", WebUi.Html);
        Assert.Contains("catalog.skippedDetails", WebUi.Html);
        Assert.Contains("catalog.effectiveExclusions", WebUi.Html);
        Assert.Contains("Répertoires et fichiers exclus", WebUi.Html);
        Assert.Contains("expandedPaths.has(catalog.path)", WebUi.Html);
        Assert.Contains("details.dataset.catalogPath = catalog.path", WebUi.Html);
        Assert.Contains("Voir les éléments ignorés", WebUi.Html);
        Assert.Contains("Télécharger le rapport TXT complet", WebUi.Html);
        Assert.Contains("skipped-report-actions", WebUi.Html);
        Assert.Contains("justify-content: flex-end", WebUi.Html);
        Assert.Contains("skipped-report-download", WebUi.Html);
        Assert.Contains("actions.appendChild(download)", WebUi.Html);
        Assert.Contains("/skipped-report", WebUi.Html);
        Assert.Contains("download.download", WebUi.Html);
        Assert.Contains("Éléments ignorés", WebUi.Html);
        Assert.Contains("indexingCheckInProgress || document.hidden", WebUi.Html);
        Assert.DoesNotContain("indexingPanel.hidden = jobs.length === 0", WebUi.Html);
        Assert.Contains("Aucun catalogue configuré", WebUi.Html);
        Assert.Contains("Chargement des indexations…", WebUi.Html);
        Assert.DoesNotContain("id=\"indexing-panel\" class=\"advanced\" hidden", WebUi.Html);
        foreach (var state in new[] { "pending", "running", "completed", "failed" })
            Assert.Contains(state + ":", WebUi.Html);
        Assert.Contains("catalog.lastError", WebUi.Html);
        Assert.Contains("catalog.lastUpdate", WebUi.Html);
        Assert.Contains("indexingList.replaceChildren()", WebUi.Html);
        Assert.Contains("name.textContent = catalog.name", WebUi.Html);
        Assert.Contains("catalog.fileCount", WebUi.Html);
        Assert.Contains("catalog.lastUpdate", WebUi.Html);
        Assert.Contains("catalog.nextUpdate", WebUi.Html);
        Assert.Contains("catalog.indexingJobIndex !== null", WebUi.Html);
        Assert.Contains("Lancer une indexation", WebUi.Html);
        Assert.Contains("method: \"POST\"", WebUi.Html);
        Assert.Contains("\"/api/indexing/\" + catalog.indexingJobIndex + \"/start\"", WebUi.Html);
        Assert.Contains("start.disabled = !catalog.canStart", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexingPanelDisplaysLiveCountAndElapsedTimeWithoutPercentages()
    {
        Assert.Contains("catalog.scannedEntries", WebUi.Html);
        Assert.Contains("catalog.startedAt", WebUi.Html);
        Assert.Contains("Progression :", WebUi.Html);
        Assert.Contains("éléments parcourus", WebUi.Html);
        Assert.Contains("Date.now()", WebUi.Html);
        Assert.DoesNotContain("progress.value", WebUi.Html);
        Assert.DoesNotContain("innerHTML", WebUi.Html, StringComparison.OrdinalIgnoreCase);
    }

}
