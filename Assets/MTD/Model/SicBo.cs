namespace MTD
{
    public enum SicBoOutcome
    {
        Small,
        Big,
        Triple,
    }

    // Three dice, lowest first, as DDS sends them: "1-4-4".
    public readonly struct SicBoResult
    {
        public readonly int Die1;
        public readonly int Die2;
        public readonly int Die3;

        public SicBoResult(int die1, int die2, int die3)
        {
            Die1 = die1;
            Die2 = die2;
            Die3 = die3;
        }

        public int Total => Die1 + Die2 + Die3;

        public bool IsTriple => Die1 == Die2 && Die2 == Die3;

        // DDS's rule: a total above 10 is big, 10 or below is small, and a triple is neither.
        public SicBoOutcome Outcome => IsTriple ? SicBoOutcome.Triple : (Total > 10 ? SicBoOutcome.Big : SicBoOutcome.Small);

        public static bool TryParse(string text, out SicBoResult result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split('-');
            if (parts.Length != 3) return false;

            int[] dice = new int[3];
            for (int i = 0; i < 3; i++)
            {
                string part = parts[i].Trim();
                if (part.Length != 1 || part[0] < '1' || part[0] > '6') return false;
                dice[i] = part[0] - '0';
            }

            System.Array.Sort(dice); // DDS sends them sorted; sorting again keeps one form whatever arrives.
            result = new SicBoResult(dice[0], dice[1], dice[2]);
            return true;
        }

        public override string ToString()
        {
            return $"{Die1}-{Die2}-{Die3}";
        }
    }
}
