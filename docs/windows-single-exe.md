# Windows : exécutable unique portable

Publier sur Windows x64 avec .NET 10 SDK et PowerShell :

```powershell
./scripts/Publish-WindowsSingleExe.ps1
```

Le seul fichier à distribuer est `artifacts/windows/Wimf.exe`. Une exécution CI Windows produit aussi l'artefact `Wimf-win-x64-single-exe`.

- Le programme est **self-contained** : aucune installation du runtime .NET n'est nécessaire.
- Le lanceur WinForms et le serveur ASP.NET Core sont chacun publiés en single-file. Le serveur est **intégré comme ressource** à `Wimf.exe` ; au premier lancement de cette version, il est extrait dans `%LOCALAPPDATA%\Wimf2\server-runtime\<empreinte>\Wimf.Server.exe` puis lancé comme processus enfant. Cela donne **un seul fichier à copier**, mais **pas un seul processus** ni zéro extraction.
- Un nouveau binaire serveur utilise un sous-répertoire de cache distinct. Les anciennes versions de cache ne sont pas supprimées automatiquement.
- Le serveur est arrêté quand l'utilisateur quitte la zone de notification.
- Le fichier `wimf.json` reste externe : le placer à côté de `Wimf.exe` ou passer `--config chemin\wimf.json`. Les catalogues et caches de données restent externes.
- Le paramètre `--server CHEMIN` permet toujours de sélectionner un serveur alternatif pour le développement et la maintenance.
- Le programme n'est pas signé : Windows SmartScreen peut afficher un avertissement selon les politiques du poste.

## Validation manuelle à prévoir

Sur un poste Windows x64 ne disposant d'aucun runtime .NET installé : copier **uniquement** `Wimf.exe` et un fichier de configuration externe valide, lancer le programme, ouvrir l'interface depuis la zone de notification, effectuer une recherche, vérifier les indexations, puis quitter et confirmer l'arrêt du processus `Wimf.Server.exe`.

La CI valide le build, les tests et la production d'un artefact unique ; elle ne remplace pas ce test interactif ni la validation SMB réelle.
