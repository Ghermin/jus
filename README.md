# Jus

Le pourcentage de batterie de ta souris, ton casque, tes écouteurs, ta manette et ton clavier, toujours visible dans la barre des tâches de Windows.

**Page du projet et téléchargement : https://ghermin.github.io/jus/**

- Un seul exe d'environ 200 Ko, sans fenêtre administrateur.
- Passe en rouge et prévient sous 20 %.
- Aucun compte, aucune télémétrie : rien ne quitte le PC.

## Appareils

Souris, claviers, casques, écouteurs, manettes et PC portables. Jus repère tout seul ce qui est connecté, et un appareil éteint ou retiré disparaît de la barre.

Un appareil manque ? Lance `Jus.exe --scan jus-scan.txt` et joins le fichier à une issue, après l'avoir relu : il liste tes appareils.

## Compiler

Il faut PowerShell 7, qui fournit le compilateur C#.

```bash
pwsh -File build.ps1
```

L'exe arrive dans `out\Jus.exe`. Pour l'essayer sans l'installer :

```bash
out\Jus.exe --portable
```

## Licence

MIT. Un projet du [Bored Dev Playground](https://ghermin.github.io/).
