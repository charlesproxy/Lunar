using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Lunar {
enum SettingsGlyph { Language, Keyboard, Update, Support, Startup }

sealed class SettingsCard {
    public SettingsGlyph Glyph;
    public Label Title;
    public Label[] Body;
    public Control Action;
    public Func<string> Keys;
    public string Badge;
    public Rectangle Bounds;
}

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
            Color surface=CardColorAt(card.Bounds,card.Action.Left+card.Action.Width/2);
            var button=card.Action as SoftButton;if(button!=null){button.SurfaceColor=surface;button.Invalidate();}
            var select=card.Action as SoftSelect;if(select!=null){select.SurfaceColor=surface;select.Invalidate();}
            y+=CardHeight+CardGap;
        }
        Invalidate();
    }
    Color CardColorAt(Rectangle card,int x){
        var left=Blend(Palette.Accent,BackColor,14/255f);
        float t=card.Width<=0?1f:Math.Max(0f,Math.Min(1f,(x-card.X)/(float)card.Width));
        return Color.FromArgb((int)Math.Round(left.R+(Palette.Panel.R-left.R)*t),(int)Math.Round(left.G+(Palette.Panel.G-left.G)*t),(int)Math.Round(left.B+(Palette.Panel.B-left.B)*t));
    }
    static Color Blend(Color top,Color bottom,float alpha){
        return Color.FromArgb((int)Math.Round(bottom.R+(top.R-bottom.R)*alpha),(int)Math.Round(bottom.G+(top.G-bottom.G)*alpha),(int)Math.Round(bottom.B+(top.B-bottom.B)*alpha));
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        ProfileCanvas.T(g,"Settings",24,Palette.Text,new Rectangle(Left0,26,ClientSize.Width-68,46),true);
        ProfileCanvas.T(g,"Lunar preferences",9,Palette.Muted,new Rectangle(Left0+2,70,ClientSize.Width-72,24));
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
                    int titleWidth=LunarText.MeasureText(g,card.Title.Text,card.Title.Font).Width;
                    int w=LunarText.MeasureText(g,card.Badge,font).Width+10;
                    var pill=new Rectangle(card.Title.Left+titleWidth+6,card.Title.Top+3,w,19);
                    using(var shape=Skin.Round(pill,6))using(var fill=new SolidBrush(Color.FromArgb(40,Palette.Accent)))g.FillPath(fill,shape);
                    LunarText.DrawText(g,card.Badge,font,pill,Palette.Cyan,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
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
            foreach(string part in Split(keys))width+=Math.Max(30,LunarText.MeasureText(part.Trim(),font).Width+16)+(width>0?18:0);
        return width;
    }
    static void DrawKeycaps(Graphics g,string keys,Point origin){
        int x=origin.X;bool first=true;
        using(var font=new Font("Segoe UI",9,FontStyle.Bold))
        using(var plus=new Font("Segoe UI",9))
            foreach(string raw in Split(keys)){
                string part=raw.Trim();
                if(!first){LunarText.DrawText(g,"+",plus,new Rectangle(x,origin.Y,18,30),Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);x+=18;}
                int w=Math.Max(30,LunarText.MeasureText(part,font).Width+16);
                var cap=new Rectangle(x,origin.Y,w,30);
                using(var shape=Skin.Round(cap,7))
                using(var fill=new SolidBrush(Palette.Raised))
                using(var edge=new Pen(Palette.Border)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
                using(var lip=new Pen(Color.FromArgb(60,0,0,0),2f))g.DrawLine(lip,cap.X+5,cap.Bottom-1,cap.Right-5,cap.Bottom-1);
                LunarText.DrawText(g,part,font,cap,Palette.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
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
                case SettingsGlyph.Support:
                    using(var page=Skin.Round(new Rectangle(5,3,14,18),2))g.DrawPath(pen,page);
                    g.DrawLine(pen,8.5f,9,15.5f,9);g.DrawLine(pen,8.5f,12.5f,15.5f,12.5f);g.DrawLine(pen,8.5f,16,13,16);
                    break;
                case SettingsGlyph.Startup:
                    g.DrawArc(pen,4,5,16,16,-60,300);
                    g.DrawLine(pen,12,3,12,12);
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

sealed class UpdateReadyCard:Control {
    public event Action UpdateClicked;
    public string Version="";
    bool hoverButton,hoverClose,pressed;
    int targetTop,slide;
    readonly Timer animation=new Timer{Interval=15};
    static readonly Color Glow=Color.FromArgb(183,143,246),Deep=Color.FromArgb(132,92,226);

    public UpdateReadyCard(){
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        Size=new Size(412,86);
        BackColor=Palette.Bg;
        Visible=false;TabStop=false;
        Font=new Font("Segoe UI",9);
        animation.Tick+=delegate{
            slide=Math.Max(0,slide-3);
            Top=targetTop+slide*slide/12;
            if(slide==0)animation.Stop();
        };
    }
    Rectangle ButtonBounds{get{return new Rectangle(Width-150,(Height-38)/2+4,132,38);}}
    Rectangle CloseBounds{get{return new Rectangle(Width-26,8,16,16);}}

    public void Present(Size client,bool animate){
        targetTop=Math.Max(12,client.Height-Height-18);
        Left=Math.Max(12,client.Width-Width-18);
        if(animate&&!Visible){slide=24;Top=targetTop+slide*slide/12;Visible=true;BringToFront();animation.Start();}
        else{if(!animation.Enabled)Top=targetTop;Visible=true;BringToFront();}
        Invalidate();
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        if(Width>40&&Height>40)using(var shape=Skin.Round(new Rectangle(0,0,Width,Height),18))Region=new Region(shape);
    }
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var card=new Rectangle(0,0,Width-1,Height-1);
        using(var shape=Skin.Round(card,18)){
            using(var fill=new LinearGradientBrush(card,Color.FromArgb(46,37,66),Palette.Panel,LinearGradientMode.Horizontal))g.FillPath(fill,shape);
            using(var edge=new LinearGradientBrush(card,Color.FromArgb(210,Glow),Color.FromArgb(70,Glow),LinearGradientMode.Horizontal))
            using(var pen=new Pen(edge,1.2f))g.DrawPath(pen,shape);
        }
        using(var glowPath=new GraphicsPath()){
            glowPath.AddEllipse(-6,-10,92,106);
            using(var glow=new PathGradientBrush(glowPath){CenterColor=Color.FromArgb(70,Glow),SurroundColors=new[]{Color.FromArgb(0,Glow)}})g.FillPath(glow,glowPath);
        }
        var tile=new Rectangle(18,(Height-46)/2,46,46);
        using(var shape=Skin.Round(tile,13))
        using(var fill=new LinearGradientBrush(tile,Glow,Deep,LinearGradientMode.ForwardDiagonal))g.FillPath(fill,shape);
        using(var pen=new Pen(Color.White,2.2f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}){
            float cx=tile.X+23,cy=tile.Y+21;
            g.DrawLine(pen,cx,cy-10,cx,cy+5);
            g.DrawLines(pen,new[]{new PointF(cx-6,cy-1),new PointF(cx,cy+5),new PointF(cx+6,cy-1)});
            g.DrawLines(pen,new[]{new PointF(cx-10,cy+6),new PointF(cx-10,cy+11),new PointF(cx+10,cy+11),new PointF(cx+10,cy+6)});
        }
        int x=tile.Right+14;
        string badge=LunarLanguage.Translate("UPDATE READY");
        using(var badgeFont=new Font("Segoe UI",7,FontStyle.Bold)){
            var size=LunarText.MeasureText(g,badge,badgeFont,Size.Empty,TextFormatFlags.NoPadding);
            var pill=new Rectangle(x,15,size.Width+18,19);
            using(var shape=Skin.Round(pill,9))using(var fill=new SolidBrush(Color.FromArgb(70,Glow)))g.FillPath(fill,shape);
            using(var dot=new SolidBrush(Color.FromArgb(120,230,180)))g.FillEllipse(dot,pill.X+7,pill.Y+7,5,5);
            LunarText.DrawText(g,badge,badgeFont,new Rectangle(pill.X+14,pill.Y,pill.Width-14,pill.Height),Color.FromArgb(232,220,255),
                TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPrefix);
        }
        int textWidth=ButtonBounds.X-x-8;
        string title=Version==""?LunarLanguage.Translate("New Lunar version"):"Lunar "+Version;
        using(var titleFont=new Font("Segoe UI",11,FontStyle.Bold))
            LunarText.DrawText(g,title,titleFont,new Rectangle(x,36,textWidth,24),Palette.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        using(var subFont=new Font("Segoe UI",8))
            LunarText.DrawText(g,"Restarts by itself in seconds",subFont,new Rectangle(x,58,textWidth,18),Palette.Muted,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        var button=ButtonBounds;
        if(pressed&&hoverButton)button.Offset(0,1);
        Color from=hoverButton?Color.FromArgb(204,172,255):Glow,to=hoverButton?Color.FromArgb(152,112,240):Deep;
        if(hoverButton){
            var halo=Rectangle.Inflate(button,3,3);
            using(var shape=Skin.Round(halo,21))using(var fill=new SolidBrush(Color.FromArgb(55,Glow)))g.FillPath(fill,shape);
        }
        using(var shape=Skin.Round(button,19))
        using(var fill=new LinearGradientBrush(button,from,to,LinearGradientMode.Horizontal))g.FillPath(fill,shape);
        using(var shine=new Pen(Color.FromArgb(70,255,255,255)))g.DrawLine(shine,button.X+16,button.Y+1,button.Right-16,button.Y+1);
        using(var buttonFont=new Font("Segoe UI",9.5f,FontStyle.Bold))
            LunarText.DrawText(g,"Update now",buttonFont,button,Color.White,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        var close=CloseBounds;
        using(var pen=new Pen(hoverClose?Palette.Text:Color.FromArgb(120,Palette.Muted),1.5f){StartCap=LineCap.Round,EndCap=LineCap.Round}){
            g.DrawLine(pen,close.X+4,close.Y+4,close.Right-4,close.Bottom-4);
            g.DrawLine(pen,close.Right-4,close.Y+4,close.X+4,close.Bottom-4);
        }
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        bool b=ButtonBounds.Contains(e.Location),c=CloseBounds.Contains(e.Location);
        if(b!=hoverButton||c!=hoverClose){hoverButton=b;hoverClose=c;Cursor=b||c?Cursors.Hand:Cursors.Default;Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverButton=hoverClose=pressed=false;Cursor=Cursors.Default;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&hoverButton){pressed=true;Invalidate();}}
    protected override void OnMouseUp(MouseEventArgs e){
        base.OnMouseUp(e);
        bool click=pressed&&ButtonBounds.Contains(e.Location);
        pressed=false;Invalidate();
        if(e.Button!=MouseButtons.Left)return;
        if(click&&UpdateClicked!=null)UpdateClicked();
        else if(CloseBounds.Contains(e.Location))Visible=false;
    }
    protected override void Dispose(bool disposing){if(disposing){animation.Dispose();Font.Dispose();}base.Dispose(disposing);}
}

sealed class FullscreenNoticeCard:Control {
    static readonly string OffPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","fullscreen-notice.txt");
    static readonly Color Amber=Color.FromArgb(245,190,95),AmberDeep=Color.FromArgb(214,140,52);
    int hover=-1;bool pressed;
    public FullscreenNoticeCard(){
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        Size=new Size(412,152);BackColor=Palette.Bg;Visible=false;TabStop=false;
    }
    public static bool Disabled{get{try{return File.Exists(OffPath);}catch(Exception){return false;}}}
    static void Disable(){try{Directory.CreateDirectory(Path.GetDirectoryName(OffPath));File.WriteAllText(OffPath,"off");}catch(Exception){}}
    Rectangle Primary{get{return new Rectangle(Width-128,Height-46,110,32);}}
    Rectangle Secondary{get{return new Rectangle(Width-128-8-150,Height-46,150,32);}}
    public void Present(Size client,int bottomOffset){
        Left=Math.Max(12,client.Width-Width-18);
        Top=Math.Max(12,client.Height-Height-18-bottomOffset);
        Visible=true;BringToFront();Invalidate();
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        if(Width>40&&Height>40)using(var shape=Skin.Round(new Rectangle(0,0,Width,Height),18))Region=new Region(shape);
    }
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        var card=new Rectangle(0,0,Width-1,Height-1);
        using(var shape=Skin.Round(card,18)){
            using(var fill=new LinearGradientBrush(card,Color.FromArgb(58,44,30),Palette.Panel,LinearGradientMode.Horizontal))g.FillPath(fill,shape);
            using(var edge=new LinearGradientBrush(card,Color.FromArgb(200,Amber),Color.FromArgb(60,Amber),LinearGradientMode.Horizontal))
            using(var pen=new Pen(edge,1.2f))g.DrawPath(pen,shape);
        }
        var tile=new Rectangle(18,18,42,42);
        using(var shape=Skin.Round(tile,12))using(var fill=new LinearGradientBrush(tile,Amber,AmberDeep,LinearGradientMode.ForwardDiagonal))g.FillPath(fill,shape);
        using(var pen=new Pen(Color.FromArgb(40,26,10),2.4f){StartCap=LineCap.Round,EndCap=LineCap.Round}){
            float cx=tile.X+21;g.DrawLine(pen,cx,tile.Y+11,cx,tile.Y+25);
            using(var dot=new SolidBrush(Color.FromArgb(40,26,10)))g.FillEllipse(dot,cx-1.8f,tile.Y+29.5f,3.6f,3.6f);
        }
        int x=tile.Right+14,width=Width-x-18;
        using(var title=new Font("Segoe UI",10.5f,FontStyle.Bold))
            LunarText.DrawText(g,"The overlay did not appear",title,new Rectangle(x,14,width,22),Palette.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        using(var body=new Font("Segoe UI",8.5f))
            LunarText.DrawText(g,"VALORANT is in exclusive Fullscreen. In VALORANT → Settings → Video, set Display Mode to Windowed Fullscreen.",body,
                new Rectangle(x,38,width,54),Palette.Muted,TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        Button(g,Secondary,"Don't show again",false,hover==1);
        Button(g,Primary,"Got it",true,hover==0);
    }
    void Button(Graphics g,Rectangle r,string text,bool primary,bool hot){
        if(pressed&&hot)r.Offset(0,1);
        using(var shape=Skin.Round(r,16)){
            if(primary)using(var fill=new LinearGradientBrush(r,hot?Color.FromArgb(255,206,120):Amber,hot?Color.FromArgb(230,160,70):AmberDeep,LinearGradientMode.Horizontal))g.FillPath(fill,shape);
            else using(var fill=new SolidBrush(hot?Color.FromArgb(60,255,255,255):Color.FromArgb(28,255,255,255)))g.FillPath(fill,shape);
        }
        using(var font=new Font("Segoe UI",9,FontStyle.Bold))
            LunarText.DrawText(g,text,font,r,primary?Color.FromArgb(40,26,10):Palette.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        int h=Primary.Contains(e.Location)?0:Secondary.Contains(e.Location)?1:-1;
        if(h!=hover){hover=h;Cursor=h>=0?Cursors.Hand:Cursors.Default;Invalidate();}
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;pressed=false;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(hover>=0){pressed=true;Invalidate();}}
    protected override void OnMouseUp(MouseEventArgs e){
        base.OnMouseUp(e);pressed=false;
        if(Primary.Contains(e.Location))Visible=false;
        else if(Secondary.Contains(e.Location)){Disable();Visible=false;}
        Invalidate();
    }
}
}
