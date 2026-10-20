# PR #100 — Validation multi-processus et protocole SMB

## Périmètre
La CI lance `SharedCatalogMultiprocessTests` sur Windows et Linux. Un programme auxiliaire `Wimf.LockProbe` acquiert le **vrai** `SharedCatalogLock` dans un processus indépendant du processus xUnit. Les tests couvrent : refus d'une seconde acquisition, absence de modification du catalogue lors du refus, marqueur artificiellement âgé de 31 minutes (verrou actif non expulsé), restitution après fermeture normale et sortie brutale sans `Dispose`, récupération sur le même chemin de fichier. Les tests existants d'indexation complète et incrémentale restent exécutés par `dotnet test Wimf.slnx`.

Les tests locaux **ne démontrent pas** la conformité SMB entre deux machines. La validation suivante est indispensable avant d'affirmer la sécurité multi-postes.

## Essai SMB reproductible sur deux machines

**Préparation**
1. Sur deux machines A et B (idéalement Windows/Windows, puis Windows/Linux avec montage CIFS), installer **la même révision** WIMF2 et utiliser une configuration avec un chemin de catalogue pointant vers **le même fichier physique** sur le partage SMB. Ne jamais faire ce test sur des catalogues de production.
2. Créer un répertoire source et un catalogue jetables ; relever le contenu et l'empreinte SHA-256 du catalogue ainsi que l'heure de la prochaine indexation complète configurée. S'assurer que l'identité SMB de chaque machine peut créer/ouvrir le fichier `<catalogue>.wimf.lock`.
3. Éviter de tester avec deux chemins différents qui se résolvent vers deux catalogues distincts. Examiner les journaux SMB **locaux à chaque machine** et noter les versions du serveur SMB, du client Windows/Linux et les options du montage.

**Cas A — contention et absence d'écrasement**
- Démarrer une indexation manuelle longue sur A, de façon à garder le verrou pendant l'essai (par exemple avec une arborescence de test suffisamment volumineuse). Vérifier sur le partage la présence de `.wimf.lock` ; confirmer via les journaux que A a acquis le verrou.
- Déclencher une indexation manuelle du même catalogue sur B. Vérifier que B refuse/reporte la publication et **n'efface jamais** le fichier de verrou. Comparer l'empreinte du catalogue avant et après la tentative refusée de B ; contrôler qu'aucun fichier partiellement publié n'est visible.
- Répéter en inversant A/B, et avec une indexation incrémentale contre une complète. Vérifier qu'un échec de verrou ne provoque pas une indexation complète automatique.

**Cas B — durée et plantage**
- Pendant que A détient le verrou, vieillir **uniquement** la date du marqueur à plus de 30 minutes, ou utiliser une indexation réellement plus longue que le timeout. B ne doit pas obtenir le verrou ; ne jamais supprimer le fichier de verrou pendant l'essai.
- Interrompre brutalement A (arrêt du processus, et séparément perte de liaison SMB si possible). Après que le serveur SMB a réellement libéré les verrous/sessions, B doit pouvoir acquérir le verrou sans supprimer le marqueur. Une coupure réseau peut demander une temporisation côté serveur ; noter le délai observé et toute incertitude de session.
- Vérifier l'intégrité et la lisibilité du dernier catalogue valide après tout échec.

**Cas C — journal incrémental par poste (analyse, sans présumer une correction)**
- Faire publier une modification de la source par A. Puis faire modifier un autre chemin tandis que B possède un journal/cursor local différent ; déclencher son incrémental. Vérifier l'union des deux modifications dans le catalogue final, en particulier les suppressions, renommages et modifications d'un même dossier.
- Recommencer dans l'ordre B puis A, avec reprise après déconnexion et avec un scan complet à l'échéance configurée.
- Si le résultat est non déterministe ou incorrect, **bloquer le déploiement multi-postes** et réserver la correction à une PR séparée (roadmap #102) ; ne pas contourner le problème par un scan complet non planifié.

## Résultats à consigner
Pour chaque OS/combinaison SMB : versions, montage, configuration pertinente (sans identifiants), horaires, noms des deux machines anonymisés, statut d'acquisition/rejet, empreintes avant/après, événements du journal et verdict. Les matrices GitHub Actions Windows/Linux sur disques locaux ne remplacent pas ces mesures.

## Limites architecturales connues
- Le verrou est **coopératif** : un écrivain ne l'utilisant pas peut remplacer le catalogue. Le contrôle de taille/date n'est pas une vérification transactionnelle face à cet écrivain.
- Les systèmes SMB/CIFS doivent propager de façon interopérable le verrou byte-range de `FileStream.Lock`. Les tests CI locaux ne garantissent pas cette propriété.
- Des journaux et curseurs incrémentaux propres à chaque poste peuvent ne pas avoir de référence causale commune. Une protection contre la concurrence des publications **ne suffit pas à garantir la convergence**. L'examen complet est prévu dans une PR distincte.
- Cette PR ne modifie ni cadence d'indexation ni logique de publication et n'ajoute aucun scan complet automatique.

## Exécution CI
`dotnet test Wimf.slnx --configuration Release` (Windows et Linux). L'auxiliaire est construit par la solution et exécuté dans un processus séparé via `dotnet run --no-build`.
