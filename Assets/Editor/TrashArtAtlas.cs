using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SortQuest.Editor
{
    // Original procedural artwork. Everything is baked in the editor; no runtime texture work.
    internal static class TrashArtAtlas
    {
        internal const int TileSize = 256, Size = TileSize * 8, Gutter = 8;
        private static Color[] pixels;
        private static int tile;
        private static System.Random random;
        private static readonly Color Ink = C("253032"), Paper = C("E0D6B8"), Metal = C("9AABA9");
        internal static readonly string[][] Names =
        {
            new[]{"Crushed Cola", "Orange Soda", "Lime Seltzer", "Iced Tea", "Ginger Fizz", "Berry Soda", "Sparkling Water", "Citrus Energy"},
            new[]{"Spring Water", "Sports Drink", "Mineral Water", "Citrus Drink", "Iced Coffee", "Berry Juice", "Green Tea", "Cloudy Water"},
            new[]{"Returned Parcel", "Water Stained Box", "Produce Carton", "Express Parcel", "Reused Shipping Box", "Taped Delivery", "Storage Carton", "Fragile Package"},
            new[]{"Crumpled Newsprint", "Used Receipt", "Notebook Page", "Brown Packing Paper", "Junk Mail", "Coffee Stained Page", "Discarded Form", "Blue Graph Paper"},
            new[]{"Worn Alkaline", "Copper Top", "Silver Cell", "Green Rechargeable", "Blue Alkaline", "Scraped Red Cell", "Industrial Cell", "Faded Yellow Cell"},
            new[]{"Scuffed Black Charger", "Worn White Charger", "Blue Travel Charger", "Dented Silver Charger", "Old Red Charger", "Taped Charger", "Green Pocket Charger", "Scratched Graphite Charger"}
        };
        private static readonly string[] Colors = {"A83830","DA842D","668B4A","B69560","CEA438","816278","527D92","677541"};

        internal static Vector2 UV(int index, Vector2 uv)
        {
            float span = TileSize - 2 * Gutter;
            return new Vector2(((index % 8) * TileSize + Gutter + uv.x * span) / Size,
                ((index / 8) * TileSize + Gutter + uv.y * span) / Size);
        }

        internal static Texture2D Build(string folder)
        {
            pixels = new Color[Size * Size];
            for (tile = 0; tile < 64; tile++)
            {
                random = new System.Random(7181 + tile * 139);
                int type = tile / 8, v = tile % 8;
                Color body = C(Colors[v]);
                float smooth = .27f;
                if (type == 1) body = C(new[]{"99AFAC","A7B8AF","85A7A4","B2AB7E","B5A18A","AB9C9A","879D89","A3B3B1"}[v]);
                if (type == 2 || type == 7) { body = C(new[]{"A58253","9C7D54","AD9564","AC8358","8C7753","B39262","A88D61","997A50"}[v]); smooth=.1f; }
                if (type == 3) { body = v == 3 ? C("AA936C") : Paper; smooth=.07f; }
                if (type == 4) body = C(new[]{"494B43","A26946","A9B3AB","4A7358","416582","994B3C","494A4C","C3AF51"}[v]);
                if (type == 5) body = C(new[]{"3C4244","BFC0B7","49667C","969F9B","8C4942","646B6A","63745B","51565A"}[v]);
                if (type == 6) { body = C(new[]{"AAB7B2","273033","625548","C4B38B","CCC8AD","537368","737A78","E2D4A3"}[v]); smooth = v == 0 ? .55f : .15f; }
                Fill(body, smooth);
                switch (type)
                {
                    case 0: Can(v); break;
                    case 1: Bottle(v); break;
                    case 2: Carton(v); break;
                    case 3: PrintedPaper(v); break;
                    case 4: Cell(v); break;
                    case 5: Charger(v); break;
                    case 7: BoxTop(v); break;
                }
                Wear(type, v, body);
                // Extend the artwork through an eight-pixel gutter before generating mipmaps.
                for(int y=0;y<TileSize;y++) for(int x=0;x<TileSize;x++)
                    if(x<Gutter||y<Gutter||x>=TileSize-Gutter||y>=TileSize-Gutter)
                        pixels[Index(x,y)] = pixels[Index(Mathf.Clamp(x,Gutter,TileSize-Gutter-1),Mathf.Clamp(y,Gutter,TileSize-Gutter-1))];
            }
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            texture.SetPixels(pixels);texture.Apply();
            string path=folder+"/UsedTrashAtlas.png";File.WriteAllBytes(path,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=false;
            importer.mipmapEnabled=true;importer.isReadable=false;importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=2;importer.maxTextureSize=Size;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            { name="Android",overridden=true,maxTextureSize=Size,format=TextureImporterFormat.ASTC_6x6,compressionQuality=100 });
            importer.SaveAndReimport();pixels=null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Can(int v)
        {
            Rect(8,12,240,12,Metal);Rect(8,228,240,12,Metal);
            for(int y=45;y<217;y++) for(int x=8;x<248;x++)
                if((x+y/2+v*11)%115<8) Blend(x,y,Paper,.38f);
            Rect(36,85,150,82,C(Colors[v])*.79f);
            string[] brand={"COLA","ORANGE","LIME","TEA","GINGER","BERRY","SPRING","CITRUS"};
            Text(brand[v],45,124,3,Paper);Text(v==2||v==6?"SPARKLING":"REFRESH",46,98,2,Paper);
            Text("330 ML",48,55,2,Paper);Barcode(195,55,32,54);
            for(int i=0;i<6;i++) Line(190,132+i*6,238,132+i*6,1,Paper,.65f);
        }
        private static void Bottle(int v)
        {
            Color accent=C(Colors[(v+2)%8]);
            Rect(8,84,240,91,accent);Rect(8,87,240,3,Paper);Rect(8,169,240,3,Paper);
            Text(new[]{"SPRING","SPORT","MINERAL","CITRUS","COFFEE","BERRY","LEAF","PURE"}[v],40,127,3,Paper);
            Text(v==4?"COLD BREW":"500 ML",45,105,2,Paper);Barcode(202,103,30,49);
            // A ragged, peeled label edge and a small adhesive residue patch.
            for(int x=9;x<248;x++) Rect(x,83+(int)(Mathf.Sin(x*.19f+v)*2),1,3,Paper);
            Rect(18+v*3,154,22,15,Paper);
            for(int y=32;y<220;y+=22) if(y<80||y>178) Line(8,y,247,y,2,Paper,.15f);
        }
        private static void Carton(int v)
        {
            Rect(36,73,148,117,Paper);Text(v==2?"PRODUCE":"SORT POST",44,163,2,Ink);
            Text("LOT "+(21+v),44,140,2,Ink);Barcode(47,91,124,34);
            Text("REUSE",160,31,2,Ink);
            for(int i=0;i<3;i++)Line(46,134-i*5,153-i*9,134-i*5,1,Ink,.7f);
            // Corrugated scuffed corners and shipping damage.
            for(int i=0;i<18;i++) Line(8,10+i*2,26+(i%4)*3,10+i*2,1,Paper,.48f);
            if(v%2==0){Line(12,206,225,219,3,Ink,.28f);Line(12,209,225,222,1,Paper,.65f);}
            Text("UP",193,195,2,Ink);
        }
        private static void PrintedPaper(int v)
        {
            if(v==3) { for(int i=0;i<9;i++)Line(8,20+i*24,248,27+i*23,1,Ink,.1f);return; }
            if(v==2||v==7)
            {
                for(int y=15;y<247;y+=14)Line(8,y,247,y,1,C("668B98"),.5f);
                for(int x=v==7?16:39;x<247;x+=v==7?14:250)Line(x,8,x,247,1,C("A86860"),.4f);
            }
            Text(new[]{"DAILY NEWS","THANK YOU","NOTES","","LOCAL MAIL","MEMO","RECORD","PROJECT"}[v],16,214,v==1?2:3,Ink);
            int columns=v==0||v==4?3:1;
            for(int col=0;col<columns;col++)
                for(int row=0;row<24;row++)
                {
                    int start=16+col*(226/columns),len=R(28,226/columns-5);
                    for(int x=0;x<len;x+=R(4,10))Line(start+x,201-row*7,start+x+R(2,6),201-row*7,1,Ink,.65f);
                }
            if(v==1){Barcode(30,26,180,30);Text("TOTAL 12.49",40,73,2,Ink);}
            if(v==5) Ring(152,110,48,5,C("80572F"),.28f);
        }
        private static void Cell(int v)
        {
            Rect(8,186,240,52,v==1?C("B88B61"):Metal);Rect(8,13,240,12,Metal);
            Rect(29,86,144,61,Ink);Text(new[]{"VOLT","CELL","POWER","RECHARGE","ENERGY","VOLT","INDUSTRY","CELL"}[v],35,121,2,Paper);
            Text(v==3?"AA 1.2V":"AA 1.5V",39,96,2,Paper);Text("+",109,204,4,Ink);
            Barcode(193,69,34,78);Text("RECYCLE",39,56,2,Paper);
            if(v%3==0) { Rect(18,157,38,16,Metal);Line(18,158,59,172,3,Ink,.45f); }
        }
        private static void Charger(int v)
        {
            Color print=v==1||v==3?Ink:Paper;
            Text("RECHARGE",37,175,3,print);Text("5000 MAH",45,144,2,print);
            Text("USB 5V",55,58,2,print);Text("RECYCLE",55,42,2,print);
            for(int i=0;i<4;i++){Rect(56+i*17,115,10,5,Ink);Rect(57+i*17,116,7,2,Paper);}
            Rect(182,166,38,23,C("CDC7AF"));Text("QC",188,172,2,Ink);
            if(v==5) { Rect(8,77,240,23,C("7A7C72"));for(int y=79;y<100;y+=3)Line(8,y,248,y,1,Paper,.12f); }
        }
        private static void BoxTop(int v)
        {
            Rect(95,8,62,240,C("B9A273"));Line(126,8,126,248,2,Ink,.35f);
            for(int y=9;y<246;y+=3)Line(97,y,154,y,1,Paper,.1f);
            Line(8,128,95,128,2,Ink,.42f);Line(157,128,248,128,2,Ink,.42f);
            if(v%2==1)Rect(8,46+v*8,240,25,C("AD976D"));
            Text(v==7?"FRAGILE":"REUSED",15,210,2,Ink);
        }
        private static void Wear(int type,int v,Color body)
        {
            // Abraded patches expose the substrate beneath the print. Broken edges avoid a clean decal look.
            if(type==0||type==1||type==4||type==5)
            {
                int cx=58+(v*17)%112,cy=58+(v*29)%140;
                for(int y=-18;y<=18;y++)for(int x=-22;x<=22;x++)
                {
                    float edge=1-Mathf.Sqrt(x*x/484f+y*y/324f);
                    if(edge>random.NextDouble()*.6f)
                        Blend(cx+x,cy+y,type==1?Paper:Metal,.4f+(float)random.NextDouble()*.28f);
                }
            }
            if(type==2&&(v==1||v==4))
                for(int i=0;i<7;i++)Stain(20+i*32,31+i%3*9,35,C("65472D"),.5f);
            for(int i=0;i<(type==6?22:120);i++)
            {
                int x=R(8,248),y=R(8,248),length=R(2,27);
                Color scratch=i%3==0?(type==0||type==4||type==5?Metal:Paper):Ink;
                Line(x,y,x+length,y+R(-4,5),R(1,3),scratch,(float)random.NextDouble()*.24f+.1f);
            }
            // Soft irregular dirt patches, with concentrated wear at the lower rim.
            for(int i=0;i<8;i++) Stain(R(12,245),R(12,245),R(7,26),C("62503B"),type==3?.09f:.17f);
            for(int y=8;y<248;y++)for(int x=8;x<248;x++)
            {
                float noise=((float)random.NextDouble()-.5f)*.065f;
                var c=pixels[Index(x,y)];float grime=(y<27||y>230)?.94f:1;
                pixels[Index(x,y)]=new Color(Mathf.Clamp01(c.r*grime+noise),Mathf.Clamp01(c.g*grime+noise),Mathf.Clamp01(c.b*grime+noise),c.a);
            }
        }
        private static Color C(string hex){ColorUtility.TryParseHtmlString("#"+hex,out var c);return c;}
        private static int R(int min,int max)=>random.Next(min,max);
        private static int Index(int x,int y)=>(tile/8*TileSize+y)*Size+tile%8*TileSize+x;
        private static void Fill(Color color,float smooth){color.a=smooth;for(int y=0;y<TileSize;y++)for(int x=0;x<TileSize;x++)pixels[Index(x,y)]=color;}
        private static void Blend(int x,int y,Color c,float strength)
        {if(x<8||y<8||x>=248||y>=248)return;int i=Index(x,y);float a=pixels[i].a;pixels[i]=Color.Lerp(pixels[i],c,strength);pixels[i].a=a;}
        private static void Rect(int x,int y,int w,int h,Color c){for(int j=y;j<y+h;j++)for(int i=x;i<x+w;i++)Blend(i,j,c,1);}
        private static void Line(int x,int y,int x2,int y2,int width,Color c,float opacity)
        {int steps=Math.Max(Math.Abs(x2-x),Math.Abs(y2-y));for(int i=0;i<=steps;i++){float t=steps==0?0:(float)i/steps;for(int d=0;d<width;d++)Blend(Mathf.RoundToInt(Mathf.Lerp(x,x2,t)),Mathf.RoundToInt(Mathf.Lerp(y,y2,t))+d,c,opacity);}}
        private static void Stain(int x,int y,int radius,Color c,float opacity)
        {for(int j=-radius;j<=radius;j++)for(int i=-radius;i<=radius;i++){float d=Mathf.Sqrt(i*i+j*j)/radius;Blend(x+i,y+j,c,Mathf.Max(0,1-d)*opacity);}}
        private static void Ring(int x,int y,int radius,int width,Color c,float opacity)
        {for(int j=-radius-width;j<=radius+width;j++)for(int i=-radius-width;i<=radius+width;i++){float d=Mathf.Abs(Mathf.Sqrt(i*i+j*j)-radius);if(d<width)Blend(x+i,y+j,c,(1-d/width)*opacity);}}
        private static void Barcode(int x,int y,int w,int h){Rect(x-2,y-2,w+4,h+4,Paper);for(int i=0;i<w;i+=R(2,5))Rect(x+i,y,R(1,3),h,Ink);}
        private static readonly Dictionary<char,string> Font=new Dictionary<char,string>
        {
            ['A']="01110100011000111111100011000110001",['B']="11110100011000111110100011000111110",['C']="01111100001000010000100001000001111",
            ['D']="11110100011000110001100011000111110",['E']="11111100001000011110100001000011111",['F']="11111100001000011110100001000010000",
            ['G']="01111100001000010111100011000101111",['H']="10001100011000111111100011000110001",['I']="11111001000010000100001000010011111",
            ['J']="00111000100001000010000101001001100",['K']="10001100101010011000101001001010001",['L']="10000100001000010000100001000011111",
            ['M']="10001110111010110101100011000110001",['N']="10001110011010110011100011000110001",['O']="01110100011000110001100011000101110",
            ['P']="11110100011000111110100001000010000",['Q']="01110100011000110001101011001001101",['R']="11110100011000111110101001001010001",
            ['S']="01111100001000001110000010000111110",['T']="11111001000010000100001000010000100",['U']="10001100011000110001100011000101110",
            ['V']="10001100011000110001100010101000100",['W']="10001100011000110101101011101110001",['X']="10001100010101000100010101000110001",
            ['Y']="10001100010101000100001000010000100",['Z']="11111000010001000100010001000011111",
            ['0']="01110100011001110101110011000101110",['1']="00100011000010000100001000010001110",['2']="01110100010000100010001000100011111",
            ['3']="11110000010000101110000010000111110",['4']="00010001100101010010111110001000010",['5']="11111100001000011110000010000111110",
            ['6']="01110100001000011110100011000101110",['7']="11111000010001000100010000100001000",['8']="01110100011000101110100011000101110",['9']="01110100011000101111000010000101110",
            ['+']="00000001000010011111001000010000000",['.']="00000000000000000000000000011000110"
        };
        private static void Text(string text,int x,int y,int scale,Color color)
        {foreach(char c in text){if(Font.TryGetValue(c,out string glyph))for(int row=0;row<7;row++)for(int col=0;col<5;col++)if(glyph[row*5+col]=='1')Rect(x+col*scale,y+(6-row)*scale,scale,scale,color);x+=6*scale;}}
    }
}
