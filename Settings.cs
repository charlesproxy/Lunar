using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lunar {
enum SettingsGlyph { Language, Keyboard, Update }

sealed class SettingsCard {
    public SettingsGlyph Glyph;
    public Label Title;
    public Label[] Body;
    public Control Action;
    public Func<string> Keys;   // optional: draws the current shortcut as keycaps
    public string Badge;        // optional: small pill next to the title
    public Rectangle Bounds;
}

// Settings page: a header and one rounded card per preference (icon tile, title, description, control on the right).
sealed class SettingsSurface:Panel {
    public SettingsCard[] Cards=new SettingsCard[0];
    const int Top0=124,CardHeight=104,CardGap=14,MaxWidth=860;

    public SettingsSurface(){DoubleBuffered=true;ResizeRedraw=true;}

    int Left0{get{return 34;}}
    int CardWidth{get{return Math.Max(420,Math.Min(MaxWidth,ClientSize.Width-Left0*2));}}

    public void Arrange(){
        int y=Top0;
        foreach(var card in Cards){
            card.Bounds=new Rectangle(Left0,y,CardWidth,CardHeight);
            int textX=card.Bounds.X+84;
            int actionWidth=card.Action.Width;
            int keysWidth=card.Keys==null?0:KeycapsWidth(card.Keys())+14;
            int textWidth=Math.Max(160,card.Bounds.Right-24-actionWidth-keysWidth-16-textX);
            int bodyHeight=0;foreach(var body in card.Body)bodyHeight+=body.Height;
            int blockTop=card.Bounds.Y+(CardHeight-(26+bodyHeight))/2;
            card.Title.Location=new Point(textX,blockTop);
            card.Title.Width=textWidth;
            int by=blockTop+26;
            foreach(var body in card.Body){body.Location=new Point(textX,by);body.Width=textWidth;by+=body.Height;}
            card.Action.Location=new Point(card.Bounds.Right-24-actionWidth,card.Bounds.Y+(CardHeight-card.Action.Height)/2);
            y+=CardHeight+CardGap;
        }
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        bool pt=LunarLanguage.Portuguese;
        ProfileCanvas.T(g,pt?"Configurações":"Settings",24,Palette.Text,new Rectangle(Left0,26,ClientSize.Width-68,46),true);
        ProfileCanvas.T(g,pt?"Preferências do Lunar":"Lunar preferences",9,Palette.Muted,new Rectangle(Left0+2,70,ClientSize.Width-72,24));
        foreach(var card in Cards){
            var r=card.Bounds;
            if(r.Width<=0)continue;
            using(var shape=Skin.Round(r,14))
            using(var fill=new LinearGradientBrush(r,Color.FromArgb(14,Palette.Accent),Palette.Panel,0f))
            using(var edge=new Pen(Palette.Border)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
            var tile=new Rectangle(r.X+22,r.Y+(r.Height-44)/2,44,44);
            using(var shape=Skin.Round(tile,12))
            using(var fill=new SolidBrush(Palette.AccentSoft))
            using(var edge=new Pen(Color.FromArgb(70,Palette.Accent))){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
            DrawGlyph(g,card.Glyph,new Point(tile.X+22,tile.Y+22));
            if(card.Badge!=null){
                using(var font=new Font("Segoe UI",8,FontStyle.Bold)){
                    int titleWidth=TextRenderer.MeasureText(g,card.Title.Text,card.Title.Font).Width;
                    int w=TextRenderer.MeasureText(g,card.Badge,font).Width+10;
                    var pill=new Rectangle(card.Title.Left+titleWidth+6,card.Title.Top+3,w,19);
                    using(var shape=Skin.Round(pill,6))using(var fill=new SolidBrush(Color.FromArgb(40,Palette.Accent)))g.FillPath(fill,shape);
                    TextRenderer.DrawText(g,card.Badge,font,pill,Palette.Cyan,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                }
            }
            if(card.Keys!=null){
                string keys=card.Keys();
                int width=KeycapsWidth(keys);
                DrawKeycaps(g,keys,new Point(card.Action.Left-14-width,r.Y+(r.Height-30)/2));
            }
        }
    }
    static string[] Split(string keys){return (keys??"").Split(new[]{'+'},StringSplitOptions.RemoveEmptyEntries);}
    static int KeycapsWidth(string keys){
        int width=0;
        using(var font=new Font("Segoe UI",9,FontStyle.Bold))
            foreach(string part in Split(keys))width+=Math.Max(30,TextRenderer.MeasureText(part.Trim(),font).Width+16)+(width>0?18:0);
        return width;
    }
    static void DrawKeycaps(Graphics g,string keys,Point origin){
        int x=origin.X;bool first=true;
        using(var font=new Font("Segoe UI",9,FontStyle.Bold))
        using(var plus=new Font("Segoe UI",9))
            foreach(string raw in Split(keys)){
                string part=raw.Trim();
                if(!first){TextRenderer.DrawText(g,"+",plus,new Rectangle(x,origin.Y,18,30),Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);x+=18;}
                int w=Math.Max(30,TextRenderer.MeasureText(part,font).Width+16);
                var cap=new Rectangle(x,origin.Y,w,30);
                using(var shape=Skin.Round(cap,7))
                using(var fill=new SolidBrush(Palette.Raised))
                using(var edge=new Pen(Palette.Border)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
                using(var lip=new Pen(Color.FromArgb(60,0,0,0),2f))g.DrawLine(lip,cap.X+5,cap.Bottom-1,cap.Right-5,cap.Bottom-1);
                TextRenderer.DrawText(g,part,font,cap,Palette.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
                x+=w;first=false;
            }
    }
    static void DrawGlyph(Graphics g,SettingsGlyph glyph,Point center){
        var state=g.Save();
        g.TranslateTransform(center.X-12,center.Y-12);
        using(var pen=new Pen(Palette.Cyan,1.7f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}){
            switch(glyph){
                case SettingsGlyph.Language:
                    g.DrawEllipse(pen,3,3,18,18);
                    g.DrawEllipse(pen,8,3,8,18);
                    g.DrawLine(pen,3.5f,12,20.5f,12);
                    g.DrawArc(pen,5,4,14,6,200,140);
                    g.DrawArc(pen,5,14,14,6,20,140);
                    break;
                case SettingsGlyph.Keyboard:
                    using(var shape=Skin.Round(new Rectangle(2,6,20,13),3))g.DrawPath(pen,shape);
                    foreach(var x in new[]{6,10,14,18})g.DrawLine(pen,x,10,x+.2f,10);
                    foreach(var x in new[]{6,18})g.DrawLine(pen,x,13.5f,x+.2f,13.5f);
                    g.DrawLine(pen,9,15.5f,15,15.5f);
                    break;
                case SettingsGlyph.Update:
                    g.DrawArc(pen,4,4,16,16,-60,290);
                    g.DrawLines(pen,new[]{new PointF(16.5f,2.5f),new PointF(16.5f,6.8f),new PointF(12.4f,6.8f)});
                    g.DrawLine(pen,12,12,12,8.5f);g.DrawLine(pen,12,12,14.8f,13.8f);
                    break;
            }
        }
        g.Restore(state);
    }
}
}
