using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MTD.Tests
{
    public class Columns4KTests
    {
        GameObject root;
        MtdLayout layout;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Columns test");
            layout = MtdUiLoader.Load("Columns4K", MtdOrientation.Landscape, 6, root.transform).Layout;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [TestCase(4)]
        [TestCase(6)]
        public void CatalogResolvesBothLayoutsAndReportsPortraitFallback(int count)
        {
            var choices = MtdUiLoader.ListUis();
            Assert.That(choices.Any(c => c.Id == "Columns4K" && c.DisplayName == "4K Columns"));
            Assert.That(choices.Any(c => c.Id == MtdUiLoader.DebugUiId));
            var exact = MtdUiLoader.Load("Columns4K", MtdOrientation.Landscape, count, root.transform);
            Assert.That(exact.Warning, Is.Null);
            Assert.That(exact.Layout.SlotCount, Is.EqualTo(count));
            Assert.That(exact.Layout.slots.All(slot => slot.GetComponent<Canvas>() != null));
            Assert.That(exact.Layout.emptySlotPrefab, Is.Not.Null);
            var portrait = MtdUiLoader.Load("Columns4K", MtdOrientation.Portrait, count, root.transform);
            Assert.That(portrait.Warning, Does.Contain("portrait"));
            Assert.That(portrait.Layout.orientation, Is.EqualTo(MtdOrientation.Landscape));
            Assert.That(portrait.Layout.SlotCount, Is.EqualTo(count));
        }

        [Test]
        public void RouletteKeepsLabelsLanesAndChipOwnershipAcrossBindAndCorrection()
        {
            var message = TestSamples.Roulette(27, "0000", "32", "00", "26", "000", "0");
            message.GameType = "ROULETTE_POWER";
            message.TableName = "West";
            message.MTDTableNumber = "291";
            message.MaxBetText = "10.000 €";
            message.Results[0].Multiplier = 50;
            message.Results[1].Multiplier = 100;
            var snapshot = SnapshotParser.Parse(message);
            var widget = (ColumnsRouletteWidget)Object.Instantiate(layout.FindWidgetPrefab(snapshot), root.transform);
            widget.Bind(snapshot, Color.magenta);
            Assert.That(widget.column.tableLabel.text, Is.EqualTo("West 27"));
            Assert.That(widget.column.maximum.text, Is.EqualTo(message.MaxBetText));
            Assert.That(widget.column.leftLogo.sprite.name, Is.EqualTo("Power Reel"));
            Assert.That(widget.currentPocket.sprite.name, Is.EqualTo("Pocket 0000"));
            Assert.That(widget.currentChip.value.text, Is.EqualTo("50"));
            Assert.That(widget.rows[0].chip.value.text, Is.EqualTo("100"));
            var labels = widget.rows.Take(5).Select(row => row.ball.sprite.name);
            CollectionAssert.AreEqual(new[] { "Ball 32", "Ball 00", "Ball 26", "Ball 000", "Ball 0" }, labels);
            Assert.That(widget.rows[0].ball.rectTransform.anchorMin.x, Is.LessThan(.5f));
            Assert.That(widget.rows[1].ball.rectTransform.anchorMin.x, Is.EqualTo(.5f));
            Assert.That(widget.rows[2].ball.rectTransform.anchorMin.x, Is.GreaterThan(.5f));
            message.Results[0].Result = "unreadable";
            widget.Refresh(SnapshotParser.Parse(message), TableChange.NewResult, Color.magenta);
            Assert.That(widget.currentPocket.enabled, Is.False);
            Assert.That(widget.column.currentState.text, Is.EqualTo("Result unavailable"));
            Assert.That(widget.currentChip.rim.gameObject.activeSelf, Is.False);
            Assert.That(widget.rows[0].ball.sprite.name, Is.EqualTo("Ball 32"));

            widget.Bind(TestSamples.RouletteSnapshot(4, "11", "3"), Color.blue);
            Assert.That(widget.column.bodyTint.color, Is.EqualTo(Color.blue));
            Assert.That(widget.column.leftLogo.enabled, Is.False);
            Assert.That(widget.rows[0].chip.rim.gameObject.activeSelf, Is.False);
            Assert.That(widget.rows.Skip(1).All(row => !row.ball.gameObject.activeSelf));
            Assert.That(widget.rows.Skip(1).All(row => row.ball.sprite == null && row.chip.value.text == ""));
            widget.Refresh(TestSamples.RouletteSnapshot(4), TableChange.Corrected, Color.blue);
            Assert.That(widget.column.currentState.text, Is.EqualTo("Awaiting first result"));
            Assert.That(widget.rows.All(row => !row.ball.gameObject.activeSelf));
        }

        [Test]
        public void SicBoUsesTheModelsOutcomeAndKeepsUnreadableCurrentSeparate()
        {
            var message = TestSamples.Roulette(6, "4-5-6", "3-3-3", "1-2-6");
            message.GameType = "SICBO";
            var snapshot = SnapshotParser.Parse(message);
            var widget = (ColumnsSicBoWidget)Object.Instantiate(layout.FindWidgetPrefab(snapshot), root.transform);
            widget.Bind(snapshot, Color.cyan);
            Assert.That(widget.currentDice.total.text, Is.EqualTo("15"));
            Assert.That(widget.currentDice.outcome.sprite.name, Is.EqualTo("Big"));
            Assert.That(widget.currentDice.third.sprite.name, Is.EqualTo("Die 6"));
            Assert.That(widget.rows[0].outcome.sprite.name, Is.EqualTo("Triple"));
            Assert.That(widget.rows[1].outcome.sprite.name, Is.EqualTo("Small"));
            message.Results[0].Result = "777";
            widget.Refresh(SnapshotParser.Parse(message), TableChange.NewResult, Color.cyan);
            Assert.That(widget.currentDice.root.activeSelf, Is.False);
            Assert.That(widget.column.currentState.text, Is.EqualTo("Result unavailable"));
            Assert.That(widget.rows[0].outcome.sprite.name, Is.EqualTo("Triple"));
        }

        [Test]
        public void StatusPreservesTwoLinesAssignmentCodeAndLiteralPath()
        {
            var view = (DebugStatusView)layout.statusView;
            const string headline = "Waiting for assignment\nCode 2749, C:\\tcs";
            view.Show(new MtdStatus { Headline = headline });
            Assert.That(view.headline.text, Is.EqualTo(headline));
            Assert.That(view.headline.richText || view.headline.parseCtrlCharacters, Is.False);
            Assert.That(view.headlinePanel.activeSelf);
            view.Show(new MtdStatus());
            Assert.That(view.headlinePanel.activeSelf, Is.False);
        }

        [TestCase(4)]
        [TestCase(6)]
        public void SicBoChangesEachRowBeyondItsOwnRightEdgeAndReturnsOnTheSameLine(int slotCount)
        {
            var board = MtdUiLoader.Load("Columns4K", MtdOrientation.Landscape, slotCount, root.transform).Layout;
            TableSnapshot Roll(params string[] results)
            {
                var message = TestSamples.Roulette(6, results);
                message.GameType = "SICBO";
                return SnapshotParser.Parse(message);
            }
            var old = Roll("4-5-6", "1-2-6", "1-2-3");
            var next = Roll("5-5-6", "4-5-6", "1-2-6", "1-2-3");
            var widget = (ColumnsSicBoWidget)Object.Instantiate(board.FindWidgetPrefab(old), board.slots[1]);
            var fill = (RectTransform)widget.transform;
            fill.anchorMin = fill.offsetMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMax = Vector2.zero;
            widget.Bind(old, Color.cyan);
            var view = widget.column;
            var clip = view.history.GetComponentInParent<RectMask2D>();
            Assert.That(clip.transform.IsChildOf(board.slots[1]));
            var row = view.historyRows[0];
            Vector2 rest = row.anchoredPosition;
            widget.Refresh(next, TableChange.NewResult, Color.cyan);
            view.Sample(.22f);
            Assert.That(view.currentParts[0].localScale.x, Is.LessThan(view.currentParts[1].localScale.x));
            Assert.That(view.currentParts[2].localScale, Is.EqualTo(Vector3.one));
            view.Sample(.86f);
            Assert.That(widget.rows[0].total.text, Is.EqualTo("9"));
            Assert.That(row.anchoredPosition.x, Is.GreaterThan(rest.x));
            view.Sample(1.05f);
            var corners = new Vector3[4];
            row.GetWorldCorners(corners);
            Assert.That(clip.rectTransform.InverseTransformPoint(corners[0]).x,
                Is.GreaterThanOrEqualTo(clip.rectTransform.rect.xMax));
            Assert.That(widget.rows[0].total.text, Is.EqualTo("15"));
            Assert.That(widget.rows[1].total.text, Is.EqualTo("6"));
            view.Sample(1.43f);
            Assert.That(row.anchoredPosition.x, Is.InRange(rest.x+.01f, rest.x+clip.rectTransform.rect.width-.01f));
            Assert.That(row.anchoredPosition.y, Is.EqualTo(rest.y));
            view.Sample(3.8f);
            Assert.That(row.anchoredPosition, Is.EqualTo(rest));
            Assert.That(widget.currentDice.total.text, Is.EqualTo("16"));
            Assert.That(widget.rows[1].total.text, Is.EqualTo("9"));
            Assert.That(view.currentParts.All(p => p.localScale == Vector3.one));

            widget.Refresh(old, TableChange.NewResult, Color.cyan);
            view.Sample(1.05f);
            widget.Refresh(Roll("invalid", "2-2-2"), TableChange.Corrected, Color.cyan);
            Assert.That(row.anchoredPosition, Is.EqualTo(rest));
            Assert.That(widget.currentDice.root.activeSelf, Is.False);
            Assert.That(view.currentState.text, Is.EqualTo("Result unavailable"));
            Assert.That(widget.rows[0].outcome.sprite.name, Is.EqualTo("Triple"));
            Assert.That(widget.rows[1].root.activeSelf, Is.False);
        }

        [Test]
        public void ConsecutiveRouletteBallsHaveClearanceAtRestAndThroughoutTheRipple()
        {
            string[] pockets = { "2","4","6","8","10","11","13","15","17","20","22","24" };
            var table = TestSamples.RouletteSnapshot(1, pockets);
            var widget = (ColumnsRouletteWidget)Object.Instantiate(layout.FindWidgetPrefab(table), root.transform);
            widget.Bind(table, Color.blue);
            var view = widget.column;
            var bands = view.historyRows;
            float diameter = widget.rows[0].ball.rectTransform.rect.height;
            float Gap(RectTransform top, RectTransform bottom) => top.anchoredPosition.y-bottom.anchoredPosition.y-diameter;
            Assert.That(Gap(bands[0], bands[1]), Is.EqualTo(13).Within(.01f));
            widget.Refresh(TestSamples.RouletteSnapshot(1, new[] { "26" }.Concat(pockets).ToArray()), TableChange.NewResult, Color.blue);
            for (int frame = 0; frame <= 180; frame++)
            {
                view.Sample(frame / 120f);
                for (int i = 1; i < bands.Length; i++)
                    Assert.That(Gap(bands[i-1], bands[i]), Is.GreaterThan(2.5f));
                if (view.rouletteTail.gameObject.activeSelf)
                    Assert.That(Gap(bands[bands.Length-1], view.rouletteTail), Is.GreaterThan(2.5f));
            }
        }

        [Test]
        public void RouletteCoalescesTwoWinsAndClearsAnActiveBurstOnReassignment()
        {
            var old = TestSamples.RouletteSnapshot(5, "4", "00", "19");
            var widget = (ColumnsRouletteWidget)Object.Instantiate(layout.FindWidgetPrefab(old), root.transform);
            widget.Bind(old, Color.red);
            var view = widget.column;
            var rest = view.historyRows.Select(r => r.anchoredPosition).ToArray();
            widget.Refresh(TestSamples.RouletteSnapshot(5, "14", "4", "00", "19"), TableChange.NewResult, Color.red);
            var latest = TestSamples.Roulette(5, "000", "14", "4", "00", "19");
            latest.Results[0].Multiplier = 80;
            latest.Results[1].Multiplier = 20;
            widget.Refresh(SnapshotParser.Parse(latest), TableChange.NewResult, Color.red);
            view.Sample(.65f);
            Assert.That(widget.rows[0].ball.sprite.name, Is.EqualTo("Ball 14"));
            Assert.That(widget.rows[0].chip.value.text, Is.EqualTo("20"));
            Assert.That(view.historyRows[1].anchoredPosition.y-rest[1].y,
                Is.GreaterThan(view.historyRows[0].anchoredPosition.y-rest[0].y));
            view.Sample(1.15f);
            Assert.That(widget.currentPocket.sprite.name, Is.EqualTo("Pocket 000"));
            Assert.That(widget.currentChip.value.text, Is.EqualTo("80"));
            Assert.That(view.chipBurst.enabled);
            widget.Bind(TestSamples.RouletteSnapshot(2, "0"), Color.blue);
            Assert.That(view.chipBurst.enabled, Is.False);
            Assert.That(view.rouletteTail.gameObject.activeSelf, Is.False);
            Assert.That(widget.currentChip.rim.gameObject.activeSelf, Is.False);
            Assert.That(view.currentParts.All(p => p.localScale == Vector3.one));
            CollectionAssert.AreEqual(rest, view.historyRows.Select(r => r.anchoredPosition));
        }

        [TestCase(4, "32")]
        [TestCase(6, "0000")]
        [TestCase(6, "4")]
        public void OutgoingPocketAndChipMeetTheirHistoryArtExactly(int count, string pocket)
        {
            var board = MtdUiLoader.Load("Columns4K", MtdOrientation.Landscape, count, root.transform).Layout;
            var old = TestSamples.Roulette(1, pocket, "0", "17");
            old.Results[0].Multiplier = 100;
            old.Results[0].MultiplierColour = "#CAAA36";
            var widget = Object.Instantiate((ColumnsRouletteWidget)board.FindWidgetPrefab(SnapshotParser.Parse(old)), board.slots[1]);
            var rect = (RectTransform)widget.transform;
            rect.anchorMin = rect.offsetMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMax = Vector2.zero;
            widget.Bind(SnapshotParser.Parse(old), Color.blue);
            var previousChipPosition = widget.currentChip.rim.transform.position;
            old.Results.Insert(0, new MtdResultMessage {Result="14", Multiplier=80});
            widget.Refresh(SnapshotParser.Parse(old), TableChange.NewResult, Color.blue);
            Assert.That(widget.flying.ball.sprite.name, Is.EqualTo("Ball " + pocket));
            Assert.That(widget.flying.chip.value.text, Is.EqualTo("100"));
            Assert.That(Vector3.Distance(widget.flying.chip.rim.transform.position, previousChipPosition), Is.LessThan(.03f));
            Assert.That(widget.currentChip.rim.gameObject.activeSelf, Is.False);
            widget.column.Sample(.27f);
            Assert.That(widget.flying.ball.color.a, Is.EqualTo(1));
            widget.column.Sample(.60f);
            var row = widget.rows[0];
            Assert.That(row.ball.gameObject.activeSelf, Is.False);
            Assert.That(row.chip.rim.color, Is.EqualTo(widget.flying.chip.rim.color));
            Assert.That(Vector3.Distance(row.ball.transform.position, widget.flying.ball.transform.position), Is.LessThan(.03f));
            Assert.That(Vector3.Distance(row.chip.rim.transform.position, widget.flying.chip.rim.transform.position), Is.LessThan(.03f));
            widget.column.Sample(.94f);
            Assert.That(row.ball.gameObject.activeSelf);
            Assert.That(widget.flying.ball.gameObject.activeSelf, Is.False);
            Assert.That(widget.currentChip.value.text, Is.EqualTo("80"));
        }

        [Test]
        public void TransferTracksOlderWinnerPastRepeatedAndUnreadableSpinsAndCancelsOnCorrection()
        {
            var table = TestSamples.RouletteSnapshot(1, "4","17");
            var widget = (ColumnsRouletteWidget)Object.Instantiate(layout.FindWidgetPrefab(table), root.transform);
            widget.Bind(table, Color.blue);
            widget.Refresh(TestSamples.RouletteSnapshot(1,"4","4","17"), TableChange.NewResult, Color.blue);
            widget.Refresh(TestSamples.RouletteSnapshot(1,"3","bad","4","4","17"), TableChange.NewResult, Color.blue);
            widget.column.Sample(.55f);
            Assert.That(widget.rows[0].ball.gameObject.activeSelf);
            Assert.That(widget.rows[1].ball.gameObject.activeSelf, Is.False);
            Assert.That(widget.flying.ball.sprite.name, Is.EqualTo("Ball 4"));
            widget.Refresh(TestSamples.RouletteSnapshot(1,"bad","35"), TableChange.Corrected, Color.blue);
            Assert.That(widget.flying.ball.gameObject.activeSelf, Is.False);
            Assert.That(widget.rows[1].ball.gameObject.activeSelf, Is.False);
            widget.Refresh(TestSamples.RouletteSnapshot(1,"14","bad","35"), TableChange.NewResult, Color.blue);
            Assert.That(widget.flying.ball.gameObject.activeSelf, Is.False);
        }

        [TestCase(4)]
        [TestCase(6)]
        public void RelayAlignsToTheSeamAndBothEngravingsSitBehindTheWinner(int count)
        {
            var board = MtdUiLoader.Load("Columns4K", MtdOrientation.Landscape, count, root.transform).Layout;
            var relay = board.GetComponentInChildren<DiamondRelay>();
            Assert.That(board.pagePresented.GetPersistentTarget(0), Is.SameAs(relay));
            Assert.That(relay.slots, Is.EqualTo(count));
            Assert.That(relay.seam.rectTransform.anchoredPosition.y, Is.EqualTo(-365.5f));
            relay.Play();
            Assert.That(relay.seam.enabled);
            relay.Sample(2.25f);
            Assert.That(relay.seam.enabled, Is.False);
            foreach (var item in board.widgets)
            {
                var view = item.prefab.GetComponent<ColumnView>();
                Assert.That(view.backplate.texture.name, Is.EqualTo(item.family == GameFamily.Roulette ? "Wheel Engraving" : "Dice Engraving"));
                Assert.That(view.backplate.rectTransform.GetSiblingIndex(), Is.LessThan(view.current.GetSiblingIndex()));
                Assert.That(view.backplate.rectTransform.rect.width, Is.EqualTo(352));
            }
        }
    }
}
