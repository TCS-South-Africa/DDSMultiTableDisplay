using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    public sealed class ColumnsRouletteWidget : RouletteTableWidget
    {
        [Serializable]
        public sealed class Chip
        {
            public Image rim;
            public TMP_Text value;

            public void Draw(RouletteResult? result)
            {
                bool visible = result.HasValue && result.Value.HasMultiplier;
                rim.gameObject.SetActive(visible);
                if (!visible)
                {
                    value.text = "";
                    rim.color = Color.white;
                    return;
                }
                rim.color = result.Value.MultiplierColour ?? new Color32(214, 167, 65, 255);
                value.text = result.Value.Multiplier.ToString(CultureInfo.InvariantCulture);
            }
        }

        [Serializable]
        public sealed class HistoryRow
        {
            public Image ball;
            public Chip chip;
        }

        public ColumnView column;
        public Image currentPocket;
        public Chip currentChip;
        public Sprite[] pockets, balls; // 0..36 followed by 00, 000 and 0000.
        public HistoryRow[] rows;
        public HistoryRow tail;
        public HistoryRow flying;

        RouletteSnapshot displayed;
        int landing = -1;
        bool rowHidden;
        Vector3 source, chipSource, entry;

        protected override void OnColour(Color colour) => column.Tint(colour);
        public override IEnumerator PlayEnter() => column.Fade(true);
        public override IEnumerator PlayExit() => column.Fade(false);

        protected override void OnBind(RouletteSnapshot table)
        {
            column.Roulette = this;
            tail.ball.sprite = null;
            tail.chip.Draw(null);
            column.Header(table, UseUniqueName, table.Variant);
            column.Bind(() => DrawCurrent(table), i => DrawRow(table, i));
        }

        protected override void OnRefresh(RouletteSnapshot table, TableChange change)
        {
            column.Header(table, UseUniqueName, table.Variant);
            column.Refresh(change, table.Current.HasValue && !table.CurrentResultUnreadable,
                () => DrawCurrent(table), i => DrawRow(table, i), CopyTail);
            if (change == TableChange.NewResult && table.Current.HasValue && !table.CurrentResultUnreadable)
                PrepareTransfer(table);
        }

        void DrawCurrent(RouletteSnapshot table)
        {
            displayed = table;
            RouletteResult? current = table.CurrentResultUnreadable ? null : table.Current;
            column.CurrentState(current.HasValue, table.CurrentResultUnreadable);
            currentPocket.enabled = current.HasValue;
            currentPocket.sprite = current.HasValue ? pockets[Index(current.Value.Pocket)] : null;
            currentChip.Draw(current);
        }

        void DrawRow(RouletteSnapshot table, int i)
        {
            HistoryRow row = rows[i];
            bool visible = i < table.History.Count;
            row.ball.gameObject.SetActive(visible);
            if (!visible)
            {
                row.ball.sprite = null;
                row.chip.Draw(null);
                return;
            }
            RouletteResult result = table.History[i];
            row.ball.sprite = balls[Index(result.Pocket)];
            row.ball.rectTransform.anchorMin = row.ball.rectTransform.anchorMax = new Vector2(Lane(result.Pocket), 1);
            row.chip.Draw(result);
        }

        static float Lane(RoulettePocket pocket) =>
            pocket.IsZero ? .5f : pocket.Colour == RouletteColour.Red ? .18f : .82f;

        void PrepareTransfer(RouletteSnapshot next)
        {
            EndTransfer();
            if (displayed?.Current == null || displayed.CurrentResultUnreadable || displayed.TableId != next.TableId) return;

            // Match the whole overlapping DDS sequence, including coloured chips and unreadable
            // rows. A repeated pocket alone cannot identify the spin which just left the hero.
            int added = TableRegistry.CountAddedInFront(displayed.ResultKeys, next.ResultKeys, false);
            if (added == 0) return;
            int target = 0;
            for (int i = 1; i < added; i++)
                if (!next.ResultKeys[i].StartsWith("?", StringComparison.Ordinal)) target++;
            if (target >= rows.Length || target >= next.History.Count) return;

            var old = displayed.Current.Value;
            landing = target;
            flying.ball.sprite = balls[Index(old.Pocket)];
            flying.chip.Draw(old);
            source = transform.InverseTransformPoint(currentPocket.rectTransform.position);
            chipSource = transform.InverseTransformPoint(currentChip.rim.rectTransform.position);
            // Until the scroll starts, the destination band still displays the preceding snapshot.
            var band = column.historyRows[target];
            var centre = new Vector2(Mathf.Lerp(band.rect.xMin, band.rect.xMax, Lane(old.Pocket)), band.rect.center.y);
            entry = transform.InverseTransformPoint(band.TransformPoint(centre + Vector2.up * ColumnView.RoulettePitch));
            flying.ball.gameObject.SetActive(true);
            currentChip.rim.gameObject.SetActive(false);
            SampleTransfer(0, false);
        }

        internal void SampleTransfer(float t, bool rowsCommitted)
        {
            if (landing < 0) return;
            var destination = rows[landing].ball.rectTransform;
            if (rowsCommitted && !rowHidden)
            {
                destination.gameObject.SetActive(false);
                rowHidden = true;
            }
            if (t >= .94f)
            {
                EndTransfer();
                return;
            }

            float phase = Mathf.Clamp01(t / .44f);
            float blend = Mathf.SmoothStep(0, 1, phase);
            Vector3 position = rowsCommitted ? transform.InverseTransformPoint(destination.position) : entry;
            if (!rowsCommitted)
            {
                position.x = Mathf.Lerp(source.x, entry.x, Mathf.Sin(phase * Mathf.PI * .5f));
                position.y = Mathf.Lerp(source.y, entry.y, phase * phase) + 40 * Mathf.Sin(phase * Mathf.PI);
            }
            var ball = flying.ball.rectTransform;
            ball.position = transform.TransformPoint(position);
            float scale = Mathf.Lerp(.45f, 1, blend);
            ball.localScale = Vector3.one * scale;
            flying.ball.color = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, phase * 3.5f));

            // The chip departs at its actual hero size, joins the ball on the arc and arrives
            // at the precise inset of the history chip. The destination inherits both at once.
            var chip = flying.chip.rim.rectTransform;
            var restChip = rows[landing].chip.rim.rectTransform;
            chip.localPosition = restChip.localPosition;
            Vector3 attached = transform.InverseTransformPoint(chip.position);
            chip.position = transform.TransformPoint(Vector3.Lerp(chipSource, attached, blend));
            float initialScale = currentChip.rim.rectTransform.rect.width / restChip.rect.width;
            chip.localScale = Vector3.one * (Mathf.Lerp(initialScale, 1, blend) / scale);
        }

        internal void EndTransfer()
        {
            if (rowHidden) rows[landing].ball.gameObject.SetActive(true);
            rowHidden = false;
            landing = -1;
            flying.ball.gameObject.SetActive(false);
            flying.ball.sprite = null;
            flying.chip.Draw(null);
        }

        void CopyTail()
        {
            var last = rows[rows.Length - 1];
            tail.ball.sprite = last.ball.sprite;
            tail.ball.rectTransform.anchorMin = tail.ball.rectTransform.anchorMax = last.ball.rectTransform.anchorMin;
            tail.chip.rim.color = last.chip.rim.color;
            tail.chip.value.text = last.chip.value.text;
            tail.chip.rim.gameObject.SetActive(last.chip.rim.gameObject.activeSelf);
            column.rouletteTail.gameObject.SetActive(last.ball.gameObject.activeSelf);
        }

        static int Index(RoulettePocket pocket)
        {
            if (pocket.IsZero && pocket.Label.Length > 1) return 35 + pocket.Label.Length;
            return int.Parse(pocket.Label, CultureInfo.InvariantCulture);
        }
    }
}
