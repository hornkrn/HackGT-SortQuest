using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>The snake_case id used in grasp records, e.g. "battery_aa".</summary>
        public static string ItemId(ItemType item)
        {
            switch (item)
            {
                case ItemType.AluminumCan: return "aluminum_can";
                case ItemType.PlasticBottle: return "plastic_bottle";
                case ItemType.CardboardBox: return "cardboard_box";
                case ItemType.CrumpledPaper: return "crumpled_paper";
                case ItemType.BatteryAA: return "battery_aa";
                case ItemType.PowerBank: return "power_bank";
                default: return item.ToString().ToLowerInvariant();
            }
        }

        public static string DisplayName(ItemType item)
        {
            switch (item)
            {
                case ItemType.AluminumCan: return "Aluminum can";
                case ItemType.PlasticBottle: return "Plastic bottle";
                case ItemType.CardboardBox: return "Cardboard box";
                case ItemType.CrumpledPaper: return "Crumpled paper";
                case ItemType.BatteryAA: return "AA battery";
                case ItemType.PowerBank: return "Power bank";
                default: return item.ToString();
            }
        }

        /// <summary>Sign text for a bin: its name, then the items that belong in it.</summary>
        public static string BinSignText(BinType bin)
        {
            IEnumerable<string> items = ((ItemType[])Enum.GetValues(typeof(ItemType)))
                .Where(item => CorrectBin(item) == bin)
                .Select(DisplayName);
            return $"{bin.ToString().ToUpperInvariant()}\n<size=55%>{string.Join(", ", items)}</size>";
        }

        /// <summary>The id used in grasp records, e.g. "hazardous".</summary>
        public static string BinId(BinType bin)
        {
            return bin.ToString().ToLowerInvariant();
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
