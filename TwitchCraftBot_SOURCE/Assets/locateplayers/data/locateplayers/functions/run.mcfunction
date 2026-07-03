tellraw @s {"text":"Players online:","color":"yellow","bold":true}
execute as @a run function locateplayers:print_one
