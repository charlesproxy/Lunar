using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lunar {
// Skeleton loading for the profile page: the real layout (header, tabs, filters, rank, agents, stats, matches)
// drawn as placeholder blocks with a soft shimmer sweeping across, instead of a spinner on an empty page.
sealed class ProfileSkeleton:Panel {
    readonly Timer tick=new Timer{Interval=33};
    float phase;
    static readonly Color Block=Color.FromArgb(34,38,47);
    static readonly Color Card=Palette.Panel;

    public ProfileSkeleton(){
        DoubleBuffered=true;ResizeRedraw=true;BackColor=Palette.Bg;
        tick.Tick+=delegate{phase=(phase+0.018f)%1.6f;Invalidate();};
        VisibleChanged+=delegate{tick.Enabled=Visible;};
    }
    protected override void Dispose(bool disposing){if(disposing)tick.Dispose();base.Dispose(disposing);}

    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        int w=ClientSize.Width,h=ClientSize.Height;
        if(w<300)return;
        using(var region=new GraphicsPath()){
            // Header card
            var header=new Rectangle(22,12,w-44,134);
            CardShape(g,header);
            Bar(region,header.X+22,header.Y+30,110,12);
            Bar(region,header.X+22,header.Y+54,Math.Min(300,header.Width/3),34);
            Bar(region,header.X+22,header.Y+100,80,12);
            Bar(region,header.Right-130,header.Y+40,90,12);
            Bar(region,header.Right-130,header.Y+62,70,34);
            // Tabs + filters strip
            using(var strip=new SolidBrush(Palette.Sidebar))g.FillRectangle(strip,0,158,w,98);
            for(int i=0;i<4;i++)Bar(region,24+i*114,166,100,30,8);
            for(int i=0;i<4;i++)Bar(region,24+i*180,212,170,36,8);
            int top=272;
            Bar(region,24,top,260,10);
            top+=24;
            int left=24,leftWidth=Math.Min(244,(w-72)/3),rightX=left+leftWidth+16,rightWidth=w-rightX-24;
            // Left column: rank, agents, maps
            var rank=new Rectangle(left,top,leftWidth,164);CardShape(g,rank);
            Bar(region,rank.X+18,rank.Y+18,130,14);
            Circle(region,rank.X+20,rank.Y+54,46);
            Bar(region,rank.X+80,rank.Y+60,120,16);Bar(region,rank.X+80,rank.Y+84,60,12);
            Bar(region,rank.X+18,rank.Y+126,150,12);
            var agents=new Rectangle(left,rank.Bottom+14,leftWidth,150);CardShape(g,agents);
            Bar(region,agents.X+18,agents.Y+18,100,14);
            for(int i=0;i<2;i++){int y=agents.Y+52+i*48;Circle(region,agents.X+16,y,32);Bar(region,agents.X+58,y+3,80,12);Bar(region,agents.X+58,y+20,60,9);Bar(region,agents.Right-70,y+3,52,12);}
            // Right column: summary tiles + recent matches
            if(rightWidth>200){
                var summary=new Rectangle(rightX,top,rightWidth,220);CardShape(g,summary);
                Bar(region,summary.X+20,summary.Y+18,190,14);
                int cols=4,gap=10,tw=(summary.Width-40-gap*(cols-1))/cols;
                for(int row=0;row<2;row++)for(int c=0;c<cols;c++){
                    var t=new Rectangle(summary.X+20+c*(tw+gap),summary.Y+52+row*78,tw,66);
                    using(var shape=Skin.Round(t,10))using(var edge=new Pen(Palette.Border))g.DrawPath(edge,shape);
                    Bar(region,t.X+12,t.Y+14,Math.Min(90,tw-24),9);Bar(region,t.X+12,t.Y+34,Math.Min(70,tw-24),18);
                }
                var matches=new Rectangle(rightX,summary.Bottom+14,rightWidth,Math.Max(120,h-summary.Bottom-28));CardShape(g,matches);
                Bar(region,matches.X+20,matches.Y+18,150,14);
                for(int i=0;i<4&&matches.Y+56+i*64+52<matches.Bottom;i++){
                    int y=matches.Y+52+i*64;
                    Bar(region,matches.X+12,y,matches.Width-24,56,9);
                }
            }
            using(var fill=new SolidBrush(Block))g.FillPath(fill,region);
            // Shimmer: a soft diagonal highlight clipped to the placeholder blocks.
            float sweep=(phase-0.3f)*w*1.4f;
            var band=new RectangleF(sweep-w*0.25f,0,w*0.5f,h);
            if(band.Right>0&&band.Left<w){
                var state=g.Save();
                g.SetClip(region,CombineMode.Intersect);
                using(var shine=new LinearGradientBrush(band,Color.FromArgb(0,255,255,255),Color.FromArgb(0,255,255,255),15f)){
                    shine.InterpolationColors=new ColorBlend{
                        Colors=new[]{Color.FromArgb(0,183,143,246),Color.FromArgb(28,183,143,246),Color.FromArgb(0,183,143,246)},
                        Positions=new[]{0f,.5f,1f}};
                    g.FillRectangle(shine,band);
                }
                g.Restore(state);
            }
        }
    }
    static void CardShape(Graphics g,Rectangle r){
        using(var shape=Skin.Round(r,14))
        using(var fill=new SolidBrush(Card))
        using(var edge=new Pen(Palette.Border)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
    }
    static void Bar(GraphicsPath path,int x,int y,int w,int h,int radius=-1){
        if(w<=0||h<=0)return;
        using(var shape=Skin.Round(new Rectangle(x,y,w,h),radius<0?Math.Min(6,h/2):radius))path.AddPath(shape,false);
    }
    static void Circle(GraphicsPath path,int x,int y,int size){path.AddEllipse(x,y,size,size);}
}
}
