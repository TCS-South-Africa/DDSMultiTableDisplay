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

        protected override void OnColour(Color colour) => column.Tint(colour);
        public override IEnumerator PlayEnter() => column.Fade(true);
        public override IEnumerator PlayExit() => column.Fade(false);

        protected override void OnBind(RouletteSnapshot table)
        {
            tail.ball.sprite = null;
            tail.chip.Draw(null);
            column.Header(table, table.Variant);
            column.Bind(() => DrawCurrent(table), i => DrawRow(table, i));
        }

        protected override void OnRefresh(RouletteSnapshot table, TableChange change)
        {
            column.Header(table, table.Variant);
            column.Refresh(change, table.Current.HasValue && !table.CurrentResultUnreadable,
                () => DrawCurrent(table), i => DrawRow(table, i), CopyTail);
        }

        void DrawCurrent(RouletteSnapshot table)
        {
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
            float lane = result.Pocket.IsZero ? .5f : result.Pocket.Colour == RouletteColour.Red ? .18f : .82f;
            row.ball.rectTransform.anchorMin = row.ball.rectTransform.anchorMax = new Vector2(lane, 1);
            row.chip.Draw(result);
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
