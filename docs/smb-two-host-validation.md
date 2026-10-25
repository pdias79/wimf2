# WIMF2 — protocole SMB à deux postes (validation manuelle)

Ce protocole complète les tests CI à plusieurs processus locaux. **Il ne constitue pas une validation SMB tant qu'il n'a pas été exécuté sur deux machines indépendantes.**

## Préparation

- Deux postes Windows ou Linux (A et B), exécutant la même version de WIMF2, sur un même partage SMB.
- Un catalogue de test dédié et une racine indexée accessible par les deux postes. Ne pas utiliser un catalogue de production.
- Sur chaque poste : journal incrémental **local** et répertoire de cache local, configurés séparément.
- Noter les heures (UTC), la version des binaires, le serveur SMB, le système de fichiers sous-jacent et les messages `[CATALOG-LOCK]`, `[INCREMENTAL-RESET]`, `[INCREMENTAL-ERROR]`.
- Sauvegarder avant chaque scénario la version binaire du catalogue. Comparer les entrées à un inventaire des fichiers existants.

## Scénarios à vérifier

| Cas | Manipulation | Résultat attendu |
| --- | --- | --- |
| Exclusivité | Lancer un scan complet sur A ; demander le même scan sur B pendant que A détient le verrou | Une seule publication ; B est reporté ou échoue explicitement sans écraser A |
| Marqueur ancien | Avec A toujours actif, dater artificiellement le marqueur `.wimf.lock` à plus de 30 minutes ; relancer sur B | B ne prend jamais le verrou OS actif |
| Crash du détenteur | Tuer brutalement A pendant l'indexation ; retenter sur B | Le verrou OS est libéré ; B reprend normalement ; dernier catalogue valide préservé si A n'avait pas publié |
| Déconnexion SMB | Couper le réseau du poste A pendant une publication, puis rétablir | Pas de publication incomplète ; journal non acquitté en cas d'échec ; reprise explicite |
| Deux journaux locaux | Faire publier A puis B sur le même catalogue ; relancer l'incrémentale de A | A détecte la nouvelle version et abandonne ses anciens événements ; aucun scan complet supplémentaire |
| Événement après reset | Ajouter un fichier pendant que A abandonne son ancien journal | L'événement postérieur au point de coupure reste disponible au prochain passage |
| Timestamp/taille conservés | Remplacer le catalogue par une version modifiée avec même taille et horodatage, en modifiant ses extrémités | La nouvelle empreinte déclenche la remise à zéro du journal local |
| Reprise scan planifié | Laisser arriver l'échéance de réconciliation complète | Le scan réconcilie les fichiers perdus par l'abandon volontaire des événements |

## Critères de validation

1. Aucun scan complet non planifié à cause d'un verrou occupé, d'une déconnexion ou d'un événement abandonné.
2. Jamais deux publications simultanées sous des verrous coopératifs valides.
3. Aucun remplacement du catalogue existant par une version incomplète ou issue d'un scan interrompu.
4. Les événements déjà abandonnés sont signalés en octets avec leur date ; ceux écrits après la coupure restent en attente.
5. Ne jamais supprimer manuellement un `.wimf.lock` ouvert : le verrou du système d'exploitation prévaut sur le marqueur.

## Limites connues

- Le test local sur processus indépendants ne prouve pas la sémantique effective des verrous byte-range du serveur SMB ciblé.
- Une empreinte calculée seulement sur les extrémités d'un fichier peut manquer une modification au milieu.
- Une interruption d'accès réseau et la durée de détection d'une session SMB morte dépendent de l'OS et du serveur.
- Consigner les résultats sur les deux postes avant de déclarer la fonctionnalité validée en environnement réel.
