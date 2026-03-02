# Priorité des exigences

| Lettre | Signification |
|--------|---------------|
| U | Indispensable |
| S | Souhaitable |
| O | Optionnel |
| U | Pour une version ultérieure |

# Cas d'usage

| Code | Priorité | Description | Critères d'acceptation |
|------|----------|-------------|------------------------|
| US-01 | I | En tant qu'utilisateur, je veux créer un compte avec mon courriel, afin d'accéder au jeu. | Le formulaire de création accepte un courriel valide et un mot de passe. Un courriel de confirmation est envoyé à l'inscription. L'utilisateur ne peut accéder à la plateforme sans compte. |
| US-02 | O | En tant qu'utilisateur, je veux supprimer définitivement mon compte, afin de retirer mes données de la plateforme. | La suppression est irréversible et confirmée par l'utilisateur. Toutes les données liées au compte sont supprimées. Une confirmation de suppression est affichée. |
| US-03 | I | En tant qu'utilisateur, je veux me connecter à mon compte afin d'accéder au jeu. | Le formulaire de connexion accepte un courriel et un mot de passe. L'utilisateur est connecté si les informations sont valides. Un message d'erreur est affiché si le courriel est invalide. Un message d'erreur est affiché si le mot de passe est incorrect. Un message d'erreur est affiché si le compte n'existe pas. L'utilisateur ne peut pas accéder à la plateforme sans être connecté. Une confirmation visuelle indique que l'utilisateur est connecté avec succès. |
| US-04 | O | En tant qu'utilisateur, je veux pouvoir changer mon mot de passe afin de sécuriser mon compte. | Le système permet à l'utilisateur d'accéder à une option « Changer le mot de passe ». L'utilisateur doit saisir son mot de passe actuel ainsi que le nouveau mot de passe (et une confirmation). Le nouveau mot de passe doit respecter les règles de sécurité définies (ex. longueur minimale). Un message d'erreur est affiché si le mot de passe actuel est incorrect. Un message d'erreur est affiché si le nouveau mot de passe et la confirmation ne correspondent pas. Un message d'erreur est affiché si le nouveau mot de passe ne respecte pas les critères requis. Si la modification réussit, un message de confirmation est affiché et le nouveau mot de passe est enregistré. |
| US-05 | I | En tant qu'utilisateur, je veux pouvoir avoir accès à une interface de menu afin d'accéder aux différentes fonctionnalités du jeu. | Le menu principal est affiché lorsque l'utilisateur lance le jeu. Le menu permet d'accéder aux options suivantes : créer une partie, rejoindre une partie, settings, crédits, news et quitter. L'utilisateur peut sélectionner une option du menu. Le système exécute l'action associée à l'option sélectionnée. |
| US-06 | I | En tant qu'utilisateur, je veux pouvoir créer une partie afin de jouer avec d'autres joueurs. | L'utilisateur peut sélectionner l'option « créer une partie ». Une nouvelle partie est créée. La partie est accessible aux autres joueurs avec un code. |
| US-07 | S | En tant qu'utilisateur, je veux pouvoir créer une partie à partir d'une sauvegarde afin de continuer une partie existante. | L'utilisateur peut sélectionner une sauvegarde existante. Une partie est créée à partir de cette sauvegarde. La progression sauvegardée est chargée. |
| US-08 | I | En tant qu'utilisateur, je veux pouvoir rejoindre une partie afin de jouer avec d'autres joueurs. | L'utilisateur peut rejoindre une partie. L'utilisateur apparaît dans la partie. |
| US-09 | S | En tant qu'utilisateur, je veux pouvoir quitter le jeu afin de fermer l'application. | L'utilisateur peut sélectionner l'option « quitter ». Le jeu se ferme. |
| US-10 | I | En tant qu'utilisateur, je veux pouvoir lancer une partie une fois le groupe formé afin de commencer à jouer. | Le système permet de lancer la partie lorsque le groupe est formé. La partie démarre pour tous les joueurs. |
| US-11 | O | En tant qu'utilisateur, je veux pouvoir voir l'écran de news afin de consulter les nouvelles du jeu. | L'utilisateur peut accéder à l'écran de news. Les news sont affichées. |
| US-12 | O | En tant qu'utilisateur, je veux pouvoir modifier les settings de jeu afin de personnaliser mon expérience. | L'utilisateur peut accéder aux settings. L'utilisateur peut modifier les paramètres. Les modifications sont enregistrées. |
| US-13 | I | En tant qu'utilisateur, je veux pouvoir avoir un personnage afin de pouvoir jouer dans le jeu. | Un personnage est attribué à l'utilisateur. Le personnage est visible dans le jeu. |
| US-14 | I | En tant qu'utilisateur, je veux pouvoir déplacer mon personnage afin d'explorer l'environnement. | L'utilisateur peut contrôler les déplacements du personnage. Le personnage se déplace dans l'environnement. |
| US-15 | I | En tant qu'utilisateur, je veux pouvoir être présent dans une carte afin de jouer dans un environnement. | Le personnage est placé dans une carte. L'utilisateur peut voir l'environnement. |
| US-16 | S | En tant qu'utilisateur, je veux que mon personnage ait des animations afin d'améliorer l'immersion. | Le personnage possède des animations. Les animations sont visibles en jeu. |
| US-17 | S | En tant qu'utilisateur, je veux que mon personnage et l'environnement produisent des sons afin d'améliorer l'expérience. | Des sons sont produits dans le jeu. Les sons sont audibles. |
| US-18 | I | En tant qu'utilisateur, je veux pouvoir voir et interagir avec les autres personnages afin de jouer en multijoueur. | Les autres personnages sont visibles. L'utilisateur peut interagir avec eux. |
| US-19 | I | En tant qu'utilisateur, je veux pouvoir écrire un message sur mon calepin afin de communiquer avec les autres joueurs. | L'utilisateur peut écrire un message. Le message est enregistré sur le calepin. |
| US-20 | I | En tant qu'utilisateur, je veux pouvoir fièrement montrer aux autres mon calepin avec mon message écrit dessus afin qu'ils puissent le lire. | Le calepin peut être affiché par l'utilisateur. Les autres joueurs peuvent voir le message. |
| US-21 | I | En tant qu'utilisateur, je veux pouvoir interagir avec l'environnement afin de progresser dans le jeu. | L'utilisateur peut interagir avec des éléments de l'environnement. L'interaction produit un effet dans le jeu. |
| US-22 | S | En tant qu'utilisateur, je veux pouvoir ouvrir le menu de pause afin d'accéder aux options en cours de partie. | L'utilisateur peut ouvrir le menu de pause. Le menu de pause est affiché. |
| US-23 | S | En tant qu'utilisateur responsable de la partie, je veux pouvoir sauvegarder la partie afin de la reprendre plus tard. | Le responsable peut sauvegarder la partie. La progression est enregistrée. |
| US-24 | S | En tant qu'utilisateur, je veux pouvoir quitter la partie afin de retourner au menu. | L'utilisateur peut quitter la partie. L'utilisateur retourne au menu. |
| US-25 | I | En tant qu'utilisateur, je veux pouvoir jouer dans un environnement multijoueur fluide afin d'avoir une expérience jouable. | Les mouvements des joueurs sont visibles. La partie reste jouable en multijoueur. |
| US-26 | U | En tant qu'utilisateur, je veux pouvoir jouer dans plusieurs cartes afin de varier l'expérience de jeu. | Le jeu contient plusieurs cartes. Une carte est chargée lors du lancement d'une partie. |
| US-27 | U | En tant qu'utilisateur, je veux pouvoir avoir plusieurs skins de pingouin afin de personnaliser mon personnage. | Plusieurs skins sont disponibles. Les skins sont visibles en jeu. |
| US-28 | U | En tant qu'utilisateur, je veux pouvoir configurer le skin que je veux afin d'utiliser le personnage de mon choix. | L'utilisateur peut sélectionner un skin. Le skin sélectionné est appliqué au personnage. |
| US-29 | U | En tant que développeur/admin, je veux pouvoir voir les analytics afin d'analyser l'utilisation du jeu. | Les données d'utilisation sont accessibles. Les données sont affichées. |
