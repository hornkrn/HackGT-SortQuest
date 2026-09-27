using System;
using System.Collections.Generic;
using UnityEngine;

namespace SortQuest.Editor
{
    internal static class TrashArtMeshes
    {
        internal static Mesh Build(ItemType type,int variant,Vector3 rootScale,string name)
        {
            var g=new Geometry();int tile=(int)type*8+variant;
            switch(type)
            {
                case ItemType.AluminumCan: Can(g,variant,tile);break;
                case ItemType.PlasticBottle: Bottle(g,variant,tile);break;
                case ItemType.CardboardBox: Carton(g,variant,tile);break;
                case ItemType.CrumpledPaper: Paper(g,variant,tile);break;
                case ItemType.BatteryAA: Battery(g,variant,tile);break;
                case ItemType.PowerBank: Charger(g,variant,tile);break;
                default:throw new ArgumentOutOfRangeException(nameof(type));
            }
            return g.Mesh(name,rootScale);
        }
        private static void Can(Geometry g,int v,int tile)
        {
            var profile=new[]{P(-.061f,.026f),P(-.059f,.0305f),P(-.055f,.032f),P(-.044f,.0326f),
                P(-.021f,.033f),P(.006f,.033f),P(.031f,.0326f),P(.047f,.032f),P(.054f,.029f),
                P(.058f,.028f),P(.0608f,.0295f),P(.0608f,.026f),P(.0577f,.0257f)};
            g.Lathe(profile,28,tile,v,.14f+(v%4)*.035f,true);
            g.Disc(new Vector3(0,-.061f,0),.0258f,Vector3.down,48,28);
            g.Disc(new Vector3(0,.0577f,0),.0257f,Vector3.up,48,28);
            g.Ellipse(new Vector3(0,.05785f,-.011f),.009f,.0055f,49,18);
            // Raised pull tab with a real hole and rolled rim, all in the same mesh/material.
            g.Ring(new Vector3(0,.0596f,.005f),.008f,.012f,.0046f,.008f,48,18);
            g.Disc(new Vector3(0,.0599f,-.003f),.0023f,Vector3.up,54,12);
        }
        private static void Bottle(Geometry g,int v,int tile)
        {
            float neck=.013f+(v%3)*.0006f;
            var p=new List<Vector2>{P(-.11f,.024f),P(-.107f,.031f),P(-.10f,.034f),P(-.086f,.033f)};
            // Moulded reinforcing ribs and a squeezed waist, varied by bottle style.
            for(int i=0;i<6;i++)
            {
                float y=-.073f+i*.023f;
                float r=.033f-((v==1||v==5)?Mathf.Sin(i*.65f)*.0035f:0);
                p.Add(P(y,r));p.Add(P(y+.0025f,r-.0014f));p.Add(P(y+.005f,r));
            }
            p.Add(P(.061f,.031f));p.Add(P(.079f,.024f));p.Add(P(.091f,neck));
            p.Add(P(.096f,neck));p.Add(P(.097f,neck+.0015f));p.Add(P(.099f,neck));
            g.Lathe(p.ToArray(),24,tile,v,.055f+(v%4)*.014f,false);
            g.Disc(new Vector3(0,-.11f,0),.024f,Vector3.down,53,24);
            // Opaque cap avoids transparent plastic overdraw on the headset.
            g.Lathe(new[]{P(.098f,neck+.0016f),P(.108f,neck+.0016f),P(.11f,neck)},24,v%2==0?52:53,0,0,false);
            g.Disc(new Vector3(0,.11f,0),neck,Vector3.up,v%2==0?52:53,24);
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI*2/16;Vector3 radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                Vector3 tangent=new Vector3(-radial.z,0,radial.x)*.0004f;
                Vector3 center=radial*(neck+.00165f)+Vector3.up*.103f;
                g.Polygon(new[]{center-tangent-Vector3.up*.0038f,center+tangent-Vector3.up*.0038f,center+tangent+Vector3.up*.0038f,center-tangent+Vector3.up*.0038f},radial,54);
            }
        }
        private static void Carton(Geometry g,int v,int tile)
        {
            float bevel=.0028f+(v%4)*.0012f;
            g.Box(Vector3.zero,new Vector3(.25f,.15f,.2f),bevel,tile,56+v,v+1);
            // Top flaps meet in a recessed seam under the packing tape texture.
            g.Polygon(new[]{new Vector3(-.0006f,.07505f,-.092f),new Vector3(.0006f,.07505f,-.092f),new Vector3(.0006f,.07505f,.092f),new Vector3(-.0006f,.07505f,.092f)},Vector3.up,50);
        }
        private static void Paper(Geometry g,int v,int tile)
        {
            const int sectors=14,rings=8;
            var vertices=new Vector3[rings+1,sectors];
            var random=new System.Random(381+v*137);
            for(int j=0;j<=rings;j++)for(int i=0;i<sectors;i++)
            {
                float latitude=Mathf.PI*j/rings,angle=2*Mathf.PI*i/sectors;
                float radius=.032f+(float)random.NextDouble()*.0075f;
                if((i+j+v)%4==0)radius-=.009f;
                vertices[j,i]=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(angle),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(angle))*radius;
            }
            // Single shared pole positions keep the crumpled shell closed.
            for(int i=0;i<sectors;i++){vertices[0,i]=Vector3.up*.039f;vertices[rings,i]=Vector3.down*.039f;}
            for(int j=0;j<rings;j++)for(int i=0;i<sectors;i++)
            {
                int next=(i+1)%sectors;
                if(j>0)Fold(g,vertices[j,i],vertices[j,next],vertices[j+1,i],tile,i,j,v);
                if(j<rings-1)Fold(g,vertices[j,next],vertices[j+1,next],vertices[j+1,i],tile,i+2,j+1,v);
            }
        }
        private static void Fold(Geometry g,Vector3 a,Vector3 b,Vector3 c,int tile,int i,int j,int v)
        {
            var center=(a+b+c)/3;center*=((i+j+v)%3==0?.89f:1.045f);
            Vector2 uv=new Vector2((i%7)/7f,(j%5)/5f),ub=uv+new Vector2(.15f,.01f),uc=uv+new Vector2(.02f,.18f),um=(uv+ub+uc)/3;
            g.Polygon(new[]{a,b,center},center.normalized,tile,new[]{uv,ub,um});
            g.Polygon(new[]{b,c,center},center.normalized,tile,new[]{ub,uc,um});
            g.Polygon(new[]{c,a,center},center.normalized,tile,new[]{uc,uv,um});
        }
        private static void Battery(Geometry g,int v,int tile)
        {
            g.Lathe(new[]{P(-.0375f,.009f),P(-.0365f,.0103f),P(-.032f,.0105f),P(.025f,.0105f),P(.029f,.0104f),P(.033f,.0098f),P(.034f,.0093f)},24,tile,v,.018f,true);
            g.Disc(new Vector3(0,-.0375f,0),.009f,Vector3.down,48,24);
            g.Disc(new Vector3(0,.034f,0),.0093f,Vector3.up,49,24);
            g.Lathe(new[]{P(.034f,.0046f),P(.037f,.0046f),P(.0375f,.0039f)},18,48,0,0,false);
            g.Disc(new Vector3(0,.0375f,0),.0039f,Vector3.up,48,18);
        }
        private static void Charger(Geometry g,int v,int tile)
        {
            g.Box(Vector3.zero,new Vector3(.07f,.025f,.14f),.003f+(v%3)*.0004f,tile,tile,0);
            // Case seam, sockets with inset contacts, and a recessed power button.
            g.Box(new Vector3(0,0,0),new Vector3(.0701f,.00065f,.135f),.0002f,49,49,0);
            Port(g,new Vector3(-.012f,.001f,-.07008f),new Vector2(.016f,.006f));
            Port(g,new Vector3(.012f,.001f,-.07008f),new Vector2(.008f,.004f));
            g.Box(new Vector3(.0348f,.002f,.036f),new Vector3(.0005f,.004f,.012f),.0002f,49,49,0);
        }
        private static void Port(Geometry g,Vector3 p,Vector2 size)
        {
            // A dark recess framed by four metal edges; no extra objects or colliders.
            Vector3 a=new Vector3(size.x/2,0,0),b=new Vector3(0,size.y/2,0);
            g.Polygon(new[]{p-a-b,p-a+b,p+a+b,p+a-b},Vector3.back,49);
            g.Polygon(new[]{p-a-b,p-a-b+Vector3.down*.0006f,p+a-b+Vector3.down*.0006f,p+a-b},Vector3.back,48);
            g.Polygon(new[]{p-a+b,p+a+b,p+a+b+Vector3.up*.0006f,p-a+b+Vector3.up*.0006f},Vector3.back,48);
            Vector3 inner=p+Vector3.back*.00008f;
            g.Polygon(new[]{inner-a*.7f-b*.3f,inner-a*.7f+b*.3f,inner+a*.7f+b*.3f,inner+a*.7f-b*.3f},Vector3.back,51);
        }
        private static Vector2 P(float y,float radius)=>new Vector2(y,radius);

        internal sealed class Geometry
        {
            private readonly List<Vector3> positions=new List<Vector3>(),normals=new List<Vector3>();
            private readonly List<Vector2> uvs=new List<Vector2>();
            private readonly List<int> indices=new List<int>();
            internal void Polygon(Vector3[] points,Vector3 outward,int tile,Vector2[] textureUV=null)
            {
                int start=positions.Count;
                Vector3 n=Vector3.Cross(points[1]-points[0],points[2]-points[0]).normalized;
                bool reverse=Vector3.Dot(n,outward)<0;if(reverse)n=-n;
                Vector2[] defaults=points.Length==3?new[]{Vector2.zero,Vector2.right,Vector2.up}:new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                for(int i=0;i<points.Length;i++){positions.Add(points[i]);normals.Add(n);uvs.Add(TrashArtAtlas.UV(tile,textureUV==null?defaults[i%defaults.Length]:textureUV[i]));}
                for(int i=1;i<points.Length-1;i++){indices.Add(start);indices.Add(start+(reverse?i+1:i));indices.Add(start+(reverse?i:i+1));}
            }
            internal void Disc(Vector3 center,float r,Vector3 normal,int tile,int segments)
            {
                for(int i=0;i<segments;i++)
                {
                    float a=2*Mathf.PI*i/segments,b=2*Mathf.PI*(i+1)/segments;
                    var u=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var v=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                    Polygon(new[]{center,center+u*r,center+v*r},normal,tile,new[]{Vector2.one*.5f,new Vector2(u.x,u.z)*.5f+Vector2.one*.5f,new Vector2(v.x,v.z)*.5f+Vector2.one*.5f});
                }
            }
            internal void Ellipse(Vector3 center,float rx,float rz,int tile,int segments)
            {
                for(int i=0;i<segments;i++){float a=i*2*Mathf.PI/segments,b=(i+1)*2*Mathf.PI/segments;
                    Polygon(new[]{center,center+new Vector3(Mathf.Cos(a)*rx,0,Mathf.Sin(a)*rz),center+new Vector3(Mathf.Cos(b)*rx,0,Mathf.Sin(b)*rz)},Vector3.up,tile);}
            }
            internal void Ring(Vector3 center,float rx,float rz,float ix,float iz,int tile,int segments)
            {
                for(int i=0;i<segments;i++){float a=i*2*Mathf.PI/segments,b=(i+1)*2*Mathf.PI/segments;
                    Polygon(new[]{center+new Vector3(Mathf.Cos(a)*rx,0,Mathf.Sin(a)*rz),center+new Vector3(Mathf.Cos(b)*rx,0,Mathf.Sin(b)*rz),center+new Vector3(Mathf.Cos(b)*ix,0,Mathf.Sin(b)*iz),center+new Vector3(Mathf.Cos(a)*ix,0,Mathf.Sin(a)*iz)},Vector3.up,tile);}
            }
            internal void Lathe(Vector2[] profile,int segments,int tile,int variant,float dent,bool metalEnds)
            {
                float min=profile[0].x,max=profile[profile.Length-1].x;
                foreach(var p in profile){min=Mathf.Min(min,p.x);max=Mathf.Max(max,p.x);}
                var pos=new Vector3[profile.Length,segments+1];
                for(int j=0;j<profile.Length;j++)for(int i=0;i<=segments;i++)
                {
                    float a=i*2*Mathf.PI/segments,t=Mathf.InverseLerp(min,max,profile[j].x);
                    float angle=Mathf.DeltaAngle(a*Mathf.Rad2Deg,(variant*67+38)%360)*Mathf.Deg2Rad;
                    float opposite=Mathf.DeltaAngle(a*Mathf.Rad2Deg,(variant*67+215)%360)*Mathf.Deg2Rad;
                    float d=dent*(Mathf.Exp(-angle*angle*3)+.55f*Mathf.Exp(-opposite*opposite*5))*Mathf.Pow(Mathf.Sin(Mathf.PI*t),3);
                    float r=profile[j].y*(1-d);
                    pos[j,i]=new Vector3(Mathf.Cos(a)*r,profile[j].x,Mathf.Sin(a)*r);
                }
                for(int j=0;j<profile.Length-1;j++)for(int i=0;i<segments;i++)
                {
                    float v0=Mathf.InverseLerp(min,max,profile[j].x),v1=Mathf.InverseLerp(min,max,profile[j+1].x);
                    int faceTile=metalEnds&&(v0<.035f||v0>.90f)?48:tile;
                    int start=positions.Count;
                    Vector3[] p={pos[j,i],pos[j,i+1],pos[j+1,i+1],pos[j+1,i]};
                    Vector2[] uv={new Vector2((float)i/segments,v0),new Vector2((float)(i+1)/segments,v0),new Vector2((float)(i+1)/segments,v1),new Vector2((float)i/segments,v1)};
                    // Smooth around the circumference, with explicit profile normals at each band.
                    for(int k=0;k<4;k++)
                    {
                        int column=(i+(k==1||k==2?1:0))%segments,row=j+(k>=2?1:0);
                        Vector3 along=pos[j+1,column]-pos[j,column];
                        Vector3 around=pos[row,(column+1)%segments]-pos[row,(column+segments-1)%segments];
                        Vector3 n=Vector3.Cross(along,around).normalized;
                        positions.Add(p[k]);normals.Add(n);uvs.Add(TrashArtAtlas.UV(faceTile,uv[k]));
                    }
                    indices.Add(start);indices.Add(start+2);indices.Add(start+1);
                    indices.Add(start);indices.Add(start+3);indices.Add(start+2);
                }
            }
            internal void Box(Vector3 center,Vector3 size,float bevel,int sideTile,int topTile,int damage)
            {
                Vector3 h=size*.5f,inner=h-Vector3.one*bevel;
                Func<Vector3,Vector3> deform=p=>
                {
                    if(damage>0)
                    {
                        float influence=Mathf.Max(0,(p.x/h.x)*(damage%2==0?1:-1))*Mathf.Max(0,p.z/h.z);
                        p.y-=influence*.0025f*(damage%3+1)*Mathf.Max(0,p.y/h.y);
                    }
                    return center+p;
                };
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    int u=(axis+1)%3,v=(axis+2)%3;var p=new Vector3[4];int[] su={-1,1,1,-1},sv={-1,-1,1,1};
                    for(int j=0;j<4;j++){p[j][axis]=sign*h[axis];p[j][u]=su[j]*inner[u];p[j][v]=sv[j]*inner[v];p[j]=deform(p[j]);}
                    Vector3 n=Vector3.zero;n[axis]=sign;
                    int faceTile=axis==1?topTile:sideTile;
                    if(damage>0&&axis!=1&&!(axis==2&&sign==-1))faceTile=56+(damage-1);
                    Vector2[] uv=axis==1?new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right}:null;
                    Polygon(p,n,faceTile,uv);
                }
                for(int axis=0;axis<3;axis++)for(int a=-1;a<=1;a+=2)for(int b=-1;b<=1;b+=2)
                {
                    int u=(axis+1)%3,v=(axis+2)%3;var p=new Vector3[4];
                    for(int j=0;j<4;j++){p[j][axis]=(j<2?-1:1)*inner[axis];p[j][u]=a*(j==0||j==3?h[u]:inner[u]);p[j][v]=b*(j==0||j==3?inner[v]:h[v]);p[j]=deform(p[j]);}
                    Vector3 n=Vector3.zero;n[u]=a;n[v]=b;Polygon(p,n,damage>0?51:54);
                }
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                    Polygon(new[]{deform(new Vector3(x*h.x,y*inner.y,z*inner.z)),deform(new Vector3(x*inner.x,y*h.y,z*inner.z)),deform(new Vector3(x*inner.x,y*inner.y,z*h.z))},new Vector3(x,y,z),damage>0?51:54);
            }
            internal Mesh Mesh(string name,Vector3 scale)
            {
                // Keep the existing root transforms and colliders in their original local frames.
                for(int i=0;i<positions.Count;i++)
                {
                    var p=positions[i];positions[i]=new Vector3(p.x/scale.x,p.y/scale.y,p.z/scale.z);
                    normals[i]=Vector3.Scale(normals[i],scale).normalized;
                }
                var mesh=new Mesh{name=name};mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uvs);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
            }
        }
    }
}
