# Windows Native AOT — expérimentation PR #135

Objectif : déterminer si le **serveur ASP.NET Core** WIMF2 est publiable en Native AOT sous Windows x64, mesurer sa taille, et inventorier les avertissements et erreurs **sans remplacer** la distribution fonctionnelle produite par la PR #134.

## Périmètre

- Le lanceur WinForms demeure self-contained + single-file avec JIT : son éventuelle compatibilité AOT est une question distincte.
- Le serveur est compilé avec `-p:PublishAot=true` uniquement dans un script expérimental. Aucun changement de l'architecture de production.
- Ce n'est **pas encore** un nouveau `Wimf.exe` : même une compilation AOT réussie doit être suivie de tests fonctionnels et de l'intégration au packaging avant toute substitution.

## Lancer l'expérience sur Windows

Dans PowerShell, avec SDK .NET 10 et outils C++ requis pour Native AOT :

```powershell
./scripts/Test-WindowsNativeAot.ps1
```

Les résultats sont enregistrés dans `artifacts/windows-aot-experiment/` :
- `result.txt` : succès/échec et taille ou code de sortie ;
- `publish.log` : avertissements Native AOT et erreurs de compilation ;
- `server/Wimf.Server.exe` : binaire expérimental *uniquement en cas de réussite*.

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
