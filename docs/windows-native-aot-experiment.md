# Windows Native AOT — expérimentation PR #135

Objectif : déterminer si le **serveur ASP.NET Core** WIMF2 est publiable en Native AOT sous Windows x64, mesurer sa taille, et inventorier les avertissements et erreurs **sans remplacer** la distribution fonctionnelle produite par la PR #134.

## Périmètre

- Le lanceur WinForms demeure self-contained + single-file avec JIT : son éventuelle compatibilité AOT est une question distincte.
- Le serveur est compilé avec `-p:PublishAot=true` uniquement dans un script expérimental. Aucun changement de l'architecture de production.
- Le script publie aussi **`Wimf-AOT.exe`**, un lanceur WinForms single-file classique qui embarque le serveur AOT ; cela reste une version expérimentale distincte de `Wimf.exe`.

## Lancer l'expérience sur Windows

Dans PowerShell, avec SDK .NET 10 et outils C++ requis pour Native AOT :

```powershell
./scripts/Test-WindowsNativeAot.ps1
```

Les résultats sont enregistrés dans `artifacts/windows-aot-experiment/` :
- `result.txt` : succès/échec et taille ou code de sortie ;
- `publish.log` : avertissements Native AOT et erreurs de compilation ;
- `server/Wimf.Server.exe` : binaire expérimental *uniquement en cas de réussite* ;
- `Wimf-AOT.exe` : application portable expérimentale qui embarque ce serveur, uniquement si les deux publications réussissent.

En CI Windows, cette expérience est facultative, **non bloquante**, et ses résultats sont disponibles comme artefact `Wimf-win-x64-aot-experiment`.

## Après une compilation réussie

Comparer sur la **même machine** avec le serveur classique :
1. Taille du serveur compilé et de l'exécutable portable final.
2. Temps de démarrage jusqu'à la disponibilité HTTP locale.
3. Mémoire au repos et après chargement de plusieurs catalogues.
4. Recherches, API JSON, interface Web, actualisation complète et incrémentale, verrouillage, sortie propre.
5. Avertissements IL2026/IL3050 ou équivalents : une réussite de compilation avec avertissements ne certifie pas la compatibilité d'exécution.

Microsoft indique que Native AOT ASP.NET Core est principalement prévu pour les Minimal APIs et les composants compatibles avec trimming/source generators. Un code compilant sans erreur peut nécessiter des adaptations de sérialisation ou d'hébergement. **Aucune mesure de réduction de taille ne doit être annoncée avant publication effective.**

La validation Windows x64 réelle doit précéder toute décision d'adopter AOT dans le packaging.


## Essai utilisateur du package AOT

1. Dans la PR #135, ouvrir **Checks**, puis une exécution Windows réussie et télécharger l'artefact `Wimf-win-x64-aot-experiment`.
2. Vérifier que l'archive contient effectivement `Wimf-AOT.exe`. Les CI peuvent être vertes même si la compilation expérimentale échoue ; dans ce cas lire `result.txt` et `publish.log`.
3. Quitter complètement WIMF2 classique avant l'essai : les deux variantes utilisent le même port HTTP et le même mutex de tray. Ne pas les exécuter simultanément.
4. Copier `Wimf-AOT.exe` dans un dossier d'essai et utiliser une copie de `wimf.json` adaptée aux chemins locaux. **Éviter toute indexation sur les catalogues de production** lors du premier test AOT.
5. Démarrer l'EXE expérimental, ouvrir la recherche depuis la zone de notification, tester une recherche, les statistiques et le panneau des catalogues.
6. Vérifier la console « Messages du serveur », puis quitter la tray et confirmer l'arrêt de `Wimf.Server.exe`. Relever la taille du package et la mémoire du processus dans le Gestionnaire des tâches.
7. Signaler toute exception, erreur de sérialisation, absence d'interface ou crash. En cas d'échec, revenir au `Wimf.exe` classique sans modification de la configuration.

Le serveur extrait est placé dans un sous-dossier de `%LOCALAPPDATA%\\Wimf2\\server-runtime` distinct pour chaque empreinte binaire.

## Deux variantes Windows en parallèle

- **Wimf-AOT.exe** : lanceur Windows Forms .NET, avec serveur Native AOT embarqué.
- **Wimf-Native.exe** : lanceur Win32 **C++ natif**, avec le **même serveur Native AOT** embarqué comme ressource RCDATA. Son interface est volontairement minimale : icône Windows, recherche dans le navigateur, menu contextuel, accès au journal via Bloc-notes, boîte À propos et arrêt du serveur.

L'application Windows historique **Wimf.exe** est toujours produite par les CI indépendamment des deux variantes expérimentales.

Le script utilise `cl.exe` et `rc.exe` du compilateur Visual C++ sur le runner Windows. La compilation C++ requiert l'environnement MSVC (préparé par le workflow).

Le serveur natif est extrait, au premier lancement, sous `%LOCALAPPDATA%\Wimf2\server-runtime\<SHA256>\Wimf.Server.exe`, puis démarré en processus enfant. Le journal stdout/stderr du serveur est stocké dans `%LOCALAPPDATA%\Wimf2\logs\server-native.log` : le menu **Messages du serveur** ouvre ce fichier dans le Bloc-notes.

**Tester les variantes une par une** : elles partagent volontairement le mutex global au profil utilisateur et le port local 5080. Les fichiers de configuration restent externes, avec découverte automatique d'un `wimf.json` à côté du lanceur.

La génération en CI prouve la compilation et la présence des deux binaires ; les détails ergonomiques et l'arrêt propre de la variante C++ doivent être validés manuellement avant promotion en version stable.
