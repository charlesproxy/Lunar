using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Lunar {
sealed class CrosshairPreset {
    public string Name,Style,Code,Source;
    public CrosshairShape Shape;
}

sealed class CrosshairShape {
    static readonly Color[] Colors={Color.White,Color.Lime,Color.GreenYellow,Color.YellowGreen,Color.Yellow,
        Color.Cyan,Color.Magenta,Color.Red};
    readonly List<KeyValuePair<RectangleF,Color>> marks=new List<KeyValuePair<RectangleF,Color>>();
    Color color;
    float border,outlineOpacity;
    public string ColorLabel{get;private set;}

    public static CrosshairShape Parse(string code){
        string value=(code??"").Trim();
        if(value.Length>4096)return null;
        string[] parts=value.Split(';');
        if(parts.Length<2||parts[0]!="0")return null;
        var fields=new Dictionary<string,string>(StringComparer.Ordinal);
        string section="";
        bool primary=false;
        for(int i=1;i<parts.Length;){
            string part=parts[i++];
            if(part=="P"||part=="A"||part=="S"){
                section=part;
                if(part=="P")primary=true;
                continue;
            }
            if(!Regex.IsMatch(part,@"^[a-z0-9]{1,4}$")||i>=parts.Length)return null;
            string field=parts[i++];
            if(!Regex.IsMatch(field,@"^[A-Fa-f0-9.+#-]{1,24}$")||!field.Any(Char.IsDigit)&&part!="u")return null;
            if(section=="P")fields[part]=field;
        }
        if(!primary)return null;
        int selected=(int)Number(fields,"c",0,0,8);
        var result=new CrosshairShape{
            color=selected<Colors.Length?Colors[selected]:ParseHex(fields),
            ColorLabel=new[]{"White","Green","Lime","Lime","Yellow","Cyan","Pink","Red","Custom"}[selected],
            border=Number(fields,"h",1,0,1)>0?Number(fields,"t",1,0,6):0,
            outlineOpacity=Number(fields,"o",.5f,0,1)
        };
        result.AddLines(fields,"0",6,3,.8f);
        result.AddLines(fields,"1",2,10,.35f);
        if(Number(fields,"d",0,0,1)>0){
            float size=Number(fields,"z",2,0,6);
            result.Add(new RectangleF(-size/2,-size/2,size,size),Number(fields,"a",1,0,1));
        }
        return result;
    }
    static float Number(Dictionary<string,string> fields,string key,float fallback,float min,float max){
        string text;
        float value;
        return fields.TryGetValue(key,out text)&&Single.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)
            &&!Single.IsNaN(value)&&!Single.IsInfinity(value)?Math.Max(min,Math.Min(max,value)):fallback;
    }
    static Color ParseHex(Dictionary<string,string> fields){
        string value;
        uint hex;
        if(!fields.TryGetValue("u",out value))return Color.White;
        value=value.TrimStart('#');
        if((value.Length!=6&&value.Length!=8)||!UInt32.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out hex))
            return Color.White;
        if(value.Length==8)hex>>=8;
        return Color.FromArgb((int)(hex>>16),(int)((hex>>8)&255),(int)(hex&255));
    }
    void AddLines(Dictionary<string,string> fields,string prefix,float baseLength,float baseGap,float baseOpacity){
        if(Number(fields,prefix+"b",1,0,1)==0)return;
        float length=Number(fields,prefix+"l",baseLength,0,20);
        float vertical=Number(fields,prefix+"g",0,0,1)>0?Number(fields,prefix+"v",length,0,20):length;
        float gap=Number(fields,prefix+"o",baseGap,0,40);
        float thickness=Number(fields,prefix+"t",2,0,10);
        float opacity=Number(fields,prefix+"a",baseOpacity,0,1);
        Add(new RectangleF(-gap-length,-thickness/2,length,thickness),opacity);
        Add(new RectangleF(gap,-thickness/2,length,thickness),opacity);
        Add(new RectangleF(-thickness/2,-gap-vertical,thickness,vertical),opacity);
        Add(new RectangleF(-thickness/2,gap,thickness,vertical),opacity);
    }
    void Add(RectangleF rect,float opacity){
        if(rect.Width>0&&rect.Height>0&&opacity>0)
            marks.Add(new KeyValuePair<RectangleF,Color>(rect,Color.FromArgb((int)Math.Round(255*opacity),color)));
    }
    public void Draw(Graphics g,Rectangle area,float zoom){
        if(area.Width<1||area.Height<1||marks.Count==0)return;
        var state=g.Save();
        try{
            g.SetClip(area,CombineMode.Intersect);
            g.TranslateTransform(area.Left+area.Width/2,area.Top+area.Height/2);
            g.ScaleTransform(zoom,zoom);
            g.SmoothingMode=SmoothingMode.None;
            if(border>0&&outlineOpacity>0){
                using(var outline=new SolidBrush(Color.FromArgb((int)Math.Round(outlineOpacity*255),Color.Black)))
                    foreach(var mark in marks)g.FillRectangle(outline,RectangleF.Inflate(mark.Key,border,border));
            }
            foreach(var mark in marks)
                using(var line=new SolidBrush(mark.Value))g.FillRectangle(line,mark.Key);
        }finally{g.Restore(state);}
    }
}

sealed class CrosshairView:UserControl {
    internal static readonly Color Surface=Color.FromArgb(23,18,34),Stroke=Color.FromArgb(57,44,77);
    readonly CrosshairPreset[] catalog=ReadCatalog();
    readonly TextBox search=new RiotIdInput("Search by name, style or color");
    readonly SearchField searchField=new SearchField();
    readonly SoftSelect category=new SoftSelect();
    readonly CrosshairGallery gallery=new CrosshairGallery();
    readonly SoftButton copy=new SoftButton{SurfaceColor=Surface,Text="Copy code",TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(94,61,149),
        ForeColor=Palette.Text,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand};
    readonly SoftButton browse=ActionButton("Browse",Color.FromArgb(51,37,73));
    readonly SoftButton mine=ActionButton("My Crosshairs",Color.FromArgb(32,24,46));
    readonly SoftButton add=ActionButton("+ Add crosshair",Color.FromArgb(54,36,80));
    readonly SoftButton save=ActionButton("Save",Color.FromArgb(43,32,59));
    readonly SavedCrosshairs library;
    CrosshairPreset[] saved=new CrosshairPreset[0];
    bool showSaved;
    string feedback="";
    readonly TextBox code=new TextBox{ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Surface,ForeColor=Palette.Muted,
        Font=new Font("Consolas",9),AccessibleName="VALORANT crosshair code"};
    readonly Timer restoreCopy=new Timer{Interval=1800};
    Rectangle detail;
    int count;
    CrosshairPreset selected;

    public CrosshairView():this(new SavedCrosshairs()){}
    internal CrosshairView(SavedCrosshairs library){
        this.library=library;
        try{saved=RefreshSavedCategories(library.Read());}
        catch(IOException){feedback="Couldn’t read saved crosshairs.";}
        catch(UnauthorizedAccessException){feedback="Couldn’t read saved crosshairs.";}
        catch(ArgumentException){feedback="Couldn’t read saved crosshairs.";}
        catch(InvalidOperationException){feedback="Couldn’t read saved crosshairs.";}
        DoubleBuffered=true;
        ResizeRedraw=true;
        BackColor=Color.FromArgb(13,10,20);
        search.BorderStyle=BorderStyle.None;
        search.BackColor=SearchField.Fill;
        search.ForeColor=Palette.Text;
        search.Font=new Font("Segoe UI",10);
        search.AccessibleName="Search crosshairs";
        search.MaxLength=80;
        searchField.Controls.Add(search);
        searchField.Click+=delegate{search.Focus();};
        category.AccessibleName="Filter by crosshair category";
        category.Items.Add("All categories");
        foreach(var style in catalog.Concat(saved).Select(item=>item.Style).Concat(new[]{"Custom"}).Distinct())
            category.Items.Add(style);
        category.SelectedIndex=0;
        browse.Click+=delegate{ShowLibrary(false);};
        mine.Click+=delegate{ShowLibrary(true);};
        add.Click+=delegate{
            using(var dialog=new CrosshairEditor())if(dialog.ShowDialog(FindForm())==DialogResult.OK){
                if(SavePreset(dialog.Preset,true)){
                    ShowLibrary(true,dialog.Preset.Code);
                }
            }
        };
        save.Click+=delegate{
            if(selected!=null&&SavePreset(selected,!IsSaved(selected)))Filter();
        };
        copy.FlatAppearance.BorderSize=0;
        copy.Font=new Font("Segoe UI",10,FontStyle.Bold);
        copy.Click+=delegate{CopyCode();};
        restoreCopy.Tick+=delegate{restoreCopy.Stop();copy.Text="Copy code";};
        gallery.SelectionChanged+=SelectPreset;
        search.TextChanged+=delegate{Filter();};
        category.SelectedIndexChanged+=delegate{Filter();};
        Controls.AddRange(new Control[]{browse,mine,add,searchField,category,gallery,code,copy,save});
        Filter();
    }
    static SoftButton ActionButton(string label,Color fill){
        var button=new SoftButton{Text=label,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter,
            BackColor=fill,ForeColor=Palette.Text,Cursor=Cursors.Hand};
        button.FlatAppearance.BorderSize=0;
        return button;
    }
    void ShowLibrary(bool personal,string preferred=null){
        string code=preferred??(selected==null?null:selected.Code);
        showSaved=personal;
        browse.BackColor=personal?Color.FromArgb(32,24,46):Color.FromArgb(51,37,73);
        mine.BackColor=personal?Color.FromArgb(51,37,73):Color.FromArgb(32,24,46);
        category.SelectedIndex=0;
        search.Clear();
        selected=(personal?saved:catalog).FirstOrDefault(item=>item.Code==code);
        Filter();
    }
    bool IsSaved(CrosshairPreset preset){return saved.Any(item=>item.Code==preset.Code);}
    bool SavePreset(CrosshairPreset preset,bool keep){
        try{
            saved=RefreshSavedCategories(library.Update(preset,keep));
            feedback="";
            return true;
        }catch(IOException){feedback="Couldn’t update saved crosshairs.";}
         catch(UnauthorizedAccessException){feedback="Couldn’t update saved crosshairs.";}
         catch(ArgumentException){feedback="Couldn’t update saved crosshairs.";}
         catch(InvalidOperationException){feedback="Couldn’t update saved crosshairs.";}
        Invalidate();
        return false;
    }
    internal static CrosshairPreset ReadPreset(object row){
        var preset=new CrosshairPreset{Name=Json.S(row,"name"),Style=Json.S(row,"style"),Code=Json.S(row,"code"),
            Source=Json.S(row,"source")};
        preset.Shape=CrosshairShape.Parse(preset.Code);
        return preset.Name!=""&&preset.Shape!=null?preset:null;
    }
    internal static CrosshairPreset[] ReadCatalog(){
        using(var stream=typeof(CrosshairView).Assembly.GetManifestResourceStream("crosshairs.json"))
        using(var reader=new StreamReader(stream))
            return Json.A(Json.Codec.DeserializeObject(reader.ReadToEnd())).Select(ReadPreset).Where(item=>item!=null).ToArray();
    }
    CrosshairPreset[] RefreshSavedCategories(CrosshairPreset[] items){
        foreach(var item in items){
            if(item.Source=="")continue;
            var entry=catalog.FirstOrDefault(preset=>preset.Code==item.Code&&preset.Source==item.Source);
            if(entry!=null)item.Style=entry.Style;
        }
        return items;
    }
    void Filter(){
        string query=search.Text.Trim(),style=Convert.ToString(category.SelectedItem);
        var visible=(showSaved?saved:catalog).Where(item=>(style=="All categories"||item.Style==style)&&
            (Matches(item.Name,query)||Matches(item.Style,query)||Matches(item.Shape.ColorLabel,query))).ToArray();
        count=visible.Length;
        var preserve=selected==null?null:visible.FirstOrDefault(item=>item.Code==selected.Code);
        gallery.SetItems(visible,preserve);
        Invalidate();
    }
    static bool Matches(string value,string query){return value.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0;}
    void SelectPreset(CrosshairPreset preset){
        selected=preset;
        code.Text=preset==null?"":preset.Code;
        if(preset!=null)code.Select(0,0);
        code.Visible=copy.Visible=save.Visible=preset!=null;
        save.Text=preset!=null&&IsSaved(preset)?"Remove":"Save";
        restoreCopy.Stop();
        copy.Text="Copy code";
        Invalidate();
    }
    void CopyCode(){
        if(selected==null)return;
        try{
            Clipboard.SetText(selected.Code);
            copy.Text="Copied";
        }catch(ExternalException){copy.Text="Try again";}
        restoreCopy.Stop();
        restoreCopy.Start();
    }
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);
        if(Width<630||Height<385)return;
        int right=Math.Min(405,Math.Max(270,(Width-66)*40/100));
        int left=Width-66-right;
        int start=23;
        searchField.SetBounds(start,118,left-163,39);
        search.SetBounds(42,(searchField.Height-search.PreferredHeight)/2,searchField.Width-47,search.PreferredHeight);
        category.SetBounds(start+left-151,118,151,39);
        browse.SetBounds(start,73,96,32);
        mine.SetBounds(start+104,73,137,32);
        add.SetBounds(Width-177,26,154,37);
        gallery.SetBounds(start,192,left,Height-214);
        detail=new Rectangle(start+left+20,118,right,Math.Min(500,Height-140));
        int bottom=detail.Bottom;
        copy.SetBounds(detail.X+16,bottom-63,detail.Width-129,39);
        save.SurfaceColor=Surface;
        save.SetBounds(detail.Right-104,bottom-63,88,39);
        code.SetBounds(detail.X+24,bottom-109,detail.Width-48,22);
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(detail.Width<=0)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        ProfileCanvas.T(g,"Crosshair",24,Palette.Text,new Rectangle(23,20,Width-46,51),true);
        ProfileCanvas.T(g,count+(count==1?" preset":" presets"),9,Palette.Muted,new Rectangle(23,163,gallery.Width,25));
        if(feedback!="")ProfileCanvas.T(g,feedback,9,Palette.Red,new Rectangle(detail.X,77,detail.Width,30));
        Card(g,detail,Surface,Stroke,13);
        if(selected==null){
            string title=showSaved&&saved.Length==0?"No saved crosshairs yet.":"No presets match this search.";
            ProfileCanvas.T(g,title,11,Palette.Text,new Rectangle(detail.X+18,detail.Y+26,detail.Width-36,35),true);
            if(showSaved&&saved.Length==0)ProfileCanvas.T(g,"Save a preset or add your own code.",9,Palette.Muted,
                new Rectangle(detail.X+18,detail.Y+61,detail.Width-36,31));
            return;
        }
        int sceneHeight=detail.Height-202;
        var scene=new Rectangle(detail.X+13,detail.Y+13,detail.Width-26,sceneHeight);
        DrawPreview(g,scene,selected.Shape);
        ProfileCanvas.T(g,"Primary · actual size",8,Palette.Text,new Rectangle(scene.X+9,scene.Bottom-25,scene.Width-18,22),true);
        ProfileCanvas.T(g,selected.Name,15,Palette.Text,new Rectangle(detail.X+17,scene.Bottom+8,detail.Width-34,35),true);
        string label=selected.Style+"  ·  "+selected.Shape.ColorLabel;
        if(selected.Source.StartsWith("https://www.vcrdb.net/",StringComparison.Ordinal))label+="  ·  VCRDB";
        if(selected.Source.StartsWith("https://prosettings.net/",StringComparison.Ordinal))label+="  ·  ProSettings";
        ProfileCanvas.T(g,label,9,Palette.Muted,new Rectangle(detail.X+17,scene.Bottom+43,detail.Width-34,25));
        ProfileCanvas.T(g,"VALORANT → Settings → Crosshair",8,Palette.Muted,new Rectangle(detail.X+16,detail.Bottom-22,detail.Width-32,19));
    }
    internal static void DrawPreview(Graphics g,Rectangle bounds,CrosshairShape shape){
        using(var panel=Skin.Round(bounds,9))
        using(var wall=new LinearGradientBrush(bounds,Color.FromArgb(124,101,82),Color.FromArgb(79,63,53),20f)){
            g.FillPath(wall,panel);
        }
        if(shape!=null)shape.Draw(g,bounds,1f);
    }
    internal static void Card(Graphics g,Rectangle bounds,Color fill,Color stroke,int radius){
        using(var path=Skin.Round(new Rectangle(bounds.X,bounds.Y,bounds.Width-1,bounds.Height-1),radius))
        using(var ink=new SolidBrush(fill))
        using(var edge=new Pen(stroke)){
            g.FillPath(ink,path);
            g.DrawPath(edge,path);
        }
    }
    protected override void Dispose(bool disposing){
        if(disposing){
            restoreCopy.Dispose();
        }
        base.Dispose(disposing);
    }
}

sealed class CrosshairGallery:Control {
    const int Gap=11,RowHeight=111;
    CrosshairPreset[] items=new CrosshairPreset[0];
    int selected=-1,hovered=-1,scroll;
    int Columns{get{return Math.Max(2,Width/158);}}
    int ItemWidth{get{return (Width-(Columns-1)*Gap-5)/Columns;}}
    int ScrollMax{get{return Math.Max(0,((items.Length+Columns-1)/Columns)*(RowHeight+Gap)-Gap-Height);}}
    public event Action<CrosshairPreset> SelectionChanged;
    public CrosshairGallery(){
        DoubleBuffered=true;
        ResizeRedraw=true;
        TabStop=true;
        AccessibleName="Crosshair presets";
    }
    public void SetItems(CrosshairPreset[] value,CrosshairPreset preserve){
        items=value;
        hovered=-1;
        scroll=0;
        var index=Array.IndexOf(value,preserve);
        Select(Math.Max(0,index));
    }
    void Select(int index){
        selected=items.Length==0?-1:Math.Max(0,Math.Min(index,items.Length-1));
        if(selected>=0&&Height>0){
            int y=selected/Columns*(RowHeight+Gap);
            ScrollTo(Math.Max(y+RowHeight-Height,Math.Min(scroll,y)));
        }else Invalidate();
        if(SelectionChanged!=null)SelectionChanged(selected<0?null:items[selected]);
    }
    void ScrollTo(int y){scroll=Math.Max(0,Math.Min(y,ScrollMax));Invalidate();}
    Rectangle BoundsFor(int i){return new Rectangle(i%Columns*(ItemWidth+Gap),i/Columns*(RowHeight+Gap)-scroll,ItemWidth,RowHeight);}
    int At(Point p){
        if(ItemWidth<=0||!ClientRectangle.Contains(p))return -1;
        int col=p.X/(ItemWidth+Gap),row=(p.Y+scroll)/(RowHeight+Gap);
        int i=row*Columns+col;
        return col<Columns&&i<items.Length&&BoundsFor(i).Contains(p)?i:-1;
    }
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ScrollTo(scroll);}
    protected override void OnMouseMove(MouseEventArgs e){
        int next=At(e.Location);
        if(next!=hovered){hovered=next;Cursor=next>=0?Cursors.Hand:Cursors.Default;Invalidate();}
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e){hovered=-1;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseWheel(MouseEventArgs e){hovered=-1;ScrollTo(scroll-e.Delta*72/120);base.OnMouseWheel(e);}
    protected override void OnMouseClick(MouseEventArgs e){
        int index=At(e.Location);
        if(e.Button==MouseButtons.Left&&index>=0){Focus();Select(index);}
        base.OnMouseClick(e);
    }
    protected override bool IsInputKey(Keys key){
        return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(key);
    }
    protected override void OnKeyDown(KeyEventArgs e){
        switch(e.KeyCode){
            case Keys.Left:Select(selected-1);break;
            case Keys.Right:Select(selected+1);break;
            case Keys.Up:Select(selected-Columns);break;
            case Keys.Down:Select(selected+Columns);break;
            case Keys.Home:Select(0);break;
            case Keys.End:Select(items.Length-1);break;
            case Keys.PageUp:ScrollTo(scroll-Height);break;
            case Keys.PageDown:ScrollTo(scroll+Height);break;
            default:base.OnKeyDown(e);return;
        }
        e.Handled=true;
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(ItemWidth<36||Height<1)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        int first=scroll/(RowHeight+Gap)*Columns;
        for(int i=first;i<items.Length;i++){
            var rect=BoundsFor(i);
            if(rect.Top>=Height)break;
            CrosshairView.Card(g,rect,i==hovered?Color.FromArgb(39,30,56):CrosshairView.Surface,
                i==selected?Palette.Accent:CrosshairView.Stroke,11);
            var sample=new Rectangle(rect.X+8,rect.Y+8,rect.Width-16,57);
            CrosshairView.Card(g,sample,Color.FromArgb(19,16,28),Color.FromArgb(45,35,63),7);
            var item=items[i];
            item.Shape.Draw(g,sample,1f);
            ProfileCanvas.T(g,item.Name,10,Palette.Text,new Rectangle(rect.X+10,rect.Y+67,rect.Width-20,23),true);
            ProfileCanvas.T(g,item.Style,8,Palette.Muted,new Rectangle(rect.X+10,rect.Y+90,rect.Width-20,16));
        }
        if(ScrollMax>0){
            int thumb=Math.Max(18,Height*Height/(Height+ScrollMax));
            int top=(Height-thumb)*scroll/ScrollMax;
            using(var ink=new SolidBrush(Color.FromArgb(90,66,125)))g.FillRectangle(ink,Width-2,top,2,thumb);
        }
    }
}
}
