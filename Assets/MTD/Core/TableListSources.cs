using System;
using System.Collections.Generic;

namespace MTD
{
    // An ordered roster from T-Connect, the operator or the simulator. Names never enter this index.
    public sealed class TableIdListSource
    {
        readonly Dictionary<Guid, int> positions = new Dictionary<Guid, int>();
        readonly string name;
        public TableIdListSource(string name = "T-Connect table list") { this.name = name; }

        public bool HasList { get; private set; }
        public int Count => positions.Count;
        public int Version { get; private set; }
        public string Description => HasList ? $"{name} ({Count} tables)" : $"waiting for the {name}";

        public void SetTables(IEnumerable<Guid> ids)
        {
            positions.Clear();
            if (ids != null)
                foreach (Guid id in ids)
                    if (id != Guid.Empty) positions.TryAdd(id, positions.Count);
            HasList = true;
            Version++;
        }

        public bool Includes(TableSnapshot table) => positions.ContainsKey(table.TableId);
        public int Position(Guid id) => positions[id];
    }
}
