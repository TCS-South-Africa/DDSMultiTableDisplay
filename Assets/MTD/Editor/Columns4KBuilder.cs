using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MTD.Editor
{
    // All coordinates are in the approved 3840 x 2160 design. Horizontal anchors let the
    // same widget widen in four-slot mode while dice, balls, chips and logos keep their aspect.
    public static class Columns4KBuilder
    {
        const string ui = "Assets/MTD/UI/Columns4K";
        const string resources = ui + "/Resources/MtdUis";
        const string id = "Columns4K";
        static readonly Color gold = new Color32(250, 211, 114, 255);
        static readonly Color brass = new Color32(178, 127, 48, 255);
        static Dictionary<string, Sprite> art;
        static TMP_FontAsset regular, medium, semibold;
        static Material lights, artwork, lettering, tableLettering, captionLettering;

        [MenuItem("MTD/Setup/5. Build 4K Columns")]
        public static void Build()
        {
            var original = SceneManager.GetActiveScene();
            var staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(staging);
                BuildAssets();
            }
            finally
            {
                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(staging, true);
            }
        }

        static void BuildAssets()
        {
            Directory.CreateDirectory(resources);
            AssetDatabase.Refresh();
            medium = FontAsset("Medium");
            semibold = FontAsset("SemiBold");
            regular = FontAsset("Regular");
            lights = LightsMaterial();
            artwork = MaterialAsset(ui + "/Art/Gilded Artwork.mat", "MTD/Columns/Gilded Artwork");
            lettering = GoldMaterial(semibold, "Prompt Gold");
            tableLettering = GoldMaterial(regular, "Prompt Table Gold", sweep: true);
            captionLettering = GoldMaterial(regular, "Prompt Caption", sweep: true, plain: true);
            art = new[] { "Columns", "Pockets" }
                .SelectMany(sheet => AssetDatabase.LoadAllAssetsAtPath(ui + "/Art/" + sheet + ".png"))
                .OfType<Sprite>().ToDictionary(sprite => sprite.name);
            var roulette = BuildRoulette();
            var sicBo = BuildSicBo();
            var empty = BuildEmpty();
            var definition = Asset<MtdUiDefinition>(resources + "/" + id + ".asset");
            definition.id = id;
            definition.displayName = "4K Columns";
            definition.layouts.Clear();
            foreach (int count in new[] { 6, 4 })
                definition.layouts.Add(BuildLayout(count, roulette, sicBo, empty));

            var catalog = Asset<MtdUiCatalog>(resources + "/MtdUiCatalog.asset");
            var entry = catalog.entries.Find(item => item.id == id);
            if (entry == null)
            {
                entry = new MtdUiCatalog.Entry();
                catalog.entries.Add(entry);
            }
            entry.id = id;
            entry.displayName = definition.displayName;
            entry.resourcePath = "MtdUis/" + id;
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            art = null;
        }

        static TMP_FontAsset FontAsset(string weight)
        {
            string stem = ui + "/Fonts/Prompt-" + weight;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(stem + " SDF.asset");
            if (font != null) return font;
            int atlasSize = weight == "Regular" ? 1024 : 2048;
            font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(stem + ".ttf"),
                weight == "Regular" ? 68 : 88, 8, GlyphRenderMode.SDFAA, atlasSize, atlasSize, AtlasPopulationMode.Dynamic, false);
            font.name = "Prompt " + weight;
            var glyphs = Enumerable.Range(32, 352).Concat(Enumerable.Range(0x0E01, 91))
                .Concat(Enumerable.Range(0x2000, 112)).Concat(Enumerable.Range(0x20A0, 49)).Select(n => (uint)n).ToArray();
            font.TryAddCharacters(weight == "Regular" ? glyphs.Where(n => n < 256 || n >= 0x20A0).ToArray() : glyphs);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            if (weight == "Regular") font.fallbackFontAssetTable.Insert(0, medium);
            AssetDatabase.CreateAsset(font, stem + " SDF.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
            EditorUtility.SetDirty(font);
            return font;
        }

        static Material MaterialAsset(string path, string shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        static Material LightsMaterial()
        {
            var material = MaterialAsset(ui + "/Art/Column Lights.mat", "MTD/Columns/Film Lights");
            string texturePath = ui + "/Art/Winning Sparks.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled || importer.maxTextureSize != 4096)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 4096;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            material.SetTexture("_Burst", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(material);
            return material;
        }

        static RawImage Light(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<RawImage>();
            image.material = lights;
            image.raycastTarget = false;
            return image;
        }

        static Material GoldMaterial(TMP_FontAsset font, string name, bool sweep = false, bool plain = false)
        {
            string path = ui + "/Fonts/" + name + ".mat";
            var material = MaterialAsset(path, "TextMeshPro/Distance Field");
            material.mainTexture = font.atlasTexture;
            foreach (string key in new[] { "_GradientScale", "_TextureWidth", "_TextureHeight", "_WeightNormal", "_WeightBold" })
                material.SetFloat(key, font.material.GetFloat(key));
            material.SetTexture("_FaceTex", plain ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(ui + "/Art/Letter Gold.png"));
            if (sweep) material.EnableKeyword("MTD_HEADER_SHEEN");
            else material.DisableKeyword("MTD_HEADER_SHEEN");
            if (plain) material.DisableKeyword("UNDERLAY_ON");
            else material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(0,0,0,.85f));
            material.SetFloat("_UnderlayOffsetX", .5f);
            material.SetFloat("_UnderlayOffsetY", -.6f);
            material.SetFloat("_UnderlaySoftness", .32f);
            material.SetFloat("_OutlineWidth", plain ? 0 : .035f);
            material.SetColor("_OutlineColor", new Color32(221,179,82,255));
            EditorUtility.SetDirty(material);
            return material;
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static GameObject Save(RectTransform root)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, ui + "/" + root.name + ".prefab");
            Object.DestroyImmediate(root.gameObject);
            return prefab;
        }

        static RectTransform Root(string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.sizeDelta = new Vector2(622, 2160);
            return rect;
        }

        // A horizontal span with optional insets, measured down from its parent's top.
        static RectTransform Span(Transform parent, string name, float left, float right, float top, float height, float inset = 0)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(left, 1);
            rect.anchorMax = new Vector2(right, 1);
            rect.offsetMin = new Vector2(inset, -top-height);
            rect.offsetMax = new Vector2(-inset, -top);
            return rect;
        }

        static RectTransform Box(Transform parent, string name, float centre, float top, float width, float height)
        {
            return Span(parent, name, centre, centre, top, height, -width/2);
        }

        static Image Image(RectTransform rect, string sprite = null, bool preserve = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            if (sprite != null) image.sprite = art[sprite];
            image.preserveAspect = preserve;
            return image;
        }

        static Image Picture(Transform parent, string sprite, float centre, float top, float width, float height)
            => Image(Box(parent, sprite, centre, top, width, height), sprite, true);

        static TMP_Text Text(RectTransform rect, float size, Color colour, bool bold = false)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = bold ? semibold : medium;
            text.fontSize = text.fontSizeMax = size;
            text.fontSizeMin = Mathf.Min(16, size);
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.color = colour;
            text.raycastTarget = false;
            text.richText = false;
            text.parseCtrlCharacters = false;
            text.text = "";
            return text;
        }

        static Image Panel(RectTransform root, string name, int y, int height)
        {
            var tint = Image(Span(root, name + " tint", 0, 1, y, height), name + " Tint");
            Image(Span(root, name + " frame", 0, 1, y, height), name + " Frame");
            return tint;
        }

        static ColumnView BuildColumn(RectTransform root, bool roulette)
        {
            var view = root.gameObject.AddComponent<ColumnView>();
            view.surface = root.gameObject.AddComponent<CanvasGroup>();
            view.headerTint = Panel(root, "Header", 18, 342);
            view.bodyTint = Panel(root, "Body", 374, 1770);
            // The engraving is cut at a fixed design size, so the wheel stays circular in four-slot mode.
            var plate = Box(root, "Engraved backplate", 0, 403, 352, 290);
            plate.anchoredPosition += Vector2.right * (11 + 176);
            view.backplate = plate.gameObject.AddComponent<RawImage>();
            view.backplate.raycastTarget = false;
            view.backplate.material = MaterialAsset(ui + "/Art/Engraved Backplate.mat", "MTD/Columns/Engraved Backplate");
            view.backplate.texture = Engraving(roulette ? "Wheel" : "Dice");
            Picture(root, roulette ? "Roulette Title" : "Sic Bo Title", .516f, 72, roulette ? 493 : 347, 103).material = artwork;
            var divider = Image(Span(root, "Divider", .12f, .88f, 176, 2));
            divider.color = gold;
            divider.material = artwork;
            view.leftLogo = Image(Box(root, "Variant left", .15f, 184, 128, 112), preserve:true);
            view.rightLogo = Image(Box(root, "Variant right", .85f, 184, 128, 112), preserve:true);
            view.powerReel = art["Power Reel"];
            view.xtra = art["Roulette Xtra"];
            view.tableLabel = Text(Span(root, "Table name and number", 0, 1, 184, 88), 55, Color.white, true);
            view.tableLabel.characterSpacing = 6;
            view.tableLabel.font = regular;
            view.tableLabel.fontSharedMaterial = tableLettering;
            view.reflection = Text(Span(root, "Table reflection", 0, 1, 221, 88), 55, new Color(.76f,.64f,.43f,.13f));
            view.reflection.font = regular;
            view.reflection.fontSharedMaterial = captionLettering;
            view.reflection.rectTransform.localScale = new Vector3(1,-.86f,1);
            view.reflection.characterSpacing = 6;
            view.reflection.enableVertexGradient = true;
            view.reflection.colorGradient = new VertexGradient(Color.clear, Color.clear, Color.white, Color.white);
            view.minimum = Limit(root, "MIN", .13f, .50f);
            view.maximum = Limit(root, "MAX", .50f, .89f);
            // The frame sprite stretches with the slot. Its light must use the same
            // horizontal scale, including the transparent margin outside the gold edge.
            const float lightMargin = 9f / 622f;
            view.headerLight = Light(Span(root, "Header lights", -lightMargin, 1+lightMargin, 7, 364));

            view.current = Span(root, "Current", 0, 1, 396, 300);
            view.currentState = Text(Span(view.current, "Result status", 0, 1, 75, 125, 20), 43, gold);
            if (roulette)
                view.historyFields = new[] { .18f, .5f, .82f }.Select(centre =>
                    Image(Span(root, "Lane", centre-.144f, centre+.144f, 707, 1428), "Lane")).ToArray();
            var mask = Span(root, "History window", 0, 1, 704, 1432, roulette ? 0 : 8);
            mask.gameObject.AddComponent<RectMask2D>();
            view.history = Span(mask, "History", 0, 1, 0, 1432);
            view.history.pivot = new Vector2(.5f, 1);
            view.history.anchoredPosition = Vector2.zero;
            return view;
        }

        static TMP_Text Limit(RectTransform root, string caption, float left, float right)
        {
            RectTransform group = Span(root, caption, left, right, 286, 48);
            var line = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.childAlignment = caption == "MIN" ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            line.childControlWidth = line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.spacing = 10;
            var label = Text(Span(group, "Label", 0, 0, 0, 48), 32, gold);
            label.text = caption;
            label.font = regular;
            label.fontSharedMaterial = captionLettering;
            var value = Text(Span(group, "Amount", 0, 0, 0, 48), 32, Color.white);
            return value;
        }

        static Texture2D Engraving(string game)
        {
            string path = ui + "/Art/" + game + " Engraving.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.sRGBTexture || importer.npotScale != TextureImporterNPOTScale.None ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.sRGBTexture = false; // R and G hold the two edges of the cut.
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static ColumnsRouletteWidget.Chip Chip(RectTransform parent, float centre, float y, float size)
        {
            float tile = size * 1.18f; // The film chip has a soft shadow outside its printed diameter.
            var rim = Picture(parent, "Chip Rim", centre, y+(size-tile)/2, tile, tile);
            Picture(rim.transform, "Chip Centre", .5f, 0, tile, tile);
            var value = Text(Span(rim.transform, "Multiplier", .11f, .89f, tile*.1f, tile*.8f), size*.39f, Color.white);
            value.font = regular;
            return new ColumnsRouletteWidget.Chip { rim = rim, value = value };
        }

        static ColumnsRouletteWidget BuildRoulette()
        {
            var root = Root("Roulette Column");
            var view = BuildColumn(root, true);
            var widget = root.gameObject.AddComponent<ColumnsRouletteWidget>();
            widget.column = view;
            widget.pockets = PocketArt("Pocket ");
            widget.balls = PocketArt("Ball ");
            widget.currentPocket = Image(Span(view.current, "Winning pocket", .025f, .975f, 5, 296), preserve:true);
            view.chipBurst = Light(Box(view.current, "Winning sparks", .826f, -40, 256, 256));
            view.chipBurst.enabled = false;
            widget.currentChip = Chip(view.current, .826f, 25, 126);
            view.currentParts = new[] { widget.currentPocket.rectTransform, widget.currentChip.rim.rectTransform };
            widget.rows = new ColumnsRouletteWidget.HistoryRow[ColumnView.HistoryLength];
            view.historyRows = new RectTransform[widget.rows.Length];
            for (int i = 0; i <= widget.rows.Length; i++)
            {
                var band = Span(view.history, "Spin " + (i+1), 0, 1, 31+i*ColumnView.RoulettePitch, 124);
                Image ball = Image(Box(band, "Ball", .5f, 6, 112, 112), preserve:true);
                var row = new ColumnsRouletteWidget.HistoryRow { ball = ball, chip = Chip(ball.rectTransform, .98f, 3, 51) };
                if (i < widget.rows.Length) { widget.rows[i] = row; view.historyRows[i] = band; }
                else { widget.tail = row; view.rouletteTail = band; band.gameObject.SetActive(false); }
            }
            Image token = Image(Box(root, "Winner travelling to history", .5f, 0, 112, 112), preserve:true);
            widget.flying = new ColumnsRouletteWidget.HistoryRow { ball = token, chip = Chip(token.rectTransform, .98f, 3, 51) };
            token.gameObject.SetActive(false);
            return Save(root).GetComponent<ColumnsRouletteWidget>();
        }

        static Sprite[] PocketArt(string prefix)
        {
            var labels = Enumerable.Range(0,37).Select(n => n.ToString()).Concat(new[] { "00", "000", "0000" });
            return labels.Select(label => art[prefix + label]).ToArray();
        }

        static ColumnsSicBoWidget BuildSicBo()
        {
            var root = Root("Sic Bo Column");
            var view = BuildColumn(root, false);
            var widget = root.gameObject.AddComponent<ColumnsSicBoWidget>();
            widget.column = view;
            widget.faces = Enumerable.Range(1,6).Select(face => art["Die " + face]).ToArray();
            widget.small = art["Small"];
            widget.big = art["Big"];
            widget.triple = art["Triple"];
            widget.currentDice = DiceRow(Span(view.current, "Winning dice", 0, 1, 0, 300), true);
            var hero = widget.currentDice;
            view.currentParts = new[] { hero.first.rectTransform, hero.second.rectTransform, hero.third.rectTransform,
                hero.total.rectTransform, hero.outcome.rectTransform };
            widget.rows = new ColumnsSicBoWidget.DiceRow[ColumnView.HistoryLength];
            view.softenHistoryTint = true;
            view.historyFields = new Image[widget.rows.Length];
            view.historyRows = new RectTransform[widget.rows.Length];
            for (int i = 0; i < widget.rows.Length; i++)
            {
                var row = Span(view.history, "Roll " + (i+1), .027f, .99f, i*ColumnView.RowPitch+3, 120);
                view.historyRows[i] = row;
                view.historyFields[i] = Image(row, "Row Field");
                widget.rows[i] = DiceRow(row, false);
            }
            return Save(root).GetComponent<ColumnsSicBoWidget>();
        }

        static ColumnsSicBoWidget.DiceRow DiceRow(RectTransform root, bool hero)
        {
            float size = hero ? 146 : 92;
            float x = hero ? .245f : .102f;
            float dx = hero ? .255f : .174f;
            Image Face(float centre) => Image(Box(root, "Die", centre, hero ? 18 : 14, size, size), preserve:true);
            var total = Text(Span(root, "Total", hero ? .185f : .555f, hero ? .545f : .785f,
                hero ? 135 : -20, hero ? 202 : 160), hero ? 130 : 103, Color.white, true);
            total.alignment = TextAlignmentOptions.Midline;
            total.fontSharedMaterial = lettering;
            float glyphSize = hero ? 112 : 88;
            var outcome = Image(Box(root, "Outcome", hero ? .64f : .885f, hero ? 180 : 16, glyphSize, glyphSize), preserve:true);
            return new ColumnsSicBoWidget.DiceRow { root = root.gameObject, first = Face(x), second = Face(x+dx),
                third = Face(x+2*dx), total = total, outcome = outcome };
        }

        static GameObject BuildEmpty()
        {
            var root = Root("Empty Column");
            var tint = new Color32(50,55,63,255);
            Panel(root, "Header", 18, 342).color = tint;
            Panel(root, "Body", 374, 1770).color = tint;
            Text(Span(root, "Empty", 0, 1, 125, 100, 30), 48, gold).text = "No table";
            return Save(root);
        }

        static MtdLayout BuildLayout(int count, TableWidget roulette, TableWidget sicBo, GameObject empty)
        {
            var root = Root("Columns " + count + " Landscape");
            root.gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840,2160);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var board = Box(root, "Columns", .5f, 0, 3840, 2160);
            board.anchorMin = board.anchorMax = new Vector2(.5f,.5f);
            board.anchoredPosition = Vector2.zero;
            Image(board).color = Color.black;

            var layout = root.gameObject.AddComponent<MtdLayout>();
            layout.slots = new RectTransform[count];
            layout.emptySlotPrefab = empty;
            layout.defaultColour = new Color32(15,84,131,255);
            layout.widgets.Add(new MtdLayout.WidgetEntry { family = GameFamily.Roulette, prefab = roulette });
            layout.widgets.Add(new MtdLayout.WidgetEntry { family = GameFamily.SicBo, prefab = sicBo });
            float pitch = (3840f - 20f)/count;
            for (int i = 0; i < count; i++)
            {
                float centre = (10+(i+.5f)*pitch)/3840;
                var slot = Box(board, "Slot " + (i+1), centre, 0, pitch-14, 2160);
                slot.gameObject.AddComponent<Canvas>();
                layout.slots[i] = slot;
                if (i < count-1) Picture(board, "Diamond", (10+(i+1)*pitch)/3840, 343, 45, 45);
            }

            var seam = Span(board, "Diamond relay", 0, 1, 321.5f, 88, 10);
            var relay = seam.gameObject.AddComponent<DiamondRelay>();
            relay.seam = seam.gameObject.AddComponent<RawImage>();
            relay.seam.raycastTarget = false;
            relay.seam.material = MaterialAsset(ui + "/Art/Diamond Relay.mat", "MTD/Columns/Diamond Relay");
            relay.slots = count;
            relay.seam.enabled = relay.enabled = false;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(layout.pagePresented, relay.Play);

            var panel = Span(board, "Display status", .12f, .88f, 866, 428);
            Image(panel).color = new Color(.015f,.019f,.025f,.97f);
            Image(Span(panel, "Gold rule", .06f, .94f, 4, 3)).color = brass;
            var status = root.gameObject.AddComponent<DebugStatusView>();
            status.headlinePanel = panel.gameObject;
            status.headline = Text(Span(panel, "Headline", .04f, .96f, 34, 360), 72, gold);
            status.headline.textWrappingMode = TextWrappingModes.Normal;
            layout.statusView = status;
            panel.gameObject.SetActive(false);
            return Save(root).GetComponent<MtdLayout>();
        }
    }
}
