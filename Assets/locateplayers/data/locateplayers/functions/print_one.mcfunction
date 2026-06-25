execute store result score $x lp_math run data get entity @s Pos[0] 1
execute store result score $y lp_math run data get entity @s Pos[1] 1
execute store result score $z lp_math run data get entity @s Pos[2] 1
tellraw @a[tag=lp_requester] [{"selector":"@s","color":"aqua"},{"text":": ","color":"gray"},{"text":"X=","color":"gold"},{"score":{"name":"$x","objective":"lp_math"}},{"text":" Y=","color":"gold"},{"score":{"name":"$y","objective":"lp_math"}},{"text":" Z=","color":"gold"},{"score":{"name":"$z","objective":"lp_math"}}]
