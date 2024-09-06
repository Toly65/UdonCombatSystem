# UdonCombatSystem
A simple combat system designed for VRchat


Setup:
Simply drag the combat system prefab into your world, this will include a respawn point upon death along with a container where players temporarily go when they die.

The root of the gun prefabs typically contain all of the configurable variables.

Some guns are configured with a two handed grip, in this case you must place anything you want to follow the position of the gun as it rotates on the rotate point.

hitboxes are managed locally and are pooled in the combat system prefab, any changes to a hitbox will have to be duplicated to refil the pool

Projectile weapons should contain any scripted things they are firing out within themselves in a disabled form in order to clone them for use in game, this allows them to be preconfigured. An example of something you would want to clone would be a physical bullet

For further support and possible updates join this discord: https://discord.gg/cvF8JEhrq7
