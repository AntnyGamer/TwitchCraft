tag @a remove lp_requester
tag @a[scores={locateplayers=1..},limit=1,sort=arbitrary] add lp_requester
execute as @a[tag=lp_requester,limit=1] run function locateplayers:run
scoreboard players set @a[tag=lp_requester] locateplayers 0
tag @a[tag=lp_requester] remove lp_requester
