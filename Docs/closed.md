# Surfaces that stay closed

When the client or this repository has no drop table, pity weights, shop goods, or mode table, the surface stays closed. A `Reward.tsv` row that only shares a number is a different grant. These notes record the player-facing behavior and the missing source. They are not permission to invent a pool.

## Affection gift boxes

Normal, Fine, and Precious Gift Boxes (40691, 40692, 40693) are open. Each gives one favor gift of the box's quality. The members are the `CharacterTrustItem` rows of that quality: 40681 and 40682, 40683 and 40684, and the per-construct favorites 40601–40645. Each construct has their own precious gift, so a client patch that adds a construct adds an id. Account for that id here, in `Docs/items.md`, and in the expected pool in `AscNet.Test/Program.AutoUseGift.cs`. The client ships no `DropGroup` weights for ids 1003, 1004, and 1005, and the descriptions give no rates, so `AffectionGiftBoxPolicy.tsv` gives every member of a tier an equal chance. That is the same kind of labeled policy as the overclock boxes, not a recovered retail table.

Rewards 1003, 1004, and 1005 pay Black Cards, Cogs, and Skill Points. Those rows are not the gift pool. Quality 6 affection items 40800–40811 are a separate tier and are not in these boxes.

94033 Construct Research Surprise Fortune Bag is not one of these boxes. It opens from the chances printed on the item: 10/15/20/25/30% for 250/225/200/175/150 Event Construct R&D Tickets (50005). Those chances are recorded in `ConstructResearchFortuneBagPolicy.tsv`, keyed on item 94033. Reward 1011 pays Cogs and Black Cards and is not that grant.

## Other packs with no drop table

The same gap covers the other subtype-2 and subtype-4 packs. `Docs/items.md` lists each id. The affection gift boxes and 94033 are not in this list.

- 60003 Christmas Decor Blueprint Set. Reward 1006 is absent.
- 90101 and 90110 Equipment Overclock Black Box (S) and (M). Reward 9011 is absent. This is separate from 60001 and 60002, which open under the uniform overclock policy in `EquipmentOverclockDropPolicy.tsv`.
- 90104 Minor HQ Black Box, 90107 HQ Black Box (M), 90108 Memory EXP Set (S). Rewards 9014, 9017, and 9018 are absent.
- 91000 2–4★ Memory Box. Reward 9100's only good is Serum Bundle β (L) (90033), not a memory pool.
- 91006 6★ Memory Box, 92000 2–4★ Weapon Box, 93000 5★ Weapon Box. Rewards 9106, 9200, and 9300 are absent.
- 400031–400033 and 400060–400062 Flaming, Cobalt, and Green Eggs. Rewards 370000011–370000013 and 370000030–370000032 are absent. Each description grants 30,000 Cogs plus an Event Construct R&D Ticket count of 5, 10, or 15, and gives no weights.
- 1050–1052 New Year Present Test. Subtype 4, a red-envelope share chest. The client ships `RedEnvelopeNpc` only. The Cat Grab drop tables are absent.

## Fate banners

Fate groups publish pity as an inclusive 80–100 range and no threshold weights. `DrawServerRule.tsv` has `PityMin` 80 and `PityMax` 100 for groups 15, 36, and 38. A banner on those groups is not advertised, serves no draw info, and rejects a draw. See `Resources/Configs/draw-rules-source.md`.

That covers the Fate banners requested for the limited frame pools:

- Crucible Fate `2503` and arrival Fate `2510`, both group 15.
- Collab Fate `5613`, group 38. Its character banner `5612` stays open. The Fate pair does not.

## S-rank shard shop

Item 35 (S-rank inver material) and item 48 (Uniframe material) have no goods list in the shop catalogs. Overflow S-rank shards are not converted into item 35. Shop 6001 spends Trade Vouchers (102). Shop 601 spends item 34 for A-rank recycle. Details are in `Docs/items.md`.

## Operation Guardians

Operation Guardians is Maintainer Action (functional id 1302, `OnOpenMaintainerAction`). It is open: the 4.8 dump supplies `MaintainerActionConfig`, `MaintainerActionLevel` and `MaintainerActionEvent`, imported by `Scripts/import_maintaineraction_4_8.py`. These parts remain AscNet policy or closed, not retail behavior:

- The node pool for `NodePoolId` 2 is not in the dump. Board generation, node composition, hidden-node selection, card draws, the extra-action amounts of events 8 and 12 (event 13 states +2), the once-per-day start bonus and the warehouse reward (the level's `BoxRewardIds` entry) are labelled AscNet policy, seeded per player and week and persisted.
- `TimeId` 123 has no calendar row; availability is a labelled permanent-mode policy in `ActivityScheduleService`.
- Mentor nodes (node type 13, events 16-19, mails 61002/61003) stay inert: the mentor system is absent, so they are never generated and `HasMentorNode` is false.
- Other players on the board, cross-check requests and shared records are not simulated; the board shows only the owner and `MaintainerActionRecordRequest` returns only their own records.
- A lost response to a relocation node's event cannot be replayed (the follow-up request is indistinguishable from a retry); every other accepted node event is replayed.
- Only login creates the first board. A player who reaches the unlock condition mid-session gets the mode's data at their next login.

## Lightning and Thunder / The Meandering Three shops

Cerberus activity rows and Thunderbolt Manual guide activity `10007` reference shops `4008` and `4009`, but neither catalog exists in `Resources/Configs/client_shops.json`. The mode's story and challenge tables do not supply shop goods, prices or purchase limits. These two shops therefore stay closed; no catalog is inferred from the guide's reward totals or unrelated `Reward.tsv` rows.

Special "Honey Cake" (`97007`) and Movie Film: Cerberus (`97008`) remain inventory tokens, not automatically converted rewards. Guide `10007` lists Event Construct R&D Tickets (`50005`, target `750`), Trade Vouchers (`102`, target `2000`) and Punk Dynamo (`6003204`, target `1`), with event cause `180001`. Those are authored guide targets, not a free grant or a replacement shop catalog. Missing shops can prevent those exchange-derived guide targets from being earned.
