# C61-The-Mountain
Projet synthèse dans le cadre du programme de technique de l'informatique du cégep du Vieux-Montréal

## Membres de l'équipe

- Samuel Bergeron-Lagacé
- Jean-Marc Bouchard

## Démarrage rapide

La source du projet Godot se situe dans `./dev-game/`. Pour démarrer le projet sans configuration avancé, démarrer Godot et ouvrir le projet à la source du projet Godot. Ouvrez votre IDE a la racine du projet complet. Le projet devrait déja être initialisé et prêt à tester. Une configuration avancée est requise pour coder directement sur l'IDE.


## Configuration avancée

Pour avoir des fonctions de développement avancés dans votre éditeur de choix (Zed ou VsCode), Il vous faudra configurer les variables d'environnement du chemin vers l'éxécutable Godot. Pour ce faire, 

Sur Windows:
```powershell
[Environment]::SetEnvironmentVariable("GODOT_BIN", "C:\DDJV\core\Godot_v4.6.1-stable_mono_win64.exe", "User")
```

Et sur Linux:
```bash
export GODOT_BIN="/usr/bin/godot/Godot_v4.6.1-stable_mono_win64" 
# Changer le path pour le votre
```

Vue que les propriétés sont écris comme:
```json
    "command": "$env(GODOT_BIN)",    
```
alors ca va fonctioner *Out of the box*. Si jamais ca ne fonctionne pas comme Linux, avec des shells comme *fish*, alors aller dans le fichier de config (`~/.config/fish/fish.config`) et ajouter:

```config
set -gx GODOT_BIN /path/to/godot
```

```bash
# Rien encore
```

## Convention 
Nous utilisons les conventions standards pour DOTNET et Godot pour le code. Avec l'ajout de TOUJOURS placer les fonctions _Ready() , _Process() et autres a la fin du document.
