# Distribution Windows native (choix actuel)

Le build Windows principal produit désormais **`artifacts/windows/Wimf.exe`**, avec :
- un lanceur Windows C++ Win32 léger ;
- le serveur WIMF2 compilé en .NET Native AOT, intégré comme ressource ;
- un seul EXE à distribuer, sans runtime .NET à installer ;
- un fichier `wimf.json` externe.

Le lanceur .NET WinForms reste dans `src/Wimf.Tray` pour référence et retour arrière, mais n'est **plus compilé ni publié en CI**. Le script de publication historique `scripts/Publish-WindowsSingleExe.ps1` est conservé pour une éventuelle utilisation manuelle.

## Publication

Sur Windows avec SDK .NET 10, outils MSVC et Windows SDK dans le PATH :

```powershell
./scripts/Test-WindowsNativeAot.ps1
```

GitHub Actions produit l'artefact **`Wimf-win-x64-native`** contenant `Wimf.exe`. L'échec d'une compilation AOT, du compilateur de ressources ou de l'édition de liens C++ fait échouer la CI Windows.

## Fonctionnement et limites

Le serveur AOT est extrait dans `%LOCALAPPDATA%\Wimf2\server-runtime\<SHA256>\Wimf.Server.exe` puis lancé comme processus enfant. Le menu « Messages du serveur » ouvre le journal `%LOCALAPPDATA%\Wimf2\logs\server-native.log`. L'application utilise le port local 5080. Les catalogues et caches restent extérieurs au binaire.

Validation manuelle conseillée avant fusion : icône EXE et zone de notification, accents du menu et de la boîte de lancement multiple, fenêtre « À propos » avec lien GitHub, ouverture de l'interface, recherche, lecture des catalogues, arrêt propre. Ne pas faire d'essais d'indexation sur les catalogues de production.

## Variante expérimentale avec serveur compressé

Le build Windows normal reste `Wimf-win-x64-native` : son serveur Native AOT est stocké sans compression supplémentaire.

La CI tente maintenant de créer `Wimf-win-x64-native-compressed-experiment` en compressant le serveur avec **XPRESS_HUFF** (API native Windows, `cabinet.dll`). Le lanceur C++ décompresse cette ressource en mémoire, puis écrit le serveur habituel dans le cache local identifié par SHA-256.

Les mesures sont dans `sizes.txt` (octets avant/après et économie). Le résultat de cette expérience est non bloquant et **ne remplace pas** le build Windows principal.

Compromis : le démarrage initial effectue une décompression et une allocation mémoire temporaire supplémentaire. Vérifier en priorité le lancement, l'extraction correcte, la recherche, le menu et la fermeture. Si l'artefact compressé n'est pas produit, consulter les logs Windows plutôt que considérer une CI verte comme une validation de cette variante.
