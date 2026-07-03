execute unless data storage locateplayers:state installed run scoreboard objectives add locateplayers trigger
execute unless data storage locateplayers:state installed run scoreboard objectives add lp_math dummy
execute unless data storage locateplayers:state installed run scoreboard objectives add lp_const dummy
execute unless data storage locateplayers:state installed run scoreboard players set $ten lp_const 10
execute unless data storage locateplayers:state installed run data modify storage locateplayers:state installed set value 1
