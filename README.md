# UdonCombatSystem
## LATEST VERSIONS REQUIRE THE LATEST UDONSHARP VERSION THAT CAN ONLY BE OBTAINED
## THROUGH VRCHAT CREATOR COMPANION
A simple combat system designed for VRchat

Relies on https://github.com/Toly65/Udon-Stow-System

## Setup
### Combat System Setup
1. Drag the combat system prefab into the scene
2. Find the playerHealthManager gameobject
3. Assign a spawn point into the spawn points array

### Gun setup
1. Use one of the gun prefabs as a template
2. Place anything visual on the rotation point
3. Have the script as the first parent of the pickup
4. Configure variables as desired

#### For projectile weapons
1. Perform the above steps in Gun Setup
2. Have a gameobject with a collider and a bullet script
3. Assign variables there
4. Assign this bullet prefab to the gun
5. Untick raycast damage
6. Ensure that the projectile is slow as unity's collision will not play nice at actual bullet speeds

Projectile weapons should contain any scripted things they are firing out within themselves in a disabled form in order to clone them for use in game, this allows them to be preconfigured. An example of something you would want to clone would be a physical bullet

Hitboxes are managed locally and are pooled in the combat system prefab, any changes to a hitbox will have to be duplicated to refill the pool

## Features

- Favour the shooter combat
- Bone tracked hitboxes
- Explosions that push physics objects and players
- A flexible modular system if you wish to expand the system with your own features

For further support and possible updates join this discord: https://discord.gg/cvF8JEhrq7
