using System;
using System.Collections.Generic;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Where the trash guide board and the bin labels sit. Shared by the scene art (built in the Editor by
    /// SortQuest > Polish Scene Visuals) and the runtime models and text (TrashGuide, InteractionFeedback),
    /// so the two always line up.
    ///
    /// Board space: x runs to the viewer's right, y is height above the floor, and +z points into the board
    /// (away from the viewer). Text reads correctly with an identity rotation in this space.
    /// </summary>
    public static class TrashGuideLayout
    {
        /// <summary>The floor point under the board's center: to the player's left, outside the play area.</summary>
        public static readonly Vector3 BoardPosition = new Vector3(-2.35f, 0f, 0.15f);

        /// <summary>Faces the player standing at the origin.</summary>
        public static readonly Quaternion BoardRotation = Quaternion.Euler(0f, -90f, 0f);

        public const float BoardWidth = 1.5f;
        public const float BoardBottom = 0.78f;
        public const float BoardTop = 1.98f;
        public const float ColumnWidth = 0.36f;
        public const float TitleY = 1.88f;
        public const float HeaderY = 1.71f;
        public const float HeaderHeight = 0.1f;

        /// <summary>Items in a column are spread between these heights.</summary>
        public const float SlotTop = 1.62f;
        public const float SlotBottom = 0.84f;

        /// <summary>The board's front surface is at z = -FaceDepth.</summary>
        public const float FaceDepth = 0.01f;
        public const float ShelfDepth = 0.14f;

        /// <summary>One column per bin, left to right.</summary>
        public static readonly BinType[] Columns = { BinType.Metal, BinType.Plastic, BinType.Paper, BinType.Hazardous };

        public static float ColumnX(int column) => (column - (Columns.Length - 1) * 0.5f) * ColumnWidth;

        /// <summary>The items that belong in a bin, in ItemType order.</summary>
        public static List<ItemType> ItemsFor(BinType bin)
        {
            var items = new List<ItemType>();
            foreach (ItemType item in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                if (TrashTypes.CorrectBin(item) == bin) items.Add(item);
            }
            return items;
        }

        /// <summary>Center of item slot <paramref name="index"/> of <paramref name="count"/> in a column.</summary>
        public static Vector3 Slot(int column, int index, int count)
        {
            float height = (SlotTop - SlotBottom) / count;
            return new Vector3(ColumnX(column), SlotTop - height * (index + 0.5f), 0f);
        }

        /// <summary>Height of the shelf top that an item's model stands on.</summary>
        public static float ShelfY(Vector3 slot) => slot.y - 0.06f;

        /// <summary>Center height of an item's name under its shelf.</summary>
        public static float LabelY(Vector3 slot) => ShelfY(slot) - 0.055f;

        public static Vector3 ToWorld(Vector3 boardPoint) => BoardPosition + BoardRotation * boardPoint;

        /// <summary>Bright accent color for each bin: the bin collars, the guide's column headers, and name text.</summary>
        public static Color BinAccent(BinType bin)
        {
            switch (bin)
            {
                case BinType.Metal: return new Color32(0x57, 0x8E, 0xFF, 0xFF);
                case BinType.Plastic: return new Color32(0xF5, 0xD8, 0x52, 0xFF);
                case BinType.Paper: return new Color32(0x55, 0xCF, 0x81, 0xFF);
                case BinType.Hazardous: return new Color32(0xF6, 0x6B, 0x64, 0xFF);
                default: return Color.gray;
            }
        }

        // ---------- Bin labels ----------
        // The player stands right at the bins and looks down at them, so a label flat on a bin's front wall is seen
        // edge-on. Instead each bin has a sloped label along its top front edge, tilted up toward the player.
        // Bin space: the bin's front wall faces -z, its outer surface is at z = -0.2, and its rim is at y = 0.6.

        /// <summary>Middle of the label's top edge, on the bin's front rim.</summary>
        public static readonly Vector3 BinLabelTop = new Vector3(0f, 0.6f, -0.2f);

        /// <summary>Width and slanted height of the label, including its colored border.</summary>
        public static readonly Vector2 BinLabelSize = new Vector2(0.32f, 0.15f);

        /// <summary>How far the label leans out from the bin's front wall, in degrees from vertical.</summary>
        public const float BinLabelTilt = 40f;

        /// <summary>Label orientation in bin space: its face points up and toward the player.</summary>
        public static Quaternion BinLabelRotation => Quaternion.Euler(BinLabelTilt, 0f, 0f);

        /// <summary>The direction the label's face points, in bin space.</summary>
        public static Vector3 BinLabelNormal => BinLabelRotation * Vector3.back;

        /// <summary>Center of the label's face, in bin space.</summary>
        public static Vector3 BinLabelCenter => BinLabelTop + BinLabelRotation * Vector3.down * (BinLabelSize.y * 0.5f);

        /// <summary>The dark printed face sits this far above the colored slope, and the text just above that.</summary>
        public const float BinLabelFaceLift = 0.0015f;
        public const float BinLabelTextLift = 0.003f;
    }
}
