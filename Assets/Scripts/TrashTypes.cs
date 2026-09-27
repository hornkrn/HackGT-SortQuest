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
        PowerBank,
        // Append only: these values are serialized in prefabs and scenes.
        FoodTin,
        TunaCan,
        MetalLid,
        FoilTray,
        SteelBottle,
        YogurtCup,
        DetergentBottle,
        ShampooBottle,
        PlasticTub,
        PlasticCap,
        CerealCarton,
        EggCarton,
        PaperTube,
        FoldedNewspaper,
        Battery9V,
        BatteryCoin,
        Smartphone,
        CircuitBoard
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
                case ItemType.AluminumCan:
                case ItemType.FoodTin:
                case ItemType.TunaCan:
                case ItemType.MetalLid:
                case ItemType.FoilTray:
                case ItemType.SteelBottle: return BinType.Metal;
                case ItemType.PlasticBottle:
                case ItemType.YogurtCup:
                case ItemType.DetergentBottle:
                case ItemType.ShampooBottle:
                case ItemType.PlasticTub:
                case ItemType.PlasticCap: return BinType.Plastic;
                case ItemType.CardboardBox: return BinType.Paper;
                case ItemType.CrumpledPaper:
                case ItemType.CerealCarton:
                case ItemType.EggCarton:
                case ItemType.PaperTube:
                case ItemType.FoldedNewspaper: return BinType.Paper;
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
                case ItemType.FoodTin: return "food_tin";
                case ItemType.TunaCan: return "tuna_can";
                case ItemType.MetalLid: return "metal_lid";
                case ItemType.FoilTray: return "foil_tray";
                case ItemType.SteelBottle: return "steel_bottle";
                case ItemType.YogurtCup: return "yogurt_cup";
                case ItemType.DetergentBottle: return "detergent_bottle";
                case ItemType.ShampooBottle: return "shampoo_bottle";
                case ItemType.PlasticTub: return "plastic_tub";
                case ItemType.PlasticCap: return "plastic_cap";
                case ItemType.CerealCarton: return "cereal_carton";
                case ItemType.EggCarton: return "egg_carton";
                case ItemType.PaperTube: return "paper_tube";
                case ItemType.FoldedNewspaper: return "folded_newspaper";
                case ItemType.Battery9V: return "battery_9v";
                case ItemType.BatteryCoin: return "battery_coin";
                case ItemType.Smartphone: return "smartphone";
                case ItemType.CircuitBoard: return "circuit_board";
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
                case ItemType.FoodTin: return "Food tin";
                case ItemType.TunaCan: return "Tuna can";
                case ItemType.MetalLid: return "Metal lid";
                case ItemType.FoilTray: return "Foil tray";
                case ItemType.SteelBottle: return "Steel bottle";
                case ItemType.YogurtCup: return "Yogurt cup";
                case ItemType.DetergentBottle: return "Detergent bottle";
                case ItemType.ShampooBottle: return "Shampoo bottle";
                case ItemType.PlasticTub: return "Plastic tub";
                case ItemType.PlasticCap: return "Plastic cap";
                case ItemType.CerealCarton: return "Cereal carton";
                case ItemType.EggCarton: return "Egg carton";
                case ItemType.PaperTube: return "Paper tube";
                case ItemType.FoldedNewspaper: return "Folded newspaper";
                case ItemType.Battery9V: return "9V battery";
                case ItemType.BatteryCoin: return "Coin cell";
                case ItemType.Smartphone: return "Smartphone";
                case ItemType.CircuitBoard: return "Circuit board";
                default: return item.ToString();
            }
        }

        /// <summary>Sign text for a bin: its name, then the items that belong in it.</summary>
        public static string BinSignText(BinType bin)
        {
            IEnumerable<string> items = ((ItemType[])Enum.GetValues(typeof(ItemType)))
                .Where(item => CorrectBin(item) == bin)
                .Select(DisplayName);
            return $"{bin.ToString().ToUpperInvariant()}\n<size=45%>{string.Join(" • ", items.Take(3))}\n{string.Join(" • ", items.Skip(3))}</size>";
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
