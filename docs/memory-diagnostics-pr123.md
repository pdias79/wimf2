# PR #123 — Diagnostiquer la mémoire après le déchargement

Les catalogues passent de huit à zéro après quinze minutes. Une collecte forcée de diagnostic a réduit le tas managé de ~1224 Mo à ~1,5 Mo, mais le working set Windows est resté élevé (~1942 Mo). L'absence de références fortes persistantes est donc confirmée pour ce scénario, mais la raison du working set restant n'est pas encore démontrée.

Cette PR ajoute aux réponses existantes des API `GET /api/memory-diagnostics` et `POST /api/memory-diagnostics/collect` les métriques :
- `gcFragmentedBytes` : fragmentation du tas managé observée par .NET ;
- `gcTotalAvailableBytes` : enveloppe de mémoire disponible estimée par le runtime ;
- `virtualMemoryBytes` : espace virtuel du processus, **pas** de la RAM ;
- `pagedMemoryBytes` : compteur de mémoire paginée du processus ;
- `nonpagedSystemMemoryBytes` et `pagedSystemMemoryBytes` : allocations de pool système attribuées au processus.

**Prudence :** ne pas additionner ces compteurs ; ils mesurent des espaces différents et ne constituent pas un découpage exhaustif du working set. Cette PR ne prétend pas mesurer directement le *private working set* Windows ni les allocations natives précises. Un diagnostic OS supplémentaire serait nécessaire pour cette distinction.

## Protocole Windows

1. Démarrer le systray sans ouvrir la page ; relever `/api/memory-diagnostics`.
2. Ouvrir l'interface et effectuer une recherche ; relever à nouveau.
3. Après plus de quinze minutes sans nouvelle ouverture de page ou saisie, confirmer `catalogCount == 0`.
4. Effectuer **une seule** collecte diagnostique `POST /api/memory-diagnostics/collect` et comparer avant/après.
5. Refaire un cycle chargement → déchargement, vérifier qu'il n'existe pas de croissance sans borne du heap managé.

Aucune collecte forcée automatique, aucune indexation supplémentaire, aucun changement de cache disque.
