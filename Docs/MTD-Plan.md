# DDS Multi-Table Display (MTD): implementation plan

**Status:** Pass 1 is implemented (24 November 2026, branch `pass-1-backend`). Section 10 says what was built, what was checked and where it differs from this plan. Pass 2 has not started, and DDS does not publish the summary yet.

MTD shows the results history of several DDS tables on one 16:9 screen. Each table gets a widget in a slot. The work runs in two passes, each in its own session:

- **Pass 1** is all the code: message handling, connections, table switching, configuration, the Configuration Menu and the framework that MTD UIs plug into. It ends with a working application that shows plain text widgets.
- **Pass 2** is the first real MTD UI, the 4K columns design: graphics, prefabs, widgets and animation.

The message DDS must publish is specified separately in [MTD-Table-Summary-Contract.md](MTD-Table-Summary-Contract.md). Read it before either pass.

Code references point at DDS (`C:\UnityProjects\DDS`) and the T-Connect server snapshot (`Downloads/tcsjh-tconnect-c5329be04546`) as read on 24 November 2026. References marked *(survey)* come from a read-only sweep of DDS and were not all opened line by line; confirm them before relying on a line number.

## 1. Decisions already made

Answered by Garth on 24 November 2026:

| Question | Decision |
| --- | --- |
| Where does MTD get its list of tables? | From the T-Connect server. MTD registers as a device of type MTD and uses the server's per-display table list. Switching between tables stays a local MTD setting. The server's per-slot config (FIXED / ROTATION / MEDIA, overrides) is not used yet, with room left to add it. |
| Which transports? | NATS and MQTT. The server's table list travels over NATS Key-Value, which MQTT cannot read, so in MQTT mode MTD discovers tables from the messages it receives. |
| Which concept is the first MTD UI? | The 3840x2160 six-column design (concept image 3). |

From the original request:

- Roulette, Roulette Xtra and Power Reel are one game, Roulette, with a variant. SicBo is the second game. Blackjack comes later and must not need a redesign.
- 4 or 6 widgets. Landscape now, portrait likely. Widgets may be tall columns or cards depending on the MTD UI.
- MTD UIs must be swappable from the Configuration Menu. No Addressables yet.
- The Configuration Menu looks exactly like DDS's.
- Two switching options: whole pages, or one slot at a time.

## 2. What the evidence says

These findings shape the design. Each is from code, not assumption.

**The T-Connect server already models MTDs.** It has an MTD device type, a per-display table list, per-slot config, and `MTDTableNumber` / `MTDColourSetting` on its table info message. Details and references are in section 8 of the contract. MTD plugs into that model instead of inventing a parallel one.

**NATS has no retained messages.** A core NATS publish reaches only current subscribers (DDS `NatsTConnect2Transport.cs:207`). DDS's MQTT messages are retained but expire after an hour (`MqttTConnect2Transport.cs:31`). So the table message is a full snapshot repeated on a 10-second heartbeat, and MTD treats silence as "table gone".

**DDS's normal publish path stores and replays.** Every stored message goes into SQLite first and is replayed after an outage (DDS `TConnect2.cs:855-874`). Snapshots must use the live-only path.

**DDS decides orientation from the screen.** It checks `Screen.height > Screen.width` and authors each layout natively; nothing is rotated and there is no orientation setting (DDS `EventController.cs:3418-3428` *(survey)*). MTD does the same.

**DDS's Configuration Menu cannot be lifted as it stands.** About 80% of its 5067 lines are DDS options, and its two panel prefabs (one per orientation, kept in step by hand) are DDS-specific. What gives it its look is small: eight control template prefabs in `Assets/Configuration/UI/`, `Prefabs/Scrollview.prefab`, LiberationSans SDF, built-in uGUI sprites and two PNGs *(survey)*. The only project script those templates need is `ScrollViewResetter.cs` (checked: every other script GUID in them is a uGUI or TextMesh Pro component).

**DDS has what the message needs, in three different places.** Plain results are in `DataLogger.LatestWinningNumbers`. Roulette Xtra's winning multiplier per entry is in `EventController.historyToSpawnHolder`. Power Reel's multiplier and chip colour per entry are in its own history list. Hot/cold and statistics are computed inside statistics widgets and not exposed. The contract's DDS work list covers this.

**This machine has no NATS tooling.** `nats-server`, the `nats` CLI and Docker are not installed. Pass 1 needs a broker to test against (see section 9).

## 3. Architecture

```
            DDS tables                         T-Connect server
                │  mtdsummary (per table)            │  assign-device, ping, table list (NATS only)
                ▼                                    ▼
        ┌──────────────────── IMtdTransport ────────────────────┐
        │   NatsMtdTransport │ MqttMtdTransport │ SimulatedTransport │
        └───────────────┬───────────────────────────┬───────────┘
                        │ main-thread queue         │
                        ▼                           ▼
                 TableRegistry               ITableListSource
          (newest snapshot per table,     (T-Connect list │ discovery)
           freshness, change kind)                  │
                        └────────────┬──────────────┘
                                     ▼
                           eligible, ordered tables
                                     ▼
                           RotationController          ← mode, dwell, swap interval, slot count
                                     ▼
                         slot → table assignments
                                     ▼
                         MtdDisplayController          ← selected MTD UI (layout + widget prefabs)
                                     ▼
                       TableWidget per slot (Bind / Refresh)
```

Everything above `MtdDisplayController` is plain C# with no Unity types, driven by an injected clock, so it is covered by edit-mode tests. Everything from `MtdDisplayController` down is the only part an MTD UI touches.

### 3.1 Games and variants

```csharp
public enum GameFamily { Unsupported, Roulette, SicBo, Blackjack }
public enum RouletteVariant { Standard, Xtra, PowerReel }
```

| `GameType` on the wire | Family | Variant |
| --- | --- | --- |
| `ROULETTE`, `ROULETTE_SPREAD`, `PARIMAX_ROULETTE` | Roulette | Standard |
| `ROULETTE_XTRA` | Roulette | Xtra |
| `ROULETTE_POWER` | Roulette | PowerReel |
| `SICBO` | SicBo | |
| `BLACKJACK` | Blackjack (recognised, skipped until a widget exists) | |
| anything else | Unsupported (skipped, logged once) | |

Parsed data is typed per family: `RouletteSnapshot` (variant, results with optional multiplier and chip colour, hot, cold, statistics) and `SicBoSnapshot` (dice results with derived total and small / big / triple, hot and cold totals, statistics), both deriving from `TableSnapshot` (identity, names, MTD number, colour, limits, open flag, times).

The variant only matters at the widget. A roulette result shows its multiplier chip whenever the message carries one; the variant selects branding such as the Power Reel or Xtra logo. An MTD UI may supply one roulette prefab for all variants or a separate prefab for a variant.

Adding Blackjack later means: a `BlackjackSnapshot` and parser, a widget prefab in each MTD UI that supports it, and the contract's Blackjack section. Nothing else changes.

### 3.2 Which tables are shown

`ITableListSource` answers "which table IDs may this display show?".

| Source | Used when | How |
| --- | --- | --- |
| `TConnectTableListSource` | NATS, table list source set to T-Connect (the default) | Registers with `provisioning.assign-device` (`DeviceType` `MTD`, the configured MTD name in `TableName`), retrying every 30 seconds as DDS does (DDS `TConnect2.cs:939-970`). The reply's `TableId` is the display ID. Then watches Key-Value bucket `mtd-table-list`, key `tcsjh.services.tconnect.display.{displayId}.table-list`, and reads `AvailableTableIds`. Pings `ping.ping-server` every 30 seconds so the server sees the display online (DDS `TConnect2.cs:1030-1067`). |
| `DiscoveryTableListSource` | MQTT always; NATS when chosen | Every table that publishes a summary, optionally narrowed by a list of MTD table numbers typed in the menu (`1-6, 19`; blank means all). |

A table is **eligible** when it is in the source's list, has a fresh summary (heard within the timeout), belongs to a family the selected MTD UI has a widget for, and is open (unless "Skip Closed Tables" is off). Eligible tables are ordered by MTD table number (numerically when it parses), then table name, table number and ID.

MTD subscribes once to the wildcard subject and filters by the list. It does not subscribe per table.

### 3.3 Switching between tables

`RotationController` turns the ordered eligible list into slot assignments. It is plain C# and advanced by `Tick(now)`.

**Pages (option 1).** Tables are cut into pages of N (4 or 6). A page shows for the dwell time (default 120 seconds), then the next page shows, wrapping at the end. A short last page leaves its remaining slots empty. With N or fewer tables there is no paging.

**Slot by slot (option 2).** The first N tables show for the dwell time. Then one slot at a time, slot 1 to slot N, takes the table that has waited longest off screen, and the table it replaces joins the back of the waiting queue. After slot N the display holds for the dwell time and the pass repeats. A table is never in two slots, and every table gets equal screen time. With N or fewer tables nothing swaps.

Two settings drive option 2: the dwell time and the **slot swap interval** between one slot's swap and the next. The interval defaults to 5 seconds, which reads as a staggered page turn. Setting it to the dwell time gives "one slot changes every two minutes" instead. See assumption A1.

Changes to the eligible list:

- For the first 12 seconds after the first table appears (one heartbeat plus margin), slots simply follow the ordered list, so the first page settles as tables report in.
- After that, a new table waits for the next switch, unless a slot is empty, in which case it fills it at once.
- A table that stops being eligible leaves its slot immediately. In slot mode the slot is refilled from the waiting queue; in page mode it stays empty until the next page.

### 3.4 MTD UIs

An MTD UI is a self-contained folder plus one definition asset. Swapping UIs changes only which definition is loaded.

```
MtdUiCatalog (ScriptableObject, in Resources)
  entries: id, display name, Resources path        ← strings only, so listing UIs loads no art

MtdUiDefinition (ScriptableObject, one per MTD UI, in Resources)
  id, display name
  layouts: MtdLayout prefabs

MtdLayout (component on a layout prefab root, which is a Canvas with its own CanvasScaler)
  orientation: Landscape | Portrait
  slots: RectTransform[]                           ← 4 or 6; the length is the slot count
  widgets: (GameFamily, optional RouletteVariant) → TableWidget prefab
  emptySlot: optional prefab shown in an unused slot
  defaultColour: used when a table's colour does not parse

TableWidget (abstract MonoBehaviour, on each widget prefab root)
  Bind(TableSnapshot)                              ← a different table now owns this widget: redraw, no animation
  Refresh(TableSnapshot, TableChange)              ← same table, new data: NewResult | Corrected | Updated
  SetColour(Color)
  PlayEnter(), PlayExit()                          ← coroutines for slot and page transitions, instant by default
```

`RouletteTableWidget` and `SicBoTableWidget` are thin typed bases so a UI's widget script receives a `RouletteSnapshot` or `SicBoSnapshot` directly.

How the pieces are chosen at start-up:

1. The UI id comes from configuration. `MtdUiLoader` finds it in the catalog and calls `Resources.Load` for that one definition. Only the selected UI's prefabs and textures load. Moving to Addressables later replaces this one loader.
2. Orientation is `Screen.height > Screen.width`.
3. The layout is the one matching that orientation and the configured slot count. If the UI has no such layout, MTD falls back to the UI's nearest layout and shows a warning on the status display.
4. For each slot assignment, `MtdDisplayController` picks the widget prefab for the table's family (preferring a variant-specific entry), creates it in the slot on first use, keeps it for reuse, and calls `Bind`, `SetColour` and the transitions.

Because the widget prefabs hang off the layout, the same UI can use tall column widgets in a six-across landscape layout and card widgets in a portrait grid.

**Debug UI.** A UI built entirely in code (a canvas, N slots, one text widget that prints the snapshot). It exists so Pass 1 can prove the whole pipeline without art, and it stays as the fallback when the configured UI is missing.

### 3.5 Configuration

**File.** `C:\tcs\mtd\MtdConfig.json`, written with `JsonUtility` in the way DDS writes `C:\tcs\dds\DisplayConfig.json`: write a temp file, validate it, replace the real file, keep `MtdConfig.last-known-good.json`. On a failed read MTD falls back to the backup, then to defaults *(DDS behaviour from survey, `Configuration.cs:52-77, 1311-1369, 1737-1835`)*.

**Applying.** "Confirm & Save" writes the file, then `MtdApp` tears down the connection, the table state and the MTD UI and builds them again from the new file. Nothing applies piecemeal. (DDS reloads its scene for the same purpose; rebuilding in place has the same effect without depending on the build's scene list.)

**Options.** Keys follow DDS's pipe convention; the label is the text after the last pipe.

| Key | Type | Default | Notes |
| --- | --- | --- | --- |
| `T-Connect\|Connect` | checkbox | off | |
| `T-Connect\|URL` | text | | |
| `T-Connect\|Port` | number | blank | Blank means 4222 for NATS, 1883 for MQTT, as in DDS. |
| `T-Connect\|Username`, `T-Connect\|Password` | text | | Stored in plain text, as in DDS. |
| `T-Connect\|Site Name` | text | blank | MQTT only. Lower-cased; blank means `testsite`. |
| `T-Connect\|TLS` | checkbox | off | Accepts any server certificate, the same policy as DDS. |
| `T-Connect\|Use NATS` | checkbox | on | Off means MQTT. |
| `T-Connect\|MTD Name` | text | `MTD 01` | The name this display registers under. |
| `T-Connect\|Device ID` | read-only | generated | See assumption A3. |
| `Tables\|Table List Source` | dropdown | T-Connect | T-Connect or Discover. Forced to Discover in MQTT mode. |
| `Tables\|MTD Table Numbers` | text | blank | Discover only. Ranges and commas; blank means all. |
| `Tables\|Table Timeout (s)` | number | 35 | Not below 20: tables report every 10 seconds. |
| `Tables\|Skip Closed Tables` | checkbox | on | |
| `Display\|MTD UI` | dropdown | first in catalog | Lists the catalog. |
| `Display\|Number Of Tables` | dropdown | 6 | 4 or 6. |
| `Display\|Demo Mode` | checkbox | off | Simulated tables, no broker. |
| `Display\|Demo Tables` | number | 12 | Demo Mode only. How many tables are simulated. |
| `Switching\|Mode` | dropdown | Pages | Pages or Slot By Slot. |
| `Switching\|Dwell Time (s)` | number | 120 | |
| `Switching\|Slot Swap Interval (s)` | number | 5 | Slot By Slot only. |

**The menu.** A new `MtdConfigurationMenu` script of a few hundred lines, not a port of DDS's script. It keeps what makes DDS's menu recognisable:

- the copied DDS control templates, unchanged, so the look is identical (orange background, TCS watermark, tab row, white-on-alpha sub panels, black 36-point labels, white Cancel and Confirm & Save buttons);
- the pipe-named option keys;
- the **C** key to open it, a draft copy edited by the controls, Cancel to discard, Confirm & Save to write and reload.

It differs in two ways. Rows are created at run time from the templates, one per registered option, so adding an option is one line of code and no prefab edit. And there is one panel that stretches to the screen instead of a prefab per orientation. Both remove the hand-synchronisation that forced DDS to clone rows at run time (`EnsureTConnectNatsCheckbox`, DDS `ConfigurationMenu.cs:3227-3251` *(survey)*).

MTD uses the new Input System only, so the menu reads the key through it. DDS's `UITabNavigator` uses the legacy input API and is not copied.

### 3.6 Folder layout

```
Assets/
  MTD/
    MTD.asmdef
    Contract/        wire DTOs: MtdTableSummaryMessage, T-Connect request and reply types
    Model/           GameFamily, RouletteVariant, snapshots, parsers, roulette pocket colours
    Core/            TableRegistry, table list sources, RotationController
    Transport/       IMtdTransport, NATS, MQTT and simulated implementations, subject builder
    TConnect/        device assignment, ping, Key-Value table list
    Config/          MtdConfiguration, MtdConfigurationMenu
    UI/              catalog, definition, layout, TableWidget bases, MtdDisplayController, debug UI
    App/             MtdApp (bootstrap), main-thread queue
    Plugins/         NATS.Client.dll, MQTTnet.dll (copied from DDS with their .meta files)
    Resources/MtdUis/   catalog asset and one definition asset per UI
    UIs/Columns4K/      Pass 2: everything belonging to the first MTD UI
    Tests/Editor/       MTD.Tests.asmdef, tests, sample payloads
  Configuration/     DDS menu templates (copied with .meta files) and the MTD panel shell
  Scenes/MTD.unity   one bootstrap object
Docs/                this plan and the contract
```

Two assembly definitions: `MTD` for run time and `MTD.Tests` for edit-mode tests. DDS has none, but MTD is a new project and the test assembly needs something to reference.

## 4. Pass 1: all the code

Each step lists what is built and how it is checked. Compile checks use `unity recompile --project-path .`; tests use `unity test . --mode EditMode`. Neither command has been exercised in this project yet, so read `unity <command> --help` on first use.

| Step | Build | Check |
| --- | --- | --- |
| 1.0 Project setup | Add `com.unity.nuget.newtonsoft-json` and `com.unity.test-framework` to the manifest (both are already present as indirect dependencies). Import TMP Essential Resources. Copy `NATS.Client.dll` and `MQTTnet.dll` from DDS. Create the two assembly definitions, the folder layout and `Scenes/MTD.unity`; make it the only scene in the build. Player settings: run in background, fullscreen at native resolution, product name. | Editor compiles clean. An empty test runs. |
| 1.1 Contract and model | `MtdTableSummaryMessage` and result DTO exactly as the contract. `GameType` mapping. Snapshot parsers for roulette and SicBo. Roulette pocket colour helper. Sample payload files (standard, Xtra, Power Reel, SicBo, unknown game, malformed). | Tests: each sample parses to the expected snapshot; unknown game type, missing optional fields, bad colour and bad result strings do not throw. |
| 1.2 Table registry | Newest snapshot per table. Rejects older `EventTime`. Marks a table stale after the timeout. Classifies changes: new table, new result, correction, update, heartbeat. | Tests with an injected clock: ordering, staleness, change classification. |
| 1.3 Rotation | `RotationController` with page and slot-by-slot strategies and the list-change rules in 3.3. | Tests: 12, 14 and 5 tables with 6 and 4 slots; a full slot-by-slot pass; a table leaving and joining mid-dwell; the settle window. |
| 1.4 Transports | `IMtdTransport` (start, subscribe, request, connection state, dispose). NATS adapted from DDS's `NatsTConnect2Transport` (first-connection retry, reconnect events, TLS), without its publish queue because MTD only subscribes and makes requests. MQTT adapted from `MqttManagerLite` (connect, reconnect, resubscribe). Simulated transport that emits contract JSON for a configurable set of fake tables. Subject builder for both forms. Main-thread queue that keeps only the newest pending message per table. | Tests: subject building. Demo Mode feeds the registry in play mode. Live: connect to a broker and receive a published sample (needs a broker, section 9). |
| 1.5 T-Connect client | Device assignment, ping, and the Key-Value table list watch, NATS only. **Do the Key-Value watch first, as a spike.** The NATS client DLL contains the Key-Value API (checked by symbol search), but it has not been run under Unity's Mono. If it does not work, stop and report before building on it. | Live against a T-Connect server or a local JetStream-enabled `nats-server`: the display registers, a table list put into the bucket arrives, an update to it arrives. |
| 1.6 Table list sources | Both sources, the MTD-number filter parser, eligibility and ordering. | Tests: filter parsing, eligibility rules, ordering including non-numeric MTD numbers. |
| 1.7 Configuration | `MtdConfiguration` data, defaults, load with fallback, save with backup. A device ID generated once and kept. | Tests: round trip, corrupt file falls back to the backup, missing file gives defaults. |
| 1.8 UI framework | Catalog, definition, layout, `TableWidget` and typed bases, `MtdUiLoader`, `MtdDisplayController` (widget reuse per slot, colour, transitions), and the code-built debug UI. | Play mode in Demo Mode: the debug UI shows 6 then 4 slots filling and updating. |
| 1.9 Application | `MtdApp` wires configuration, transport, registry, list source, rotation and display. Status model (connection, assignment state and code, tables listed, tables reporting) shown by the debug UI. Scene reload on apply; clean disposal. | Play mode in Demo Mode with 12 tables: page mode pages every dwell; slot mode swaps in order. Use a short dwell for the check. Enter and exit play mode twice without leaked connections or log errors. |
| 1.10 Configuration Menu | Copy the DDS templates, `Scrollview.prefab`, `ScrollViewResetter.cs`, the tooltip and watermark images and the LiberationSans material, with their `.meta` files. Build the panel shell. `MtdConfigurationMenu` creates rows from the option list. | Play mode: **C** opens it; every option shows its stored value; Cancel discards; Confirm & Save writes the file and reloads; side by side with DDS's menu it looks the same. Check at a landscape and a portrait Game view size. |

**Pass 1 is done when** edit-mode tests pass, the Editor compiles clean, Demo Mode runs both switching modes on the debug UI at 4 and 6 slots, and the menu round-trips every option. The live checks in 1.4 and 1.5 are done if a broker is available and are otherwise reported as not done.

**Not in Pass 1:** any artwork or styled widget, the server's slot config and media slots, Blackjack, Addressables, and any change to DDS.

## 5. Pass 2: the first MTD UI, "4K columns"

A new session. Start by reading this plan, the contract, and the Pass 1 code under `Assets/MTD/UI/`.

**What the concept shows.** Six columns on a 3840x2160 canvas. Each column has a header card (game title in gold, table label, variant logos either side of the label for Power Reel and Xtra, minimum and maximum) and a body tinted in the table's colour.

- Roulette body: one large current result (red digits for red, silver for black, green for zeros), then 11 history results as numbered balls in three lanes: red left, green centre, black right. A small chip at the ball's top right carries the multiplier when there is one.
- SicBo body: three large dice, the total in gold and a small / big character, then 11 history rows of three small dice, total and small / big character.

| Step | Build | Check |
| --- | --- | --- |
| 2.0 Art intake | Collect the items in section 9. Decide the canvas: reference 3840x2160, scale with screen size. | Sprites imported at 4K with sensible compression; fonts made into TMP assets including the characters the design needs. |
| 2.1 Layout, six across | `Columns4K` definition and a landscape six-slot layout prefab. Catalog entry. | Selected from the menu; empty slots show the empty-slot visual. |
| 2.2 Roulette widget | Header, current result, three-lane history, multiplier chip, variant logos, colour tint. One prefab for all three variants unless the art differs. | Demo Mode tables of each variant. Compare with the concept at 3840x2160 and at 1920x1080. |
| 2.3 SicBo widget | Header, current dice, total and small / big / triple, history rows. | Demo Mode. Totals and small / big come from the dice, so the concept's inconsistent sample values will not reproduce. |
| 2.4 Motion | New result arrives: current value changes and history steps down. Slot swap and page turn transitions. | Both switching modes with a short dwell. No frame drops at 4K with six widgets updating. |
| 2.5 Status and empty states | Empty slot, "waiting for tables", connection lost, not assigned (show the assignment code). | Disconnect the broker; start with no table list. |
| 2.6 Four across | A landscape four-slot layout using the same widgets, if the art stretches. | Menu set to 4. |
| 2.7 Live check | Run against DDS tables publishing the summary. | One table of each kind, a result, a correction, a limit change, a table closing. |

Screenshots for the checks can be taken through the Unity CLI or MCP from the running Editor.

Portrait layouts are not in the first UI because there is no portrait design yet. The framework already selects a portrait layout when one exists.

Notes for whoever builds it:

- Tinting works by multiplying a neutral, light background sprite by the table colour, with shading as a separate overlay. Ask for the backgrounds in that form.
- The concept's header reads "MIN $10 / MIN $10,000". The second label should be MAX. The text comes from `MinBetText` and `MaxBetText`.
- The small / big characters (小, 大) need a font with those glyphs, or sprites.
- Give each slot its own nested canvas so one widget's update does not rebuild the others.

## 6. DDS work

DDS must publish the summary for anything real to appear. That work is listed in section 7 of the contract and is a separate task in the DDS project. Neither MTD pass edits DDS. Until it exists, MTD is exercised with Demo Mode and with the sample payloads published by hand.

## 7. Assumptions to confirm

These were chosen from the evidence instead of asked. Each is cheap to change before Pass 1 and more expensive after.

| | Assumption | If wrong |
| --- | --- | --- |
| A1 | In slot-by-slot mode the slots swap 5 seconds apart, then everything holds for the dwell time. | It is a setting, so only the default changes. |
| A2 | Closed tables are left out of the rotation by default. | Flip the default of "Skip Closed Tables". |
| A3 | MTD's device ID is a GUID generated on first run and kept in the config file. DDS instead takes its ID from the licence hardware-ID tool. | If MTD must be licensed or identified the same way, step 1.7 calls that tool instead. |
| A4 | A short last page shows empty slots instead of wrapping round to refill. | One rule in the page strategy. |
| A5 | The summary's subject is `…/table/{TableId}/mtdsummary` and the MTD number field is named `MTDTableNumber`. | Rename in the contract before either side implements it. |
| A6 | In MQTT mode MTD does not register with T-Connect at all. | Add assignment and ping over MQTT request/reply. |
| A7 | The heartbeat is 10 seconds and the table timeout 35 seconds. | Change both numbers together. |
| A8 | The Configuration Menu is one stretching panel with rows created in code, not a DDS-style prefab per orientation. | Author the prefabs instead; the script's binding changes. |

## 8. Risks and things not verified

- **Key-Value under Unity.** The table list depends on the NATS client's Key-Value API working under Mono. Step 1.5 tests this first.
- **The server snapshot is from October 2026.** Bucket names, keys and the table list message were read from it. The test client in the same snapshot reads the table list as a different class from the one the server writes, though both have `AvailableTableIds`, which is the only field MTD reads. Re-check against the current server before a site install.
- **JSON casing from the server.** The server serialises with System.Text.Json. MTD reads with Newtonsoft, which matches property names without regard to case, so either casing works, but this has not been observed.
- **Nothing has run against a broker.** Subjects, wildcard delivery and broker permissions are read from code and the snapshot's NATS config.
- **Copied prefabs across Unity versions.** DDS is on Unity 6000.0.35f1 and MTD on 6000.6.10f1. The templates use only uGUI and TextMesh Pro components, and LiberationSans SDF keeps the same GUID once TMP Essentials is imported, but the import has not been tried.
- **4K fill rate.** Six full-height tinted columns with many overlapping sprites at 3840x2160 should be fine on a PC, but it is unmeasured.

## 9. Needed before each pass

**Before Pass 1**

- A broker to test against: either a reachable T-Connect development server, or agreement to put `nats-server` and the `nats` CLI on this machine.
- A decision on putting the project under git first. It is not a repository today, and a commit per step makes Pass 1 reviewable.
- Any corrections to section 7.

**Before Pass 2**

- The design's source artwork, split into layers: column backgrounds in neutral tones, header card, red / black / green balls, multiplier chips, dice faces, the Power Reel and Roulette Xtra logos.
- The fonts for the gold titles and the large result numerals, with licences that allow embedding.
- What the header shows exactly: whether the table label is the table number or the MTD number, and where the table name (for example "SB") appears.
- How triples look in the SicBo widget.
- Whether a four-across version of this design is wanted, and any portrait design.

## 10. Pass 1 result

Built on 24 November 2026 on branch `pass-1-backend`, one commit per step. `main` holds only the baseline.

### How to run it

- Open `Assets/Scenes/MTD.unity` and press Play. The configuration left in `C:\tcs\mtd\MtdConfig.json` has Demo Mode on, so twelve simulated tables appear on the debug UI.
- **C** opens the Configuration Menu.
- `MTD > Setup` in the Editor's menu bar rebuilds the generated assets: the menu prefab, the scene, the build and player settings.
- Edit-mode tests cannot be run with `unity test` while the Editor has the project open. Use `unity command test run --project-path . --mode editor`.
- The live NATS tests are marked Explicit and need a local server first: `nats-server -js -p 14222`, then run the tests in category `Live`. NATS Server 2.15.3 and the NATS CLI 0.5.2 were installed with winget for this.

### What was checked

| Check | Result |
| --- | --- |
| Edit-mode tests | 157 pass: message parsing, change classification, staleness, table ordering and filtering, both switching modes, subject mapping, simulated tables, configuration persistence. |
| Live NATS tests | 5 pass against nats-server 2.15.3 inside the Editor: wildcard subscription, request with no responder, Key-Value read and watch, and the T-Connect client registering, pinging, reading its table list and picking up a change to it. The test plays DDS and the T-Connect server. |
| App against a NATS server | The four sample summaries published with the `nats` CLI on `tcsjh.table.{id}.mtdsummary` appeared in MTD-number order. Blackjack, an unknown game and a malformed payload were skipped with one log line each. Tables dropped out 35 seconds after the last message. Stopping and restarting the server showed the lost connection and then recovered. |
| Demo Mode | Pages with 12 tables and 6 slots; slot by slot with 7 tables and 4 slots. Landscape and portrait Game views. Entering and leaving play mode repeatedly left no errors. |
| Configuration Menu | Every option shows its saved value; dependent rows grey out; Cancel discards; Confirm & Save writes the file and rebuilds; confirming with no changes writes nothing. Compared side by side with DDS's own menu prefab rendered in the same Game view. |

### What was not checked

- **MQTT.** `MqttMtdTransport` compiles and follows DDS's `MqttManagerLite`, but there is no MQTT 5 broker on this machine, so it has never connected to anything.
- **A real T-Connect server.** Registration and the table list were exercised only against the test's stand-in, which was written from the October 2026 server snapshot.
- **A real DDS table.** DDS does not publish the summary yet.
- **TLS**, on either transport.
- **A standalone build.** Everything ran in the Editor.
- **Keyboard and mouse by hand.** The CLI's key simulation does not reach the Editor's play mode, so the **C** key was exercised by queueing an Input System event, and the controls through their own change events. Nobody has clicked through the menu with a mouse.

### Where the build differs from this plan

- **Applying a configuration rebuilds in place** instead of reloading the scene (section 3.5 is updated).
- **A new result is recognised from the shape of the result list**, not from a change of `GameId` (contract section 6 is updated). This does not depend on DDS reporting the right game's ID.
- **The table list is read two ways**: by a Key-Value watch, and by a direct read every 30 seconds in case the watch stops silently.
- **The table timeout cannot be set below 20 seconds.** A value shorter than the heartbeat made healthy tables drop out.
- **`Display|Demo Tables` was added** so Demo Mode can be run with any number of tables.
- **The menu's panels have no background of their own.** DDS's panels carry Image components that are switched off, so only the orange background and the TCS watermark are drawn. The MTD menu was first built with those images on and looked different; it now matches.
- **The scene and the menu prefab are generated by editor code** (`Assets/MTD/Editor`), not assembled by hand, so they can be rebuilt exactly.
- **DDS's NATS socket wrapper was not copied.** It exists to bound blocked writes when publishing, and MTD does not publish.
- **Player settings were changed**: company name `TCS JohnHuxley` (as DDS), product name `DDS Multi-Table Display`, run in background, fullscreen window at native resolution. Rename the product if it should be called something else.
- **The template's `SampleScene`, `TutorialInfo` and `Readme` were left in the project.** They are no longer in the build.

### What Pass 2 starts from

A new MTD UI is a folder of prefabs plus two assets, with no change to the code above the widgets:

1. Widget scripts deriving from `RouletteTableWidget` and `SicBoTableWidget` (`Assets/MTD/UI/TableWidget.cs`). `DebugTableWidget` shows every field a snapshot offers.
2. A layout prefab per orientation and slot count: a Canvas with an `MtdLayout` listing its slots and widget prefabs, and optionally an `MtdStatusView` and an empty-slot prefab.
3. An `MtdUiDefinition` asset listing the layouts, in a `Resources` folder.
4. An `MtdUiCatalog` asset at `Resources/MtdUis/MtdUiCatalog` with an entry pointing at the definition. With a blank `Display|MTD UI` setting, the first catalog entry is what loads.
