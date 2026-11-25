using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    public sealed class ColumnsSicBoWidget : SicBoTableWidget
    {
        [Serializable]
        public sealed class DiceRow
        {
            public GameObject root;
            public Image first, second, third, outcome;
            public TMP_Text total;
        }

        public ColumnView column;
        public Sprite[] faces;
        public Sprite small, big, triple;
        public DiceRow currentDice;
        public DiceRow[] rows;

        protected override void OnColour(Color colour) => column.Tint(colour);
        public override IEnumerator PlayEnter() => column.Fade(true);
        public override IEnumerator PlayExit() => column.Fade(false);

        protected override void OnBind(SicBoSnapshot table)
        {
            column.Header(table);
            column.Bind(() => DrawCurrent(table), i => DrawHistory(table, i));
        }

        protected override void OnRefresh(SicBoSnapshot table, TableChange change)
        {
            column.Header(table);
            column.Refresh(change, table.Current.HasValue && !table.CurrentResultUnreadable,
                () => DrawCurrent(table), i => DrawHistory(table, i));
        }

        void DrawCurrent(SicBoSnapshot table)
        {
            bool readable = table.Current.HasValue && !table.CurrentResultUnreadable;
            column.CurrentState(readable, table.CurrentResultUnreadable);
            DrawRow(currentDice, readable ? table.Current : null);
        }

        void DrawHistory(SicBoSnapshot table, int i)
            => DrawRow(rows[i], i < table.History.Count ? table.History[i] : null);

        void DrawRow(DiceRow row, SicBoResult? value)
        {
            row.root.SetActive(value.HasValue);
            if (!value.HasValue)
            {
                row.first.sprite = row.second.sprite = row.third.sprite = row.outcome.sprite = null;
                row.total.text = "";
                return;
            }
            SicBoResult result = value.Value;
            row.first.sprite = faces[result.Die1 - 1];
            row.second.sprite = faces[result.Die2 - 1];
            row.third.sprite = faces[result.Die3 - 1];
            row.total.text = result.Total.ToString(CultureInfo.InvariantCulture);
            row.outcome.sprite = result.Outcome == SicBoOutcome.Triple ? triple : result.Outcome == SicBoOutcome.Big ? big : small;
        }
    }
}
