using System.Collections;
using UnityEngine;

namespace MTD
{
    // The base of every table widget. One widget prefab draws one table in one slot. An MTD UI supplies its own
    // widget prefabs with scripts derived from this (usually through RouletteTableWidget or SicBoTableWidget); the
    // display controller is the only caller.
    public abstract class TableWidget : MonoBehaviour
    {
        // The table this widget is showing. Null before the first Bind.
        public TableSnapshot Table { get; private set; }

        // A different table now owns this widget. Draw it from scratch, with no animation.
        public void Bind(TableSnapshot table, Color colour)
        {
            Table = table;
            OnColour(colour);
            OnBind(table);
        }

        // The same table has new data.
        public void Refresh(TableSnapshot table, TableChange change, Color colour)
        {
            TableSnapshot previous = Table;
            Table = table;
            if (previous == null || !Equals(previous.Colour, table.Colour)) OnColour(colour);
            OnRefresh(table, change);
        }

        protected abstract void OnBind(TableSnapshot table);

        // Redraws everything unless overridden. Override to animate a new result or to update only what changed.
        protected virtual void OnRefresh(TableSnapshot table, TableChange change)
        {
            OnBind(table);
        }

        // The table's colour, or the layout's default when the table sent none.
        protected virtual void OnColour(Color colour)
        {
        }

        // Played after the widget is bound and shown, and before it is hidden. Instant unless overridden.
        public virtual IEnumerator PlayEnter()
        {
            yield break;
        }

        public virtual IEnumerator PlayExit()
        {
            yield break;
        }
    }

    public abstract class RouletteTableWidget : TableWidget
    {
        protected sealed override void OnBind(TableSnapshot table)
        {
            OnBind((RouletteSnapshot)table);
        }

        protected sealed override void OnRefresh(TableSnapshot table, TableChange change)
        {
            OnRefresh((RouletteSnapshot)table, change);
        }

        protected abstract void OnBind(RouletteSnapshot table);

        protected virtual void OnRefresh(RouletteSnapshot table, TableChange change)
        {
            OnBind(table);
        }
    }

    public abstract class SicBoTableWidget : TableWidget
    {
        protected sealed override void OnBind(TableSnapshot table)
        {
            OnBind((SicBoSnapshot)table);
        }

        protected sealed override void OnRefresh(TableSnapshot table, TableChange change)
        {
            OnRefresh((SicBoSnapshot)table, change);
        }

        protected abstract void OnBind(SicBoSnapshot table);

        protected virtual void OnRefresh(SicBoSnapshot table, TableChange change)
        {
            OnBind(table);
        }
    }
}
