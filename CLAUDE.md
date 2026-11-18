# Jus : notes pour les sessions de développement

Appli Windows qui affiche le niveau de batterie des appareils dans la barre des tâches.

## Compiler / tester

- `pwsh -File build.ps1` → `out\Jus.exe`. Compilateur = Roslyn embarqué dans PowerShell 7, cible .NET Framework 4.8. Ne pas créer de `.csproj`.
- `out\Jus.exe` lancé sans option **s'installe** dans `%LOCALAPPDATA%\Programs\Jus`. Pour un essai : `--portable`.
- `--scan fichier.txt` : ce que dit chaque source et la liste des collections HID. `--preview fichier.png` : rendu de la barre en sombre et en clair. `--quit` : ferme la copie en cours.
- Ne jamais publier une sortie de `--scan` : elle contient les noms et adresses des appareils.

## Organisation

- `src/Sources.cs` : une classe par famille d'appareils (`IBatterySource`). Ajouter un appareil = une source de plus, déclarée dans `BatteryMonitor.sources`.
- `src/BatteryMonitor.cs` : interroge les sources sur un fil à part, toutes les 4 s pour les moins coûteuses, et aussitôt qu'un appareil est branché ou retiré (`DeviceChangeWindow`). Une fenêtre enfant de la barre des tâches partage sa file d'entrée avec l'Explorateur : aucun accès matériel sur le fil de l'interface, sinon la barre des tâches se fige.
- `src/TaskbarWidget.cs` : fenêtre transparente enfant de `Shell_TrayWnd`, placée à gauche de `TrayNotifyWnd`.
- `src/TrayApp.cs` : icône, panneau, alertes, réglages (`%APPDATA%\Jus\settings.txt`).
- `src/MenuTheme.cs` : design du panneau, repris du Gestionnaire RGB. Couleurs `Palette.Light` / `Palette.Dark` = variables `--pg-*` de `docs/index.html` : les changer aux deux endroits. `--preview-menu fichier.png` le dessine en clair et en sombre.

## Protocoles

- Requêtes en lecture seule uniquement : ne jamais envoyer de commande qui modifie un réglage d'un appareil, ni de commande non documentée pour « voir ce que ça répond ».
- 8BitDo Ultimate 2C : pas de niveau lisible en câble (testé : `81 04 21 01 00 00 06` sans réponse, Windows.Gaming.Input renvoie un faux 1000/1000). Elle s'affiche avec `NoLevel`. Seule requête autorisée : identification `81 05 00 21 01` sur FF7A, réponse `1B 30` par câble, `1C 30` par récepteur. Beaucoup de commandes `81 05` écrivent en flash : n'en ajouter aucune sans source fiable.
- Panneau : `AutoClose = false`, il ne se ferme que sur un clic en dehors de sa fenêtre (`PanelMenu.Watch`).
- Logitech HID++ : `softwareId` change à chaque requête, sinon une réponse en retard d'une souris en veille passe pour la suivante.
- Arctis Nova 7 : requête `00 B0`, réponse niveau en `b[3]`, état en `b[4]` (0 éteint, 1 en charge).
- Les noms Bluetooth peuvent contenir le prénom du propriétaire : `Naming.Clean` le retire.

## Publier

`AssemblyVersion` dans `src/Program.cs`, commit, étiquette `vX.Y.Z` poussée → `.github/workflows/build.yml` compile et crée la release avec `Jus.exe`.

## Git

Dépôt public `github.com/Ghermin/jus`. Identité des commits : `Ghermin <Ghermin@users.noreply.github.com>`.

## Bored Dev Playground

Ce projet fait partie du Bored Dev Playground : https://ghermin.github.io/ (repo `Ghermin/Ghermin.github.io`, en local `../Ghermin.github.io`).

- `docs/playground.css` est une copie de `kit/playground.css` du playground : modifier le kit là-bas puis lancer `sh kit/sync.sh`. Les couleurs propres à Jus se règlent dans la page, avec les variables `--pg-*`.
- Icône : `brand/icons/jus.svg` du playground, puis `node build.js jus` dans `brand/`.
- Si le nom, la description ou les tags changent, mettre à jour la carte dans `index.html` du playground.
- Garder sur la page le bandeau `pg-strip`, le thème clair / sombre automatique et le responsive. L'appli garde le lien « un projet du bored dev playground » en bas de son menu.
