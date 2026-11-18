using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;
namespace Lunar {
enum LunarGlyph { Home, Live, Search, Shop, Friends, Crosshair, Settings }
static class LunarIcons {
    public static void Draw(Graphics graphics,LunarGlyph icon,Point center,Color color,int size=20){
        var state=graphics.Save();
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
        graphics.TranslateTransform(center.X-size/2f,center.Y-size/2f);
        graphics.ScaleTransform(size/24f,size/24f);
        using(var pen=new Pen(color,1.7f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}){
            switch(icon){
                case LunarGlyph.Home:
                    graphics.DrawLines(pen,new[]{new Point(3,11),new Point(12,3),new Point(21,11)});
                    graphics.DrawLines(pen,new[]{new Point(5,10),new Point(5,20),new Point(10,20),new Point(10,15),new Point(14,15),new Point(14,20),new Point(19,20),new Point(19,10)});
                    break;
                case LunarGlyph.Live:
                    graphics.DrawLines(pen,new[]{new Point(2,12),new Point(6,12),new Point(9,5),new Point(13,19),new Point(16,12),new Point(22,12)});
                    break;
                case LunarGlyph.Search:
                    graphics.DrawEllipse(pen,4,4,12,12);
                    graphics.DrawLine(pen,14.5f,14.5f,20,20);
                    break;
                case LunarGlyph.Shop:
                    graphics.DrawLines(pen,new[]{new Point(2,4),new Point(5,4),new Point(7,15),new Point(18,15),new Point(21,7),new Point(6,7)});
                    graphics.DrawEllipse(pen,7,18,2,2);
                    graphics.DrawEllipse(pen,16,18,2,2);
                    break;
                case LunarGlyph.Settings:{
                    // Gear: 8 rounded teeth around a ring, hollow hub.
                    const int teeth=8;
                    var outline=new List<PointF>();
                    for(int i=0;i<teeth;i++){
                        double a=i*2*Math.PI/teeth;
                        foreach(var step in new[]{new[]{-0.36,7.4},new[]{-0.2,10.2},new[]{0.2,10.2},new[]{0.36,7.4}}){
                            double angle=a+step[0];
                            outline.Add(new PointF(12f+(float)(Math.Cos(angle)*step[1]),12f+(float)(Math.Sin(angle)*step[1])));
                        }
                    }
                    graphics.DrawPolygon(pen,outline.ToArray());
                    graphics.DrawEllipse(pen,8.6f,8.6f,6.8f,6.8f);
                    break;}
                case LunarGlyph.Crosshair:
                    graphics.DrawEllipse(pen,5,5,14,14);
                    graphics.DrawLine(pen,12,2,12,8);
                    graphics.DrawLine(pen,12,16,12,22);
                    graphics.DrawLine(pen,2,12,8,12);
                    graphics.DrawLine(pen,16,12,22,12);
                    break;
                case LunarGlyph.Friends:
                    graphics.DrawEllipse(pen,5,3,7,7);
                    graphics.DrawArc(pen,2,12,13,12,180,180);
                    graphics.DrawArc(pen,13,4,6,6,-95,190);
                    graphics.DrawArc(pen,13,12,9,12,-90,90);
                    break;
            }
        }
        graphics.Restore(state);
    }
}
sealed class NavButton:Button {
    readonly LunarGlyph glyph;
    bool hover,active;
    public bool Active {
        get{return active;}
        set{if(active!=value){active=value;Invalidate();}}
    }
    public NavButton(string title,LunarGlyph icon){
        Text=title;
        glyph=icon;
        FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;
        Cursor=Cursors.Hand;
        Font=new Font("Segoe UI",10,FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;
        g.Clear(Parent.BackColor);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Skin.Round(new Rectangle(1,1,Width-3,Height-3),9)){
            if(active||hover){
                using(var fill=new SolidBrush(active?Palette.AccentSoft:Palette.Raised))g.FillPath(fill,path);
            }
            if(Focused&&ShowFocusCues){using(var border=new Pen(Palette.Accent))g.DrawPath(border,path);}
        }
        if(active){
            using(var pen=new Pen(Palette.Accent,2.5f){StartCap=LineCap.Round,EndCap=LineCap.Round})
                g.DrawLine(pen,5,Height/2-7,5,Height/2+7);
        }
        LunarIcons.Draw(g,glyph,new Point(24,Height/2),active?Palette.Accent:Palette.Muted);
        LunarText.DrawText(g,Text,Font,new Rectangle(42,0,Width-46,Height),active?Color.White:Palette.Text,
            TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
}
class SoftButton:Button {
 public Color? SurfaceColor{get;set;}
 bool hover;
 public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(SurfaceColor??(Parent==null?Palette.Bg:Parent.BackColor));using(var p=Skin.Round(new Rectangle(0,0,Width-1,Height-1),10))using(var b=new SolidBrush(hover?ControlPaint.Light(BackColor,.15f):BackColor)){g.FillPath(b,p);if(Focused)using(var pen=new Pen(Palette.Accent))g.DrawPath(pen,p);}var r=new Rectangle(12,0,Width-24,Height);var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;if(TextAlign==ContentAlignment.MiddleCenter)flags|=TextFormatFlags.HorizontalCenter;LunarText.DrawText(g,Text,Font,r,Enabled?ForeColor:Palette.Muted,flags);}
}
class SoftSelect:Control {
 public readonly List<object> Items=new List<object>();int selected=-1;bool hover;SelectMenu menu;
 public event EventHandler SelectedIndexChanged;
 public int SelectedIndex{get{return selected;}set{int next=value>=0&&value<Items.Count?value:-1;bool changed=selected!=next;selected=next;Invalidate();if(changed&&SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
 public object SelectedItem{get{return selected>=0&&selected<Items.Count?Items[selected]:null;}set{SelectedIndex=Items.IndexOf(value);}}
 public Color? SurfaceColor{get;set;}
 public SoftSelect(){DoubleBuffered=true;Height=36;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ComboBox;BackColor=Palette.Panel;ForeColor=Palette.Text;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
 protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(SurfaceColor??(Parent==null?Palette.Bg:Parent.BackColor));using(var path=Skin.Round(new Rectangle(0,0,Width-1,Height-1),10))using(var b=new SolidBrush(hover?Color.FromArgb(29,26,35):Palette.Panel))using(var pen=new Pen(Focused?Palette.Accent:Palette.Border)){g.FillPath(b,path);g.DrawPath(pen,path);}LunarText.DrawText(g,Convert.ToString(SelectedItem),Font,new Rectangle(12,0,Width-38,Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);using(var pen=new Pen(Palette.Muted,1.5f))g.DrawLines(pen,new[]{new Point(Width-23,Height/2-2),new Point(Width-18,Height/2+3),new Point(Width-13,Height/2-2)});}
 protected override void OnClick(EventArgs e){base.OnClick(e);Focus();OpenMenu();}
 protected override bool IsInputKey(Keys keyData){return keyData==Keys.Up||keyData==Keys.Down||base.IsInputKey(keyData);}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space||e.KeyCode==Keys.F4){OpenMenu();e.Handled=true;}else if(e.KeyCode==Keys.Down||e.KeyCode==Keys.Up){SelectedIndex=Math.Max(0,Math.Min(Items.Count-1,selected+(e.KeyCode==Keys.Down?1:-1)));e.Handled=true;}}
 void OpenMenu(){
    if(!Enabled||Items.Count==0)return;
    if(menu!=null){menu.Close();menu.Dispose();}
    Rectangle anchor=RectangleToScreen(ClientRectangle);
    Rectangle work=Screen.FromRectangle(anchor).WorkingArea;
    int rowHeight=Math.Max(26,Font.Height+13);
    int below=work.Bottom-anchor.Bottom-6,above=anchor.Top-work.Top-6;
    int idealHeight=Math.Min(8,Items.Count)*rowHeight+12;
    bool openAbove=below<idealHeight&&above>below;
    int available=openAbove?above:below;
    int visibleRows=Math.Max(1,Math.Min(Math.Min(8,Items.Count),(available-12)/rowHeight));
    int width=Math.Min(work.Width,Math.Max(Width,242));
    menu=new SelectMenu(Items.ToArray(),SelectedIndex,width,rowHeight,visibleRows,Font,Choose);
    int x=Math.Max(work.Left,Math.Min(anchor.Left,work.Right-width));
    int y=openAbove?anchor.Top-menu.Height-6:anchor.Bottom+6;
    menu.Show(new Point(x,Math.Max(work.Top,y)));
    menu.FocusList();
 }
 void Choose(object option){
    int index=Items.IndexOf(option);
    var choice=option as Choice;
    if(index<0&&choice!=null)index=Items.FindIndex(item=>item is Choice&&((Choice)item).Value==choice.Value);
    if(index>=0)SelectedIndex=index;
    Focus();
 }
 protected override void Dispose(bool disposing){if(disposing&&menu!=null)menu.Dispose();base.Dispose(disposing);}
}
sealed class SelectMenu:ToolStripDropDown {
    readonly OptionList options;
    readonly Action<object> choose;
    public SelectMenu(object[] items,int selected,int width,int rowHeight,int rows,Font font,Action<object> choose){
        this.choose=choose;
        AutoSize=false;
        Padding=new Padding(6);
        DropShadowEnabled=false;
        Renderer=new SelectPopupRenderer();
        BackColor=Palette.Panel;
        var listSize=new Size(width-12,rows*rowHeight);
        options=new OptionList(items,selected,rowHeight,rows){
            BackColor=BackColor,ForeColor=Palette.Text,Font=font,Size=listSize,Cursor=Cursors.Hand,
            AccessibleName="Filter options"
        };
        options.Chosen+=Commit;
        Items.Add(new ToolStripControlHost(options){AutoSize=false,Size=listSize,Margin=Padding.Empty,Padding=Padding.Empty});
        Size=new Size(width,listSize.Height+12);
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        if(Width<22||Height<22)return;
        using(var shape=Skin.Round(new Rectangle(Point.Empty,Size),10))Region=new Region(shape);
    }
    sealed class SelectPopupRenderer:ToolStripProfessionalRenderer {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(e.ToolStrip.BackColor);}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){
            if(e.ToolStrip.Width<22||e.ToolStrip.Height<22)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=Skin.Round(new Rectangle(0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1),10))
            using(var border=new Pen(Palette.Border))e.Graphics.DrawPath(border,shape);
        }
    }
    public void FocusList(){options.Focus();}
    protected override bool ProcessCmdKey(ref Message message,Keys keyData){
        if(keyData==Keys.Escape){Close();return true;}
        if(keyData==Keys.Enter||keyData==Keys.Space){options.Confirm();return true;}
        return base.ProcessCmdKey(ref message,keyData);
    }
    void Commit(object item){
        Close(ToolStripDropDownCloseReason.ItemClicked);
        choose(item);
    }
    sealed class OptionList:Control {
        readonly object[] items;
        readonly int applied,rowHeight,rows;
        int first,cursor,hover=-1,wheel;
        public event Action<object> Chosen;
        public OptionList(object[] items,int applied,int rowHeight,int rows){
            this.items=items;this.applied=applied;this.rowHeight=rowHeight;this.rows=rows;
            cursor=Math.Max(0,applied);
            first=Math.Max(0,Math.Min(cursor-rows/2,items.Length-rows));
            DoubleBuffered=true;
            TabStop=true;
            AccessibleRole=AccessibleRole.List;
        }
        public void Confirm(){if(Chosen!=null)Chosen(items[cursor]);}
        protected override bool IsInputKey(Keys key){
            switch(key){case Keys.Up:case Keys.Down:case Keys.Home:case Keys.End:case Keys.PageUp:case Keys.PageDown:return true;}
            return base.IsInputKey(key);
        }
        protected override void OnKeyDown(KeyEventArgs e){
            int target;
            switch(e.KeyCode){
                case Keys.Up:target=cursor-1;break;
                case Keys.Down:target=cursor+1;break;
                case Keys.Home:target=0;break;
                case Keys.End:target=items.Length-1;break;
                case Keys.PageUp:target=cursor-rows;break;
                case Keys.PageDown:target=cursor+rows;break;
                case Keys.Space:case Keys.Enter:Confirm();e.Handled=true;return;
                default:base.OnKeyDown(e);return;
            }
            cursor=Math.Max(0,Math.Min(items.Length-1,target));
            first=Math.Max(cursor-rows+1,Math.Min(first,cursor));
            hover=-1;e.Handled=true;Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e){
            wheel+=e.Delta;
            int steps=wheel/SystemInformation.MouseWheelScrollDelta;
            wheel%=SystemInformation.MouseWheelScrollDelta;
            int lines=SystemInformation.MouseWheelScrollLines;
            first=Math.Max(0,Math.Min(items.Length-rows,first-steps*(lines<0?rows:lines)));
            cursor=Math.Max(first,Math.Min(first+rows-1,cursor));
            hover=first+Math.Min(rows-1,Math.Max(0,e.Y/rowHeight));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e){
            int next=first+Math.Min(rows-1,Math.Max(0,e.Y/rowHeight));
            if(next!=hover){hover=next;Invalidate();}
        }
        protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseClick(MouseEventArgs e){
            base.OnMouseClick(e);
            if(e.Button==MouseButtons.Left&&ClientRectangle.Contains(e.Location)){
                cursor=first+Math.Min(rows-1,e.Y/rowHeight);
                Confirm();
            }
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            const TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
            for(int row=0;row<rows;row++){
                int index=first+row,y=row*rowHeight;
                bool active=index==(hover<0?cursor:hover);
                if(active||index==applied){
                    using(var path=Skin.Round(new Rectangle(1,y+1,Width-8,rowHeight-2),6))
                    using(var brush=new SolidBrush(active?Palette.AccentSoft:Palette.Raised))
                        g.FillPath(brush,path);
                }
                LunarText.DrawText(g,Convert.ToString(items[index]),Font,new Rectangle(11,y,Width-52,rowHeight),index==applied?Palette.Accent:Palette.Text,flags);
                if(index==applied)LunarText.DrawText(g,"✓",Font,new Rectangle(Width-28,y,19,rowHeight),Palette.Accent,flags);
            }
            if(items.Length>rows){
                int thumb=Math.Max(15,(Height-4)*rows/items.Length);
                int y=2+(Height-4-thumb)*first/(items.Length-rows);
                using(var brush=new SolidBrush(Color.FromArgb(92,63,126)))g.FillRectangle(brush,Width-4,y,2,thumb);
            }
        }
    }
}
static class SearchChecks {
 public static void Run(){string id="11111111-1111-1111-1111-111111111111";
 var valid=Json.Codec.DeserializeObject("[{\"puuid\":\""+id+"\",\"game_name\":\"Lunar\",\"tag_line\":\"BR1\"}]");
 if(RiotReader.ParseLookup(valid,"lunar","br1")==null)throw new Exception("Lookup case matching");
 if(RiotReader.ParseLookup(valid,"Other","BR1")!=null)throw new Exception("Lookup wrong name");
 if(RiotReader.ParseLookup(valid,"Lunar","BR2")!=null)throw new Exception("Lookup wrong tag");
 if(RiotReader.ParseLookup(Json.Codec.DeserializeObject("[{\"puuid\":\"bad\"}]"),"Lunar","BR1")!=null)throw new Exception("Lookup malformed UUID");
 if(RiotReader.ParseLookup(null,"Lunar","BR1")!=null)throw new Exception("Lookup empty");
 if(ApiArt.AgentUrl("add6443a-41bd-e414-f6ad-e58d267f4e95")!="https://media.valorant-api.com/agents/add6443a-41bd-e414-f6ad-e58d267f4e95/displayicon.png"||ApiArt.AgentUrl("bad")!="")throw new Exception("Invalid API art URL");
 Console.WriteLine("6 search / embedded rank checks passed.");
 }
}
static class Skin {
 public static GraphicsPath Round(Rectangle r,int radius=12){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 public static void Card(Graphics g,Rectangle r){using(var p=Round(r))using(var b=new SolidBrush(Palette.Panel))using(var pen=new Pen(Palette.Border)){g.FillPath(b,p);g.DrawPath(pen,p);}}
}
partial class RiotReader {
 public static Player ParseLookup(object result,string name,string tag){
  foreach(var item in Json.A(result)){string id=Json.S(item,"puuid");Guid parsed;if(!Guid.TryParse(id,out parsed))continue;string n=Json.S(item,"game_name"),t=Json.S(item,"tag_line");if(n!=""&&!String.Equals(n,name,StringComparison.OrdinalIgnoreCase))continue;if(t!=""&&!String.Equals(t,tag,StringComparison.OrdinalIgnoreCase))continue;return new Player{Id=id,Name=(n==""?name:n)+"#"+(t==""?tag:t)};}return null;
 }
 // Riot Client processes: "RiotClientServices", "RiotClientUx" and, since 2024, "Riot Client" (with a space).
 public static bool ClientProcessAlive(int pid){try{using(var p=System.Diagnostics.Process.GetProcessById(pid))return !p.HasExited&&IsRiotClientName(p.ProcessName);}catch{return false;}}
 internal static bool IsRiotClientName(string name){return (name??"").Replace(" ","").StartsWith("RiotClient",StringComparison.OrdinalIgnoreCase);}
 public static string LookupFailure(System.Net.WebExceptionStatus status){switch(status){case System.Net.WebExceptionStatus.ConnectFailure:return "Riot Client is not responding. Open it and sign in.";case System.Net.WebExceptionStatus.Timeout:return "Riot Client timed out. Wait for it to load and try again.";case System.Net.WebExceptionStatus.TrustFailure:case System.Net.WebExceptionStatus.SecureChannelFailure:return "Could not connect securely to Riot Client. Restart it and try again.";default:return "Riot Client request failed ("+status+"). Check that Riot Client is signed in.";}}
 public static void OpenClient(){string executable="";string manifest=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Riot Games\RiotClientInstalls.json");if(System.IO.File.Exists(manifest)){var data=Json.Codec.DeserializeObject(System.IO.File.ReadAllText(manifest));executable=Json.S(data,"rc_default");if(executable=="")executable=Json.S(data,"rc_live");}if(executable=="")executable=@"C:\Riot Games\Riot Client\RiotClientServices.exe";if(!System.IO.File.Exists(executable)||!String.Equals(System.IO.Path.GetFileName(executable),"RiotClientServices.exe",StringComparison.OrdinalIgnoreCase))throw new Exception("Riot Client installation not found. Open it from the Start menu.");System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=true});}
 public Player Lookup(string input){
  int split=input.LastIndexOf('#');if(split<1||split==input.Length-1)throw new Exception("Enter the full Riot ID: Name#TAG.");
  try{string path=System.IO.Path.Combine(local,@"Riot Games\Riot Client\Config\lockfile");if(!System.IO.File.Exists(path))throw new Exception("Sign in to Riot Client to search for players.");string[] lf=ReadShared(path,4096).Trim().Split(':');int port;if(lf.Length!=5||!Int32.TryParse(lf[2],out port)||port<1||port>65535||lf[4]!="https")throw new Exception("Riot Client session unavailable.");int clientPid;if(!Int32.TryParse(lf[1],out clientPid)||!ClientProcessAlive(clientPid))throw new Exception("Open Riot Client and sign in.");basic=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("riot:"+lf[3]));origin="https://127.0.0.1:"+port;
   string name=input.Substring(0,split).Trim(),tag=input.Substring(split+1).Trim();
   var result=Request(origin+"/player-account/aliases/v1/lookup?gameName="+Uri.EscapeDataString(name)+"&tagLine="+Uri.EscapeDataString(tag),"GET",null,true,false);
   var player=ParseLookup(result,name,tag);if(player!=null)return player;
   throw new Exception("Player not found. Check the name and tag.");
  }catch(System.Net.WebException e){throw new Exception(LookupFailure(e.Status));}catch(System.IO.IOException){throw new Exception("Riot Client is starting or closing. Wait and try again.");}catch(ApiFailure e){throw new Exception(e.Code==401||e.Code==403?"Riot Client did not authorize the search. Sign in again.":e.Code==429?"Too many requests. Try again in a minute.":"Search unavailable in this session (HTTP "+e.Code+").");}
  finally{basic=access=entitlement=null;}
 }
}
sealed class SearchField:Panel {
 public static readonly Color Fill=Palette.Raised;
    public SearchField(){DoubleBuffered=true;ResizeRedraw=true;}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<24||Height<24)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var outline=Skin.Round(new Rectangle(1,1,Width-3,Height-3),11))
        using(var fill=new SolidBrush(Fill))
        using(var border=new Pen(ContainsFocus?Palette.Accent:Palette.Border)){
            g.FillPath(fill,outline);
            g.DrawPath(border,outline);
        }
        LunarIcons.Draw(g,LunarGlyph.Search,new Point(23,Height/2),ContainsFocus?Palette.Accent:Palette.Muted,19);
    }
}
sealed class RiotIdInput:TextBox {
    const int SetCueBanner=0x1501;
    readonly string cue;
    public RiotIdInput(string cue="e.g. Lunar#BR1"){this.cue=cue;}
    [DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern IntPtr SendMessage(IntPtr window,int message,IntPtr showOnFocus,string text);

    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        ApplyCue();
        LunarLanguage.Changed+=ApplyCue;
    }
    protected override void OnHandleDestroyed(EventArgs e){LunarLanguage.Changed-=ApplyCue;base.OnHandleDestroyed(e);}
    void ApplyCue(){if(IsHandleCreated)SendMessage(Handle,SetCueBanner,new IntPtr(1),LunarLanguage.Translate(cue));}
}
class SearchView:UserControl {
    readonly SearchField field=new SearchField();
    readonly TextBox input=new RiotIdInput();
    readonly Label feedback=new Label();
    readonly Button search=new SoftButton(),clear=new SoftButton();
    readonly PlayerRows recent=new PlayerRows(44);
    readonly BackgroundWorker worker=new BackgroundWorker();
    readonly System.Windows.Forms.Timer retryTimer=new System.Windows.Forms.Timer{Interval=500};
    DateTime retryAt;
    string activeQuery="";
    bool retried;
    readonly List<Player> players=new List<Player>();
    readonly Font titleFont=new Font("Segoe UI",27,FontStyle.Bold);
    readonly Font subtitleFont=new Font("Segoe UI",11);
    Rectangle recentCard;
    int introY;
    public event Action<Player> OpenPlayer;
    public Func<string,Player> FindKnownPlayer;
    public Func<string> PreferredRegion;
    public event Action<Player,Task<PlayerProfile>,Task<PlayerProfile>> OpenResolvedPlayer;

    public SearchView(){
        DoubleBuffered=true;
        ResizeRedraw=true;
        BackColor=Palette.Bg;
        input.BorderStyle=BorderStyle.None;
        input.BackColor=SearchField.Fill;
        input.ForeColor=Palette.Text;
        input.Font=new Font("Segoe UI",11);
        input.AccessibleName="Riot ID";
        input.AccessibleDescription="Use the Name#TAG format";
        input.MaxLength=100;
        input.GotFocus+=delegate{field.Invalidate();};
        input.LostFocus+=delegate{field.Invalidate();};
        input.KeyDown+=delegate(object sender,KeyEventArgs e){
            if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;BeginSearch();}
        };
        input.TextChanged+=delegate{CancelRetry();};
        VisibleChanged+=delegate{if(!Visible)CancelRetry();};
        field.Click+=delegate{input.Focus();};
        field.Controls.Add(input);

        search.Text="Search";
        search.BackColor=Palette.Accent;
        search.ForeColor=Color.White;
        search.FlatStyle=FlatStyle.Flat;
        search.Font=new Font("Segoe UI",10,FontStyle.Bold);
        search.FlatAppearance.BorderSize=0;
        search.Cursor=Cursors.Hand;
        search.Click+=delegate{BeginSearch();};

        clear.Text="Clear";
        clear.AccessibleName="Clear recent searches";
        clear.FlatStyle=FlatStyle.Flat;
        clear.FlatAppearance.BorderSize=0;
        clear.BackColor=Palette.Raised;
        clear.ForeColor=Palette.Muted;
        clear.Cursor=Cursors.Hand;
        clear.Click+=delegate{players.Clear();RefreshRecent();};

        feedback.ForeColor=Palette.Muted;
        feedback.TextAlign=ContentAlignment.MiddleCenter;
        feedback.AutoEllipsis=true;
        recent.BackColor=Palette.Panel;
        recent.OpenOnClick=true;
        recent.AccessibleName="Recent profiles";
        recent.DrawRow+=DrawRecent;
        recent.OpenPlayer+=delegate(Player player){if(OpenPlayer!=null)OpenPlayer(player);};

        Controls.Add(field);
        Controls.Add(search);
        Controls.Add(clear);
        Controls.Add(feedback);
        Controls.Add(recent);
        RefreshRecent();
        worker.DoWork+=delegate(object sender,DoWorkEventArgs e){
            var searchRequest=(Tuple<string,string>)e.Argument;
            e.Result=RiotStatsClient.SearchAsync(searchRequest.Item1,searchRequest.Item2).GetAwaiter().GetResult();
        };
        worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){
            if(IsDisposed||Disposing)return;
            search.Enabled=true;
            search.Text="Search";
            if(e.Error!=null){
                if(RetryMatchesInput()){
                    var limit=e.Error as RiotStatsException;
                    if(!retried&&limit!=null&&limit.Status==429){
                        retryAt=limit.RetryAfter;
                        search.Enabled=false;
                        retryTimer.Start();
                        ShowRetry();
                    }else SetFeedback(e.Error.Message,true);
                }
                return;
            }
            var result=(Tuple<Player,Task<PlayerProfile>,Task<PlayerProfile>>)e.Result;
            var player=result.Item1;
            Remember(player);
            SetFeedback("",false);
            if(RetryMatchesInput()&&OpenResolvedPlayer!=null)OpenResolvedPlayer(player,result.Item2,result.Item3);
        };
        retryTimer.Tick+=delegate{
            if(retryAt==DateTime.MinValue){retryTimer.Stop();return;}
            retryAt=retryAt>RiotStatsClient.RemoteRetryAt?retryAt:RiotStatsClient.RemoteRetryAt;
            if(!RetryMatchesInput()){CancelRetry();return;}
            if(DateTime.UtcNow<retryAt){ShowRetry();return;}
            retryAt=DateTime.MinValue;
            retryTimer.Stop();
            retried=true;
            RunSearch(activeQuery);
        };
        Disposed+=delegate{retryTimer.Dispose();};
    }
    bool RetryMatchesInput(){return Visible&&String.Equals(input.Text.Trim(),activeQuery,StringComparison.OrdinalIgnoreCase);}
    void ShowRetry(){SetFeedback("Riot rate limit reached. Retrying in "+RiotStatsClient.RetrySeconds(retryAt)+"s…",false);}
    void CancelRetry(){
        if(retryAt==DateTime.MinValue)return;
        retryAt=DateTime.MinValue;
        retryTimer.Stop();
        search.Enabled=true;
        SetFeedback("",false);
    }
    void SetFeedback(string message,bool error){
        feedback.ForeColor=error?Palette.Red:Palette.Muted;
        feedback.Text=message;
    }
    void BeginSearch(){
        if(worker.IsBusy||retryAt!=DateTime.MinValue)return;
        string value=input.Text.Trim();
        if(!RiotStatsClient.HasRiotId(value)){
            SetFeedback("Enter the full Riot ID in the Name#TAG format.",true);
            input.Focus();
            return;
        }
        var cached=players.FirstOrDefault(player=>String.Equals(player.Name,value,StringComparison.OrdinalIgnoreCase));
        if(cached==null&&FindKnownPlayer!=null)cached=FindKnownPlayer(value);
        if(cached!=null&&OpenPlayer!=null){
            SetFeedback("",false);
            OpenPlayer(cached);
            return;
        }
        activeQuery=value;
        retried=false;
        RunSearch(value);
    }
    void RunSearch(string value){
        search.Enabled=false;
        search.Text="Searching…";
        SetFeedback("Looking up player…",false);
        worker.RunWorkerAsync(Tuple.Create(value,PreferredRegion==null?null:PreferredRegion()));
    }
    public void Remember(Player player){
        if(player==null||String.IsNullOrEmpty(player.Id))return;
        players.RemoveAll(item=>item.Id==player.Id);
        players.Insert(0,player);
        if(players.Count>10)players.RemoveAt(10);
        RefreshRecent();
    }
    void RefreshRecent(){
        recent.SetRows(players.ToArray(),false);
        recent.Visible=clear.Visible=players.Count>0;
        PerformLayout();
        Invalidate();
    }
    void DrawRecent(Graphics g,Player player,Rectangle bounds,bool selected,bool hover){
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var card=new Rectangle(bounds.X+1,bounds.Y+1,bounds.Width-2,bounds.Height-5);
        using(var shape=Skin.Round(card,9))
        using(var fill=new SolidBrush(selected||hover?Palette.AccentSoft:Palette.Raised))g.FillPath(fill,shape);
        ProfileCanvas.T(g,player.Name,10,Palette.Text,new Rectangle(card.X+13,card.Y,card.Width-44,card.Height),true);
        ProfileCanvas.T(g,"›",12,Palette.Accent,new Rectangle(card.Right-22,card.Y,18,card.Height));
    }
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);
        if(ClientSize.Width<280||ClientSize.Height<250)return;
        int width=Math.Min(704,ClientSize.Width-72),x=(ClientSize.Width-width)/2;
        introY=Math.Max(38,Math.Min(94,ClientSize.Height/9));
        field.SetBounds(x,introY+126,width-136,49);
        input.SetBounds(43,(field.Height-input.PreferredHeight)/2,field.Width-58,input.PreferredHeight);
        search.SetBounds(x+width-124,introY+126,124,49);
        feedback.SetBounds(x,introY+184,width,34);
        int top=introY+240;
        int available=Math.Max(112,ClientSize.Height-top-28);
        int height=Math.Min(available,Math.Max(140,65+players.Count*44));
        recentCard=new Rectangle(x,top,width,height);
        clear.SetBounds(recentCard.Right-104,top+10,88,28);
        recent.SetBounds(x+15,top+53,width-30,height-63);

    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(recentCard.Width<1)return;
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        DrawTitle(g);
        LunarText.DrawText(g,"View rank, stats, and recent matches by Riot ID.",
            subtitleFont,new Rectangle(20,introY+64,Width-40,32),Color.FromArgb(185,173,200),
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        ProfileCanvas.T(g,"Riot ID",9,Palette.Muted,new Rectangle(field.Left+2,introY+102,140,21),true);
        using(var card=Skin.Round(recentCard,13))
        using(var fill=new SolidBrush(Palette.Panel))
        using(var outline=new Pen(Palette.Border)){
            g.FillPath(fill,card);
            g.DrawPath(outline,card);
            g.DrawLine(outline,recentCard.Left+1,recentCard.Top+46,recentCard.Right-1,recentCard.Top+46);
        }
        ProfileCanvas.T(g,"Recent searches",10,Palette.Text,new Rectangle(recentCard.Left+17,recentCard.Top+7,210,32),true);
        if(players.Count==0){
            LunarIcons.Draw(g,LunarGlyph.Search,new Point(recentCard.Left+28,recentCard.Top+80),Palette.Accent,22);
            ProfileCanvas.T(g,"No profiles viewed in this session.",10,Palette.Muted,
                new Rectangle(recentCard.Left+48,recentCard.Top+61,recentCard.Width-65,38));
        }
    }
    void DrawTitle(Graphics g){
        const TextFormatFlags flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter;
        const string first="Search by ", last="Riot ID";
        int a=LunarText.MeasureText(g,first,titleFont,Size.Empty,flags).Width;
        int b=LunarText.MeasureText(g,last,titleFont,Size.Empty,flags).Width;
        int x=(Width-a-b)/2;
        LunarText.DrawText(g,first,titleFont,new Rectangle(x,introY,a+2,60),Palette.Text,flags);
        LunarText.DrawText(g,last,titleFont,new Rectangle(x+a,introY,b+2,60),Palette.Cyan,flags);
    }
    protected override void Dispose(bool disposing){
        if(disposing){worker.Dispose();titleFont.Dispose();subtitleFont.Dispose();}
        base.Dispose(disposing);
    }
}
}
