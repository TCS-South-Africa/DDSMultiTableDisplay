# MTD table summary: the DDS → MTD message (contract v2)

**Status:** Version 2, 26 November 2026. MTD consumes the display alias in v2 and accepts v1 summaries using the ordinary table label. The DDS publisher requires the v2 update below.

DDS publishes one message per table that carries everything MTD needs to draw that table's widget. Every message is a complete snapshot. MTD therefore keeps no history of its own, and a display that starts late needs only one message per table to be correct.

Code references point at DDS (`C:\UnityProjects\DDS`) and at the T-Connect server snapshot (`Downloads/tcsjh-tconnect-c5329be04546`) as read on 24 November 2026. Line numbers drift, so confirm them before editing.

## 1. Subject

| Transport | DDS publishes to | Wildcard form |
| --- | --- | --- |
| MQTT | `tcsjh/{siteName}/table/{TableId}/mtdsummary` | `tcsjh/{siteName}/table/+/mtdsummary` |
| NATS | `tcsjh.table.{TableId}.mtdsummary` | `tcsjh.table.*.mtdsummary` |

- In Manual Entry mode MTD subscribes to the exact publish subject for each configured GUID. Only NATS supports the T-Connect roster.
- DDS builds the MQTT form, as it does for every other message, and `TConnect2NatsSubjects.FromMqttTopic` turns it into the NATS form by dropping the site segment (DDS `Assets/External Communication/TConnect2Nats/TConnect2NatsSubjects.cs:22-45`). No change to the mapping is needed.
- `siteName` is lower-cased and defaults to `testsite` when blank (DDS `TConnect2.cs:85-86`). MTD applies the same rule in MQTT mode.
- The new subtopic does not collide with the server. The T-Connect server subscribes to each table subtopic by name (`tcsjh.table.*.gamestate`, `...info` and so on, snapshot `Server/TConnect.Server.TConnectService/TConnectWorker.cs:79-137`), not to `tcsjh.table.>`.

## 2. Payload

JSON, UTF-8, PascalCase property names, serialised with Newtonsoft like the other T-Connect 2 messages.

| Field | JSON type | Meaning |
| --- | --- | --- |
| `SchemaVersion` | number | `2` for this document. |
| `TableId` | string (GUID) | The T-Connect table ID from device assignment. MTD's identity for the table. |
| `GameId` | string (GUID) | The game that produced `Results[0]`. All zeros when `Results` is empty. See rule 5.3. |
| `GameType` | string | A `GameTypesEnum` name: `ROULETTE`, `ROULETTE_XTRA`, `ROULETTE_POWER`, `SICBO`, `BLACKJACK` and so on. |
| `TableName` | string | Table name without the number, for example `"SB"`. |
| `TableNumber` | number | Table number, for example `19`. |
| `MTDTableUniqueName` | string, optional | Display alias, for example `"Salon 019"`. Preserve text and leading zeros. Blank uses the ordinary table label. |
| `MTDColourSetting` | string | Widget colour as `#RRGGBB`, for example `"#270470"`. |
| `TableOpen` | boolean | Whether the table is open. |
| `MinBet`, `MaxBet` | number | Current table limits in DDS's stored units (cents). |
| `MinBetText`, `MaxBetText` | string | The same limits formatted by DDS for display, for example `"$10"` and `"$10,000"`. |
| `Results` | array of result objects | Newest first. `Results[0]` is the current result. At most 20 entries. Empty when the table has no results. |
| `HotNumbers` | array of strings | Hottest first. At most 8. May be empty. |
| `ColdNumbers` | array of strings | Coldest first. At most 8. May be empty. |
| `Statistics` | object, string → string | Game-specific counts. See section 3. May be empty. |
| `EventTime` | string (ISO 8601 with offset) | When DDS generated this message. |

A result object:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `Result` | string | The result in display form. See section 3. |
| `Multiplier` | number, optional | The "something additional" for Roulette Xtra and Power Reel. Left out when there is none. |
| `MultiplierColour` | string, optional | `#RRGGBB` chip colour DDS shows for that multiplier. An MTD UI may use it or ignore it. |

How this maps to the requested list: items 1 to 13 are `TableId`, `GameId`, `GameType`, `TableName`, `TableNumber`, `MTDTableUniqueName`, `MTDColourSetting`, the four limit fields, `Results`, `HotNumbers`, `ColdNumbers`, `Statistics` and `EventTime`. Four things were added:

- `SchemaVersion`, so the message can change later without guesswork.
- `TableOpen`, so MTD can leave closed tables out of the rotation.
- `MinBetText` and `MaxBetText`, so MTD shows exactly what the table shows. Currency symbol, symbol position and culture are per-table DDS settings (`currencyOverride`, `currencySymbolAfterAmount`), and DDS already has the formatter (`Utility.GetCurrencyString`, DDS `Assets/Scripts/Utility.cs:479`).
- `MultiplierColour`, because DDS stores a chip colour with each Power Reel history entry (`RouletteXtra2HistoryResult.ChipColour`).

The old v1 field `MTDTableNumber` is ignored by MTD. Only the provisioned `TableId` GUID identifies a table. Renaming a table or using the same display alias on two tables has no effect on selection, assignment or ordering.

### Example: Power Reel roulette

```json
{
  "SchemaVersion": 2,
  "TableId": "5b0c7c0e-6f0a-4a57-9a53-3b1f0d1c2a11",
  "GameId": "0f6f2f8e-3d0b-4b7b-9a2e-7c1d5e8f9a01",
  "GameType": "ROULETTE_POWER",
  "TableName": "AR",
  "TableNumber": 4,
  "MTDTableUniqueName": "West 04",
  "MTDColourSetting": "#6A1B9A",
  "TableOpen": true,
  "MinBet": 1000,
  "MaxBet": 1000000,
  "MinBetText": "$10",
  "MaxBetText": "$10,000",
  "Results": [
    { "Result": "21", "Multiplier": 50, "MultiplierColour": "#C9A227" },
    { "Result": "24", "Multiplier": 30, "MultiplierColour": "#2E7D32" },
    { "Result": "26", "Multiplier": 20, "MultiplierColour": "#1565C0" },
    { "Result": "00", "Multiplier": 60, "MultiplierColour": "#AD1457" }
  ],
  "HotNumbers": ["24", "26", "3", "8"],
  "ColdNumbers": ["1", "14", "35", "0"],
  "Statistics": {
    "SampleSize": "200",
    "Red": "92", "Black": "97", "Zero": "11",
    "Odd": "95", "Even": "94",
    "Low": "90", "High": "99",
    "Dozen1": "63", "Dozen2": "61", "Dozen3": "65",
    "Column1": "60", "Column2": "66", "Column3": "63"
  },
  "EventTime": "2026-11-24T10:15:30.123+00:00"
}
```

### Example: SicBo (fields that differ)

```json
{
  "GameType": "SICBO",
  "TableName": "SB",
  "TableNumber": 19,
  "MTDTableUniqueName": "Salon 019",
  "Results": [
    { "Result": "4-5-6" },
    { "Result": "1-2-6" },
    { "Result": "3-3-3" }
  ],
  "HotNumbers": ["10", "11", "9"],
  "ColdNumbers": ["4", "17", "5"],
  "Statistics": {
    "SampleSize": "200",
    "Small": "94", "Big": "98", "Triple": "8",
    "Odd": "97", "Even": "95",
    "Face1": "101", "Face2": "96", "Face3": "104",
    "Face4": "99", "Face5": "101", "Face6": "99"
  }
}
```

## 3. Game-specific rules

### Roulette (`ROULETTE`, `ROULETTE_XTRA`, `ROULETTE_POWER`, `ROULETTE_SPREAD`, `PARIMAX_ROULETTE`)

- `Result` is the pocket as shown on the table: `"0"`, `"00"`, `"000"`, `"0000"`, `"1"` to `"36"`. DDS holds results as integers where 37 is `00`, 38 is `000` and 39 is `0000`, and `Utility.GetRouletteStringForResult` already does the conversion (DDS `Assets/Scripts/Utility.cs:226-246`). The existing `gamesummary` message sends the raw integer (`"37"`); this message deliberately sends the display form so MTD needs no DDS-internal mapping.
- DDS must leave out the dealer-change marker (40, DDS `Assets/Scripts/History/BaseHistory.cs:22`). It is a history decoration, not a result.
- `Multiplier`:
  - Roulette Xtra: the winning multiplier when the result was a bonus number, otherwise left out. DDS stores this per history entry as `HistoryResult.winningNumberBonus`, with 0 meaning none (DDS `Assets/Scripts/EventController.cs:1787-1792`).
  - Power Reel: the game's multiplier, present on every result (`RouletteXtra2HistoryResult.Multiplier`, DDS `Assets/Scripts/History/Roulette Xtra 2 History/RouletteXtra2History.cs:8-14`).
  - Standard roulette: never present.
- MTD derives the pocket colour from the number (standard red and black sets; every zero is green).
- `HotNumbers` and `ColdNumbers` are pocket labels in the same display form.
- `Statistics` keys, all counts over the table's statistics window: `SampleSize`, `Red`, `Black`, `Zero`, `Odd`, `Even`, `Low` (1 to 18), `High` (19 to 36), `Dozen1` to `Dozen3`, `Column1` to `Column3`. Zeros count only towards `Zero`. MTD turns counts into percentages.

### SicBo (`SICBO`)

- `Result` is the three dice in ascending order joined by hyphens: `"1-4-4"`. DDS holds the result as a three-digit integer with the dice already sorted (144) and has `Utility.GetSicBoDiceNumbersFromWinningNumber` to split it (DDS `Assets/Scripts/Utility.cs:123-130`).
- DDS must leave out invalid results (777).
- MTD derives the total, and small / big / triple, from the dice. It uses DDS's rule: big is a total above 10, small is 10 or below, and a triple is neither.
- `HotNumbers` and `ColdNumbers` are dice totals (`"4"` to `"17"`), triples excluded. This is a choice made here, because DDS has several hot/cold views for SicBo (faces, doubles, totals). Change it before implementing if another view is wanted.
- `Statistics` keys: `SampleSize`, `Small`, `Big`, `Triple`, `Odd`, `Even`, `Face1` to `Face6` (how many dice showed each face).

### Blackjack (`BLACKJACK`)

Reserved. MTD recognises the game type and skips the table until a Blackjack widget exists. When Blackjack is added, its result form and statistics keys are defined here, and any extra per-result fields are added as optional properties.

### Anything else

MTD skips tables whose `GameType` it does not show (`BACCARAT`, `HEADCRACK`, unknown future values) and logs each one once. `GameType` is read as a string so an unknown value never breaks parsing.

## 4. Sizes and limits

| Item | Value | Why |
| --- | --- | --- |
| Results | at most 20 | The first MTD UI shows 12. DDS's per-entry multiplier data exists only in its 31-entry display histories. |
| Hot, cold | at most 8 each | DDS tiles show 4 of each; 8 leaves room for other MTD UIs. |
| Typical payload | 2 to 3 KB | Far below any broker limit. |

MTD shows as many results and hot/cold numbers as the selected MTD UI has room for and ignores the rest.

## 5. Publishing rules (DDS)

1. **When to publish.** After any of these, and on a heartbeat:
   - the table becomes provisioned (the first `tableID` arrives) and on every scene start after that;
   - a result is added, changed or removed (`DataLogger.newWinningNumber`, DDS `Assets/Game State/DataLogger.cs:47`);
   - history is cleared (`EventController.OnHistoryCleared`, DDS `Assets/Scripts/EventController.cs:144`);
   - limits change (`EventController.OnPaytableMinMaxUpdated`, `:143`);
   - the table opens or closes (`EventController.tableStatusChanged`, `:142`);
   - every 10 seconds regardless (heartbeat).
2. **Publish once per frame at most, after the frame's updates.** Mark the summary dirty when a trigger fires and build it in `LateUpdate` or the next frame. `newWinningNumber` can fire before the Xtra and Power Reel history entries hold the new multiplier, so building the message inside the event would send a result without its multiplier.
3. **`GameId` is the game that produced `Results[0]`.** DDS creates a new `GameState.currentGameID` on every move into Place Your Bets (DDS `Assets/Game State/GameState.cs:52-55`). A heartbeat sent during the next betting round must still carry the previous game's ID, so remember the ID when the result is recorded, the way `TriggerPlaceYourBets` already does for the game summary (`completedGameID`, DDS `EventController.cs:758`).
4. **Live only.** Publish with `pStoreWhenDisconnected: false`, as the chipper alert does (DDS `TConnect2.cs:379-382`). The default path stores every message in SQLite and replays it after an outage (`TConnect2.cs:855-874`); replaying old snapshots is useless and delays real traffic.
5. **Retained on MQTT.** Pass `pRetain: true`. NATS has no retained messages (DDS `NatsTConnect2Transport.cs:207`) and DDS's MQTT messages expire after an hour (`MqttTConnect2Transport.cs:31`), which is why the heartbeat is required on both transports.
6. **Publish when provisioned.** An empty or missing display alias must still publish whenever the DDS publisher is otherwise enabled and has a valid table GUID.
7. **Do not depend on which statistics widgets are loaded.** `BaseStatistics.pocketsCount` is filled by statistics widgets, and a package may not have one. Count from `DataLogger.LatestWinningNumbers` directly.

The heartbeat is also the liveness signal: MTD drops a table from the rotation when it has heard nothing for 35 seconds. That timeout is configurable on MTD, but not below 20 seconds, so the heartbeat must not be made slower than 10 seconds without changing MTD too.

## 6. Consumer rules (MTD)

- Match the topic and payload by `TableId`. Use the ordered T-Connect list on NATS or the manual GUID roster on either broker. The Display checkbox selects the alias or the ordinary DDS name and number (for example `AR 01`).
- A table may only describe itself. MTD ignores a message whose `TableId` is not the table ID in the topic it arrived on, so nothing that can publish on one table's topic can change another table's widget.
- Keep the newest message per table. Ignore a message whose `EventTime` is older than the one already held. Every message is compared this way, in the order it arrived; MTD does not assume the last one to arrive is the newest.
- Ignore unknown fields. Treat a missing optional field as absent, never as an error.
- `Results[0]` is the table's current result and is never replaced by an older one. If MTD cannot read it (an unknown pocket, a value that is not three dice), the widget shows the current result as unavailable and keeps the rest of the list as history. An unreadable entry further down the list is not drawn, but it still counts as one of the table's results, in its place, when two lists are compared under the next rule. Otherwise every result below it would be compared with the wrong one.
- A new result is recognised from the result list first, and from `GameId` only when the list cannot decide:
  - "Added" reading: the list MTD already holds reappears one or more places further down the new list. "Edited" reading: the lists are the same length and differ in some places. Whichever reading needs fewer changes wins. `[7, 11, 7]` becoming `[9, 11, 7]` is one edit or two additions, so it is a correction; a history moving down one place is a new result.
  - When both readings need the same number of changes (repeated values at the top of the list, or a list that holds only the current result), an unchanged `GameId` means a correction and a changed one means a new result.
  - A shorter list, or a cleared one, is a correction.
  - MTD does it in this order so that a publisher sending the wrong game's ID can neither make a correction animate as a new result nor stop a new result from animating.
- If `MTDColourSetting` does not parse, use the MTD UI's default colour.
- On MQTT, a retained message is shown immediately, so a table that died within the last hour can appear for up to one timeout after MTD starts. Accepted for v1.

## 7. DDS work list

This is a separate task in the DDS project. Nothing in the MTD passes edits DDS.

| Work | Where |
| --- | --- |
| Replace the old numeric option with `MTD\|Table Unique Name` (free text). Preserve `MTD\|Colour`. Migrate an existing positive number to its string and 0 to blank. | `Configuration.cs`, `ConfigurationMenu.cs` (three-step option workflow), both menu prefabs |
| Keep the existing server `info` schema compatible. If its legacy `MTDTableNumber` string is required, map the display alias at that boundary only. | `Messages/Tables/TableInfoMessage.cs`, `TConnect2.cs:298-312` |
| New `MtdTableSummaryMessage` DTO | `Messages/` |
| Build and publish the summary (rules above) | `TConnect2.cs`, plus a small collector for results, hot/cold and statistics |
| Edit-mode tests for the builder (result forms, markers excluded, multiplier rules) | `Assets/Scripts/Editor/` |

Where each field comes from:

| Field | DDS source |
| --- | --- |
| `TableName`, `TableNumber` | `appearanceConfiguration.tableName`, `.tableNumber` (T-Connect's own table name is the two joined, `TConnect2.cs:88`) |
| `MinBet`, `MaxBet` | `appearanceConfiguration.minMaxGroup.minValues[selectedIndex]`, `.maxValues[selectedIndex]` (`TConnect2.cs:650-660`) |
| `TableOpen` | `EventController.IsTableOpen()` |
| `Results`, standard roulette and SicBo | `DataLogger.LatestWinningNumbers` (oldest first, so reverse it) |
| `Results`, Roulette Xtra | `EventController.historyToSpawnHolder` (oldest first, includes dealer-change markers) |
| `Results`, Power Reel | the `RouletteXtra2History` list (newest first) |
| Hot, cold, statistics | counted from `DataLogger.LatestWinningNumbers`; `BaseStatistics.AccumulateStatistics` and `BaseSicboStatistics.AccumulateStatistics` show the bucket rules |
| `EventTime` | `GetDdsUtcNow()` (`TConnect2.cs:1076`) |

## 8. Related T-Connect pieces

The T-Connect server snapshot already has an MTD model. This message supplies the table data; the server supplies which tables a display should show.

| Piece | Detail | Reference in the snapshot |
| --- | --- | --- |
| Table info | The server's `TableInfoMessage` has optional `MTDTableNumber` and `MTDColourSetting`. When both are present it stores them on the table's MTD slot records. The server field name belongs to its separate info contract. | `Shared/.../Tables/TableInfoMessage.cs`, `Server/.../Handlers/TableInfoHandler.cs:35` |
| MTD as a device | An MTD registers through `provisioning.assign-device` with `DeviceType` `MTD` and its name in `TableName`. The reply's `TableId` is the display ID. | `Server/TConnect.Server.Services/ProvisioningService.cs:124-240`, `Table/TConnect.Table.TestClient/MTDSim.cs` |
| Table list | NATS Key-Value bucket `mtd-table-list`, key `tcsjh.services.tconnect.display.{displayId}.table-list`, value `{ DisplayId, AvailableTableIds }`. | `Server/TConnect.Server.Services/TableService.cs:509-520`, `Shared/.../TopicNameFactory.cs:87` |
| Slot config and overrides | Buckets `mtd-slot-config` and `mtd-slot-override`, one key per slot: type FIXED / ROTATION / MEDIA, rotation interval, tables, media bucket, timed overrides. **Not used by MTD in the first build.** | `TableService.cs:483-507`, `Shared/.../Displays/MtdSlotInfoMessage.cs` |
| Server → table | `tcsjh.services.tconnect.table.{tableId}.mtdtableinfo` carries `{ TableId, TableNumber, ColourSetting }` when an operator edits them in the web UI. DDS does not handle it today. | `TableService.cs:522-527`, `Server/TConnect.Server.WebUI/.../TableDashboardDialog.razor:344` |

## 9. Versioning

- Adding an optional field, a `Statistics` key or a `GameType` value does not change `SchemaVersion`.
- Renaming or removing a field, or changing a field's meaning, raises it.
- Version 2 replaces the v1 number with the optional display-only alias. MTD accepts versions 1 and 2. For a higher version it logs once and still reads the fields it knows.

## 10. Open points

1. **Settings ownership.** DDS supplies the alias and colour. The separate server table-list message contains the ordered GUIDs.
2. **Power Reel variants reported as plain roulette.** `roulette-xtra-2-default` and `roulette-of-dreams-custom` behave as Power Reel but DDS reports them as `ROULETTE` (`TConnect2.cs:89-104`). Under this contract their results still carry multipliers, and MTD shows a multiplier chip whenever one is present, but the header would be branded as standard roulette. Decide on the DDS side whether they should report `ROULETTE_POWER`.
3. **Broker checks not yet done.** That the T-Connect broker accepts the new subject from DDS and delivers it to an MTD login has been read from the snapshot's NATS config (one account, no subject restrictions), not observed.
