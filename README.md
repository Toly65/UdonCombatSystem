# UdonCombatSystem
## LATEST VERSIONS REQUIRE THE LATEST UDONSHARP VERSION
A simple combat system designed for VRchat

relies on https://github.com/Toly65/Udon-Stow-System

Setup:
combat system setup:

drag the combat system prefab into the scene

find the playerHealthManager gameobject

assign a spawn point into the spawn points array

gun setup:

use one of the gun prefabs as a template

 place anything visual on the rotation point

 have the script as the first parent of the pickup

configure variables as desired

(for projectile weapons)

have a gameobject with a collider and a bullet script

assign variables there

assign this bullet prefab to the gun

untick raycast damage

ensure that the projectile is slow as unity's collision will not play nice at actual bullet speeds

Projectile weapons should contain any scripted things they are firing out within themselves in a disabled form in order to clone them for use in game, this allows them to be preconfigured. An example of something you would want to clone would be a physical bullet

hitboxes are managed locally and are pooled in the combat system prefab, any changes to a hitbox will have to be duplicated to refil the pool

## features

-favour the shooter combat

-bone tracked hitboxes

-explosions that push physics objects and players

-a flexible modular system if you wish to expand the system with your own features

For further support and possible updates join this discord: https://discord.gg/cvF8JEhrq7
