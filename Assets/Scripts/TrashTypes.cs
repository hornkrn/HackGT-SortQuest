using UnityEngine;

namespace SortQuest
{
    public enum BinType
    {
        Metal,
        Plastic,
        Paper,
        Hazardous
    }

    public enum ItemType
    {
        AluminumCan,
        PlasticBottle,
        CardboardBox,
        CrumpledPaper,
        BatteryAA,
        PowerBank
    }

    /// <summary>
    /// Which bin each item belongs in, and the color of each bin.
    /// </summary>
    public static class TrashTypes
    {
        public static BinType CorrectBin(ItemType item)
        {
            switch (item)
            {
                case ItemType.AluminumCan: return BinType.Metal;
                case ItemType.PlasticBottle: return BinType.Plastic;
                case ItemType.CardboardBox: return BinType.Paper;
                case ItemType.CrumpledPaper: return BinType.Paper;
                case ItemType.BatteryAA: return BinType.Hazardous;
                case ItemType.PowerBank: return BinType.Hazardous;
                default: return BinType.Hazardous;
            }
        }

        public static Color BinColor(BinType bin)
        {
            switch (bin)
            {
                case BinType.Metal: return new Color(0.15f, 0.4f, 0.95f);
                case BinType.Plastic: return new Color(1f, 0.85f, 0.1f);
                case BinType.Paper: return new Color(0.2f, 0.75f, 0.3f);
                case BinType.Hazardous: return new Color(0.9f, 0.15f, 0.15f);
                default: return Color.gray;
            }
        }
    }
}
