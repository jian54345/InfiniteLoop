# Surfaces that stay closed

When the client or this repository has no drop table, pity weights, shop goods, or mode table, the surface stays closed. A `Reward.tsv` row that only shares a number is a different grant. These notes record the player-facing behavior and the missing source. They are not permission to invent a pool.

## Daily section rewards and obsolete stage purchases

`ReceiveDailyRewardRequest` remains closed with `20020003` (`RewardManagerGetRewardGoodsNotFound`). The EN/CN daily sources and installed daily resource expose dungeon data, rules and special conditions, but no daily-section claim requirements or section-to-reward mapping. Clearing unrelated stages cannot establish that missing claim rule. No claim record, reward or reset schedule is invented.

`BuyMainLineChallengeCountRequest` rejects unknown stages with `20003161` and existing stages with `20003020` (purchase limit reached). The EN Stage source supplies no `BuyChallengeCost` or `BuyChallengeCount` values; the server's corresponding TSV columns are blank. This obsolete purchase path cannot sell attempts until an authoritative price and purchase limit are available. It neither deducts currency nor changes persisted attempt counts.

`UnlockPrequelChallengeRequest` validates `CoverId` and the one-based `ChallengeId` against imported `prequel/Cover.tsv`, returning `20003095` or `20003096` for invalid selections. Valid selections remain locked (`20003097`): the client UI references `ChallengeConsumeItem`/`ChallengeConsumeCount`, but neither the EN Cover source nor the installed Cover schema provides them. Missing cost data is not a zero-price unlock. No synthetic unlock state or login data is created. Repeated requests and reloads preserve these boundaries.

## MainLine2 chapter rewards and eggs

Chapter treasure claims are open: `MainLine2ReceiveTreasureRequest.ChapterId` resolves `MainLine2Chapter.TreasureId`, counts passed stages through its stage groups, and claims all eligible zero-based `StageCounts` tiers with their normal and highlight rewards. Achievement claims use the requested main chapter's `AchievementId`; only persisted achievement bits whose `MainLine2Stage.AchievementTpyes` are NORMAL/SPECIAL count, never hidden bits or stage clears alone. Existing MainLine2 login data carries durable chapter treasure indices and main achievement flags.

Egg reward tables and membership are available, but the current eggs use condition type `15204`, requiring an exact persisted stage behavior-event value (`XConditionManager` reads stage id, event id, expected value). The server has no persisted stage-event value source or validated report ingestion for these behavior parameters. `MainLine2ReceiveEggsTreasureRequest` therefore fails the condition (`20219013`, `MainLine2MainEggsTreasureRewardConditionReceive`) without granting or marking the egg; ordinary stage clears cannot substitute for that missing evidence. A supported condition uses the shared condition evaluator and table reward, and claimed egg ids use the existing login list. Opening the current eggs requires authoritative stage-event report ingestion/persistence.


## Weekly task activeness and new-player task UI

These surfaces are open. `GetWeeklyActivenessRewardRequest` takes the client's empty request and claims all eligible, unclaimed Type-5 `CurrentTaskActiveness` milestones. Points count distinct finished tasks with authoritative `Task.Tag = 1`; reward contents come from `Reward`/`RewardGoods`, not legacy daily reward tables. Claimed milestone values are persisted, included in task login data, and cleared with weekly-tagged task claims at the existing weekly reset boundary. The legacy Type-2 weekly item/bitmask remains separate. `SetNewPlayerTaskActiveUiRequest.Group` must name an imported `NewPlayerTaskGroup` row and sets that group's bit in the persisted UI mask; the full mask is returned and included in login/new-player status.

## Awareness chapter claim rewards

`AwarenessGetRewardRequest` is recognized, but grants remain closed. The client Lua still calls `GetRewardId()` and consumes `RewardList`; neither the repository `AwarenessChapter.tsv`, the CN fallback JSON, nor the installed EN resource `AwarenessChapter.tab` contains a `RewardId` column or any chapter-to-reward mapping. The installed resource table SHA-1 is `df7b80159f41b72e897e263bbeba16eb206507d1` (bundle/index SHA-1 `5b6f172592c0dfbf04d488b6b85c40d5c24655fb`). Missing chapters return `20182001`, unfinished chapters `20182012`, and completed chapters `20020003` (`RewardManagerGetRewardGoodsNotFound`) without mutation, including retries and reloads. Opening this claim requires an authoritative chapter-to-reward mapping, not a matching numeric reward id.


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

## War Zone monster levels

No 4.8 table maps War Zone tiers (Backup through Legend) to monster levels. As maintainer-authorized AscNet policy (see `AGENTS.MD`), NPC levels are the curated levels in `Resources/Configs/arena_stage_data.json` scaled by `ChallengeArea.AvgScore` of the current challenge over that of the captured reference challenge (`LevelReferenceChallengeId`): Legend ×2, Hero ×1.5, Leader ×1, and Backup/Elite/Vanguard ×0.75 (those tiers share one `AvgScore`, so the table cannot separate them). Retail per-tier levels are unconfirmed; replace this when they are available.

## War Zone support buffs

No table links a zone to its support-buff group (`ArenaGroupFightEvent`). Retail pairs them by element (captured: Laser Alloy with 4174 Thunderstrike, Nihil Floating Platform with 4185 Raven's Dirge). As AscNet policy, each zone tab gets one support buff of its element, so an effect zone's two tabs get two: a group is eligible when it has one event per tab and each tab has an event whose `ArenaAreaBuffDetails` affix icon matches that tab's `AreaDistributeType` element, and a stable per-player draw picks among eligible groups. A fight applies the zone buff and the support buff of the tab it is fought on. A Chaos tab has no Chaos support buff and accepts any event; when no group matches, any group with one event per tab is used.

## Manual shop refresh and dorm tabs

The EN/CN assets contain no server-owned generic `Shop`/`ShopGoods` rows (also recorded in the table manifest). Every current curated catalog disables manual refresh (`ManualResetTimesLimit = 0`), so `RefreshShopRequest` returns `ShopKindIsNotReset` (20030018); no refresh prices or schedules have been invented. The handler supports static catalog costs/limits and explicitly authored shop `AutoResetClockId`, `IsResetManualTimes`, and goods `IsResetWhenShopReset` fields, with player counters and reset periods persisted separately. These field names come from the client's `XTableShop`/`XTableGoods` schema; applying the clock and reset flags is AscNet policy until server rows or retail behavior establish it. Escalating `ResetCostCountStr` schedules remain closed because no source rows are available; current fixed `RefreshCostCount` catalog prices are not evidence of those schedules.

Dorm is not closed: `GetShopBaseInfoRequest` returns existing curated Type-101 tab metadata only when the matching curated goods catalog exists. The client uses `Id`, `Type`, `SecondType`, and `Priority` to select and order those tabs. Goods, purchase counts and automatic reset times continue through the normal shop flow; missing dorm catalogs are hidden rather than populated with invented furniture offers. A repeated successful manual-refresh request is a new paid refresh (the client supplies no operation token), not an idempotent retry.

## Friends / social data sources

Friends, incoming applications, notes and blacklist entries are persisted and supplied through `NotifySocialData`; online targets receive the client-defined application/add/delete/block pushes. Limits and retry windows come from `share/config/Config.tsv`, normal friend capacities from `share/player/Player.tsv`, honor capacities from the CN fallback `share/player/HonorLevel.tsv` (the EN extract lacks that table), and note length from the imported client `MaxNameLength`. Recommendations contain real database players only; stable most-recent-login ordering is labelled AscNet policy because the retail ranking algorithm is not published. UTC calendar days define the daily application counter as AscNet policy. Friendship experience accrual and mentor-relation restrictions remain closed: no friendship-experience grant rule or persisted mentor backend exists in this slice; no synthetic experience or mentor relation is created.

## Archive unlocks and evaluations

Monster/info/skill/setting, weapon, and awareness acknowledgement requests validate the imported EN archive rows and durable kills/equipment before recording unlocks. Their authoritative rows contain no reward fields, so acknowledgements do not grant invented rewards. Login includes persisted unlock lists and the highest observed equipment level/breakthrough/resonance records; equipment history is reconciled at login and archive requests, retaining those observations after equipment disposal. Equipment disposed before its first archive reconciliation cannot be reconstructed from a nonexistent historical source.

Monster scores, difficulty, tags and like/dislike state are one persisted record per player and NPC. Aggregate scores/counts/tags are calculated from persisted player records, with the requesting player's live saved state replacing its database copy. Score/difficulty bounds (0–5), three selected tags, and like states (0 neutral, 1 dislike, 2 like) follow the EN Lua UI. Repeating an acknowledgement or rating is idempotent; editing/retracting a vote replaces its contribution.

`GetStoryEvaluateRequest` is declared only in `XEnumConst` in both EN and CN Lua; neither source has a producer/response consumer establishing its wire semantics. It returns `ArchiveInvalidStoryId` (20065006), and unsupported evaluation types return `ArchiveInvalidEvaluateType` (20065007), rather than inventing story aggregates. Unrecognized archive condition types fail closed with `ArchiveConditionNotFit` (20065004).

## Extra chapters, short stories and experiment star claims

Extra/short-story treasure claims join the EN chapter details' `TreasureId` and `StageId` arrays to their star-treasure rows. The imported claim TSVs preserve those joins; progress is the persisted passed-stage star bit count, not client-supplied totals. Claims and rewards persist, and `NotifyLogin` returns the claimed treasure IDs. A treasure whose authored stage list cannot meet its threshold remains unavailable rather than receiving invented stage completions.

Experiment star rewards use the EN `ExperimentLevel.StarReward` → `ExperimentReward` join (currently only the authored reward-bearing level 57). Login `ExperimentInfos` includes `Id`, persisted `StarsMark` and claimed `StarList` even before the first reward claim; live star-bearing clears publish the same `Info` through `NotifyUpdateExperimentId`, including improved stars on repeat clears. Levels without authored star rewards return `ExperimentIdNotHaveStarReward` (20051004). No CN fallback or captured reward data is needed for these claims.

## Player profiles, diaries and collection walls

Profile queries read the selected player's persisted characters, coatings, weapon coatings, medals and score titles. Appearance visibility is enforced server-side (all, persisted friends, self), including direct character-detail requests. Appearance and chief assistant selections persist in `PlayerData`; diary/message/like and collection presentation state persist in `Player.Profile`. Deleting diaries/messages and banning messages are idempotent; diary likes deliberately toggle, matching `XPersonalInfoManager`'s add/remove response codes rather than treating a repeated click as an idempotent add.

The EN legacy diary UI specifies a 100-character input limit but neither EN nor CN exposes authoritative diary-count, message-count or daily-like quotas. Under the missing-retail policy, `share/player/ProfilePolicy.tsv` explicitly defines AscNet's 100 diaries per player, 100 messages per diary, 20 like additions per server business day, and ten diaries per page. Unliking does not refund the daily addition quota. These are local server policy, not recovered retail limits. The 16-by-9 collection wall grid is not policy: it is the EN `UiCollectionWallEdit` edit surface (1440×810) divided by the client cell size of 90, which the client clamps placements to. EN `CollectionWall` and `CollectionWallDecoration` supply wall/default decoration identities and unlock rules; missing EN `CollectionSize` comes from the CN client table. Conditional walls currently evaluate the level condition used by the imported walls; unsupported condition kinds do not unlock decorations. Reward-only decorations require persisted ownership and are not granted by edits.

`CollectionShowSetRequest` is declared but unused in both managers examined: its `IsHideCollection` field follows the existing login schema as an explicit AscNet contract. Collection edits validate owned score titles, decoration ownership/type, size/quality, bounds, overlap, repeated IDs/groups and cross-wall duplicate ownership. `--profile-compat-only` exercises the registered handlers with two players and BSON reloads. No item semantics change.
