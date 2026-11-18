using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.IO;

namespace Lunar {
class Performance {
    public bool HasKda,HasDamage,HasHits,HasScore;public long Score;public long Kills,Deaths,Assists,Rounds,Head,Body,Leg;public double Damage;
    public double? KD {get{return !HasKda||Deaths==0?(double?)null:(double)Kills/Deaths;}}
    public double? ADR {get{return HasDamage&&Rounds>0?(double?)((double)Damage/Rounds):null;}}
    public double? HS {get{return HasHits&&Head+Body+Leg>0?(double?)(100.0*Head/(Head+Body+Leg)):null;}}
}
class ScorePlayer {public string Id,Name,Team,Agent,PartyId;public int Tier,AccountLevel;public Performance Stats;}
static class Catalog {
    static Dictionary<string,string> agents=new Dictionary<string,string>(),seasons=new Dictionary<string,string>();static DateTime retry=DateTime.MinValue;
    static Catalog(){try{using(var stream=typeof(Catalog).Assembly.GetManifestResourceStream("agents.json"))using(var reader=new StreamReader(stream)){foreach(var a in Json.A(Json.Codec.DeserializeObject(reader.ReadToEnd())))if(Json.S(a,"uuid")!="")agents[Json.S(a,"uuid")]=Json.S(a,"displayName");}}catch{}try{using(var stream=typeof(Catalog).Assembly.GetManifestResourceStream("seasons.json"))using(var reader=new StreamReader(stream)){var data=Json.A(Json.Get(Json.Codec.DeserializeObject(reader.ReadToEnd()),"data")).ToList();foreach(var a in data.Where(x=>Json.S(x,"type").EndsWith("::Act")).OrderByDescending(x=>Json.S(x,"startTime"))){DateTime start;if(DateTime.TryParse(Json.S(a,"startTime"),out start)&&start>DateTime.UtcNow)continue;string label=Json.S(a,"title");if(label==""){var parent=data.FirstOrDefault(x=>Json.S(x,"uuid")==Json.S(a,"parentUuid"));label=Json.S(parent,"displayName")+" / "+Json.S(a,"displayName");}seasons[Json.S(a,"uuid")]=label;}}}catch{}}
    public static IEnumerable<string> ActIds(){return seasons.Keys.ToArray();}
    public static readonly string[] Modes={"Competitive","Unrated","Swiftplay","Spike Rush","Deathmatch","Team Deathmatch"};
    public static readonly string[] Maps=MapCatalog.Names;
    public static string Agent(string id){string name;return agents.TryGetValue(id??"",out name)?name:"Agent unavailable";}
    public static string AgentId(string name){return agents.Where(a=>String.Equals(a.Value,name??"",StringComparison.OrdinalIgnoreCase)).Select(a=>a.Key).FirstOrDefault()??"";}
    public static string Season(string id){string name;return seasons.TryGetValue(id??"",out name)?name:"Unknown act";}
    public static void SetSeasons(object content){var next=new Dictionary<string,string>(seasons);foreach(var s in Json.A(Json.Get(content,"Seasons")))if(Json.S(s,"Type")=="act"&&Json.S(s,"ID")!="")if(!next.ContainsKey(Json.S(s,"ID")))next[Json.S(s,"ID")]=Json.S(s,"Name");seasons=next;}
    public static void LoadAgents(){
        if(DateTime.UtcNow<retry)return;retry=DateTime.UtcNow.AddMinutes(5);
        try{var request=(HttpWebRequest)WebRequest.Create("https://valorant-api.com/v1/agents?isPlayableCharacter=true&language=en-US");request.Timeout=3000;request.ReadWriteTimeout=3000;request.AllowAutoRedirect=false;
            using(var res=request.GetResponse())using(var stream=new StreamReader(res.GetResponseStream())){var result=Json.Codec.DeserializeObject(stream.ReadToEnd());var next=new Dictionary<string,string>();foreach(var a in Json.A(Json.Get(result,"data")))if(Json.S(a,"uuid")!="")next[Json.S(a,"uuid")]=Json.S(a,"displayName");if(next.Count>0){agents=next;retry=DateTime.UtcNow.AddHours(6);}}
        }catch{}
    }
    public static void Demo(){agents["add6443a-41bd-e414-f6ad-e58d267f4e95"]="Jett";agents["8e253930-4c05-31dd-1b6c-968525494517"]="Omen";agents["320b2a48-4d9b-a075-30f1-1f93a9b638fa"]="Sova";seasons=new Dictionary<string,string>{{"demo-act","Sample act"}};}
}
static class StatsMetrics {
    public static double Number(object value,string key){try{return Convert.ToDouble(Json.Get(value,key),System.Globalization.CultureInfo.InvariantCulture);}catch{return 0;}}
    public static Performance ForPlayer(object data,object player){
        var p=new Performance();var stats=Json.Get(player,"stats");string id=Json.S(player,"subject"),team=Json.S(player,"teamId");
        p.HasKda=Json.Get(stats,"kills")!=null&&Json.Get(stats,"deaths")!=null&&Json.Get(stats,"assists")!=null;
        if(p.HasKda){p.Kills=HistoryLogic.N(stats,"kills");p.Deaths=HistoryLogic.N(stats,"deaths");p.Assists=HistoryLogic.N(stats,"assists");}
        p.Rounds=HistoryLogic.N(stats,"roundsPlayed");p.HasScore=Json.Get(stats,"score")!=null;p.Score=HistoryLogic.N(stats,"score");
        var enemies=new HashSet<string>(Json.A(Json.Get(data,"players")).Where(x=>!Json.B(x,"isObserver")&&Json.S(x,"teamId")!=team&&Json.S(x,"subject")!=id).Select(x=>Json.S(x,"subject")));
        var damage=Json.Get(player,"roundDamage") as object[];
        if(damage!=null&&p.Rounds>0&&enemies.Count>0){p.HasDamage=damage.All(d=>Json.Get(d,"damage")!=null);p.Damage=damage.Where(d=>enemies.Contains(Json.S(d,"receiver"))).Sum(d=>Number(d,"damage"));}
        var rounds=Json.Get(data,"roundResults") as object[];
        if(rounds!=null&&rounds.Length>0&&p.Rounds>0){int covered=0;bool valid=true,validDamage=true;double roundDamage=0;
            foreach(var r in rounds){var mine=Json.A(Json.Get(r,"playerStats")).FirstOrDefault(s=>Json.S(s,"subject")==id);var shots=Json.Get(mine,"damage") as object[];if(shots==null){valid=false;validDamage=false;continue;}covered++;
                foreach(var hit in shots.Where(d=>enemies.Contains(Json.S(d,"receiver")))){if(Json.Get(hit,"damage")==null)validDamage=false;else roundDamage+=Number(hit,"damage");if(Json.Get(hit,"headshots")==null||Json.Get(hit,"bodyshots")==null||Json.Get(hit,"legshots")==null){valid=false;continue;}p.Head+=HistoryLogic.N(hit,"headshots");p.Body+=HistoryLogic.N(hit,"bodyshots");p.Leg+=HistoryLogic.N(hit,"legshots");}}
            p.HasHits=valid&&covered==p.Rounds&&enemies.Count>0;if(!p.HasDamage&&validDamage&&covered==p.Rounds&&enemies.Count>0){p.HasDamage=true;p.Damage=roundDamage;}
        }
        return p;
    }
    public static void Enrich(object data,MatchRow row,string subject){
        row.SeasonId=Json.S(Json.Get(data,"matchInfo"),"seasonId");
        foreach(var player in Json.A(Json.Get(data,"players"))){if(Json.B(player,"isObserver"))continue;string name=Json.S(player,"gameName"),tag=Json.S(player,"tagLine"),id=Json.S(player,"subject");var performance=ForPlayer(data,player);
            row.Roster.Add(new ScorePlayer {Id=id,Name=name==""?"Name unavailable":name+(tag==""?"":"#"+tag),Team=Json.S(player,"teamId"),Agent=Json.S(player,"characterId"),PartyId=RiotReader.ReadPartyId(player),Tier=(int)HistoryLogic.N(player,"competitiveTier",-1),AccountLevel=AccountLevels.Read(player),Stats=performance});
            if(String.Equals(id,subject,StringComparison.OrdinalIgnoreCase)){row.Metrics=performance;row.AgentId=Json.S(player,"characterId");}}
    }
    public static string F(double? value,string suffix="",int digits=1){return value.HasValue?value.Value.ToString("F"+digits)+suffix:"—";}
    public static string KD(Performance p){return !p.HasKda?"—":p.Deaths==0?(p.Kills>0?"∞":"—"):((double)p.Kills/p.Deaths).ToString("F2");}
    public static List<MatchRow> Filter(IEnumerable<MatchRow> matches,string mode,string map,string season,int days,DateTime now){return matches.Where(m=>(mode=="All modes"||m.Queue==mode)&&(map=="All maps"||m.Map==map)&&(season==""||m.SeasonId==season)&&(days==0||m.Started>=now.Date.AddDays(1-days))&&m.Started<=now).OrderByDescending(m=>m.Started).ToList();}
}
class Summary {
    public int Games,Wins,Losses,Draws,KdaGames,DamageGames,HitGames;public Performance Totals=new Performance();public long DamageRounds,ScoreRounds;public double? ACS {get{return ScoreRounds>0?(double?)Totals.Score/ScoreRounds:null;}}
    public double? WinRate {get{return Wins+Losses+Draws>0?(double?)(100.0*Wins/(Wins+Losses+Draws)):null;}}
    public double? ADR {get{return DamageRounds>0?(double?)((double)Totals.Damage/DamageRounds):null;}}
    public Summary(IEnumerable<MatchRow> rows){
        foreach(var m in rows.Where(m=>m.Completed&&m.Result!=Outcome.Ignored).GroupBy(m=>m.Id).Select(g=>g.First())){
            Games++;if(m.Result==Outcome.Win)Wins++;if(m.Result==Outcome.Loss)Losses++;if(m.Result==Outcome.Draw)Draws++;var p=m.Metrics;
            if(p.HasKda){KdaGames++;Totals.HasKda=true;Totals.Kills+=p.Kills;Totals.Deaths+=p.Deaths;Totals.Assists+=p.Assists;}
            if(p.HasDamage&&p.Rounds>0){DamageGames++;Totals.Damage+=p.Damage;DamageRounds+=p.Rounds;}
            if(p.HasScore&&p.Rounds>0){ScoreRounds+=p.Rounds;Totals.Score+=p.Score;}
            if(p.HasHits){HitGames++;Totals.HasHits=true;Totals.Head+=p.Head;Totals.Body+=p.Body;Totals.Leg+=p.Leg;}
        }
    }
}

static class TrackArt {
    static readonly Dictionary<string,string> maps=ReadMaps();
    static readonly Dictionary<string,Bitmap> mapTiles=new Dictionary<string,Bitmap>();
    static readonly Queue<string> tileOrder=new Queue<string>();
    static Dictionary<string,string> ReadMaps() {
        var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using(var stream=typeof(TrackArt).Assembly.GetManifestResourceStream("maps.json"))
        using(var reader=new StreamReader(stream))
            foreach(var item in Json.A(Json.Codec.DeserializeObject(reader.ReadToEnd())))
                names[Json.S(item,"name")]=Json.S(item,"id");
        return names;
    }
    public static string MapId(string name){string id;return maps.TryGetValue(name??"",out id)?id:"";}
    public static Image Asset(string name) {
        using(var stream=typeof(TrackArt).Assembly.GetManifestResourceStream(name))
            return stream==null?null:Decode(stream);
    }
    public static Image Decode(Stream stream) {
        try {using(var original=Image.FromStream(stream))return new Bitmap(original);}
        catch(ArgumentException){return null;}
        catch(System.Runtime.InteropServices.ExternalException){return null;}
    }
    public static void Cover(Graphics g,Image image,Rectangle target,float cropEdges=0f) {
        if(target.Width<=0||target.Height<=0)return;
        float usable=1f-2f*cropEdges;
        float scale=Math.Max(target.Width/(image.Width*usable),target.Height/(image.Height*usable));
        float w=target.Width/scale,h=target.Height/scale;
        var state=g.Save();
        try{
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            using(var sample=new System.Drawing.Imaging.ImageAttributes()){
                sample.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(image,target,(image.Width-w)/2,(image.Height-h)/2,w,h,GraphicsUnit.Pixel,sample);
            }
        }finally{g.Restore(state);}
    }
    public static void CoverWithFade(Graphics g,Image image,Rectangle bounds,Color background,float fadeStart,int rightShade){
        // Cover the full clipped surface so the art and the solid color never meet at a hard edge.
        Cover(g,image,bounds,.045f);
        using(var shade=new LinearGradientBrush(Rectangle.Inflate(bounds,2,0),background,background,0f)){
            shade.WrapMode=WrapMode.TileFlipXY;
            shade.InterpolationColors=new ColorBlend{
                Colors=new[]{background,background,Color.FromArgb(rightShade,background)},
                Positions=new[]{0f,fadeStart,1f}
            };
            g.FillRectangle(shade,bounds);
        }
        FadeArtEdge(g,new Rectangle(bounds.X,bounds.Y,bounds.Width,8),background,false);
        FadeArtEdge(g,new Rectangle(bounds.X,bounds.Bottom-5,bounds.Width,5),background,true);
        // The image's outermost pixels survive the fades as a light hairline; cover them with the panel colour.
        using(var edge=new SolidBrush(background)){
            g.FillRectangle(edge,bounds.X,bounds.Y,bounds.Width,2);
            g.FillRectangle(edge,bounds.X,bounds.Bottom-1,bounds.Width,1);
        }
    }
    static void FadeArtEdge(Graphics g,Rectangle strip,Color background,bool bottom){
        var transparent=Color.FromArgb(0,background);
        // Brush slightly larger than the strip, flip-wrapped: GDI+ otherwise repeats one line of the start colour.
        using(var brush=new LinearGradientBrush(Rectangle.Inflate(strip,0,1),bottom?transparent:background,bottom?background:transparent,90f)){
            brush.WrapMode=WrapMode.TileFlipXY;
            g.FillRectangle(brush,strip);
        }
    }
    internal static bool MapReady(MatchRow match){return MapImage(MapArtKey(match))!=null;}
    static string MapArtKey(MatchRow match){
        string key=match.MapId;
        Guid id;
        if(Guid.TryParse(key,out id))return id.ToString();
        if(!String.IsNullOrEmpty(match.Map))maps.TryGetValue(match.Map,out key);
        return key;
    }
    static Image MapImage(string key){
        if(String.IsNullOrEmpty(key))return null;
        return ApiArt.Map(key);
    }
    public static void DrawMapPreview(Graphics g,MatchRow match,Rectangle bounds){
        var state=g.Save();
        try{
            using(var shape=Skin.Round(bounds,5))g.SetClip(shape,CombineMode.Intersect);
            var image=MapImage(MapArtKey(match));
            if(image!=null)Cover(g,image,bounds);
            else using(var fill=new SolidBrush(Palette.Panel))g.FillRectangle(fill,bounds);
        }finally{g.Restore(state);}
    }
    public static void DrawMap(Graphics g,MatchRow match,Rectangle bounds,Color accent,bool thumbnail=false) {
        if(bounds.Width<=0||bounds.Height<=0)return;
        string key=MapArtKey(match);
        string tileKey=key+"|"+bounds.Width+"|"+bounds.Height+"|"+accent.ToArgb()+"|"+thumbnail;
        Bitmap tile;
        if(!mapTiles.TryGetValue(tileKey,out tile)){
            var map=MapImage(key);
            if(map==null&&!String.IsNullOrEmpty(key)){
                // Art still downloading: paint without caching so the real map appears once it arrives.
                var pending=g.Save();
                g.TranslateTransform(bounds.X,bounds.Y);
                PaintMapTile(g,null,new Rectangle(Point.Empty,bounds.Size),accent,thumbnail);
                g.Restore(pending);
                return;
            }
            tile=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(var buffer=Graphics.FromImage(tile)){
                buffer.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                PaintMapTile(buffer,map,new Rectangle(Point.Empty,bounds.Size),accent,thumbnail);
            }
            if(mapTiles.Count>=64){
                string oldest=tileOrder.Dequeue();
                mapTiles[oldest].Dispose();
                mapTiles.Remove(oldest);
            }
            mapTiles.Add(tileKey,tile);
            tileOrder.Enqueue(tileKey);
        }
        g.DrawImageUnscaled(tile,bounds.Location);
    }
    static void PaintMapTile(Graphics g,Image map,Rectangle bounds,Color accent,bool thumbnail){
        var state=g.Save();
        using(var shape=Skin.Round(bounds,7)){
            g.SetClip(shape,System.Drawing.Drawing2D.CombineMode.Intersect);
            using(var fill=new SolidBrush(Palette.Panel))g.FillRectangle(fill,bounds);
            if(map!=null){
                var art=bounds; // art fills the whole rounded tile: no 1px frame above the map
                var background=Palette.Panel;
                if(thumbnail){
                    Cover(g,map,art,.045f);
                    using(var shade=new LinearGradientBrush(Rectangle.Inflate(art,2,0),Color.FromArgb(135,background),Color.FromArgb(222,background),0f)){
                        shade.WrapMode=WrapMode.TileFlipXY;
                        g.FillRectangle(shade,art);
                    }
                    FadeArtEdge(g,new Rectangle(art.X,art.Y,art.Width,6),background,false);
                }else CoverWithFade(g,map,art,background,.27f,174);
            }
            if(!thumbnail&&bounds.Width>40){
                // Soft glow of the result colour from the left edge.
                var glow=new Rectangle(bounds.X,bounds.Y,Math.Max(2,bounds.Width*42/100),bounds.Height);
                using(var tint=new LinearGradientBrush(Rectangle.Inflate(glow,2,0),Color.FromArgb(52,accent),Color.FromArgb(0,accent),0f)){
                    tint.WrapMode=WrapMode.TileFlipXY;
                    g.FillRectangle(tint,glow);
                }
            }
            using(var stripe=new SolidBrush(accent))g.FillRectangle(stripe,bounds.X,bounds.Y,thumbnail?4:5,bounds.Height);
        }
        g.Restore(state);
        // The scaled map art bleeds one pixel past the rounded clip on the top edge, which showed as a light hairline.
        // Paint that row (between the rounded corners) with the panel colour so the tile edge blends in.
        var row=g.Save();
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.None;
        using(var edge=new SolidBrush(Palette.Panel))g.FillRectangle(edge,bounds.X+7,bounds.Y,Math.Max(0,bounds.Width-14),1);
        g.Restore(row);
    }
}
sealed class ProfileHeader:Panel {
    public ProfileHeader(){DoubleBuffered=true;ResizeRedraw=true;}
}
sealed class HomeWelcome:UserControl {
    public HomeWelcome(){
        DoubleBuffered=true;
        ResizeRedraw=true;
        BackColor=Palette.Bg;
        AccessibleName="Sign in to VALORANT";
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        Draw(e.Graphics,ClientRectangle,LunarGlyph.Home,"YOUR PROFILE","Your profile and match history load automatically once you sign in.");
    }
    internal static void Draw(Graphics g,Rectangle bounds,LunarGlyph glyph,string label,string description){
        if(bounds.Width<220||bounds.Height<220)return;
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int cx=bounds.Left+bounds.Width/2,y=bounds.Top+Math.Max(10,(bounds.Height-240)/2-12);
        var halo=new Rectangle(cx-54,y,108,108);
        using(var shape=new System.Drawing.Drawing2D.GraphicsPath()){
            shape.AddEllipse(halo);
            using(var glow=new System.Drawing.Drawing2D.PathGradientBrush(shape)){
                glow.CenterColor=Color.FromArgb(72,193,70,78);
                glow.SurroundColors=new[]{Color.FromArgb(0,193,70,78)};
                g.FillPath(glow,shape);
            }
        }
        using(var circle=new SolidBrush(Palette.Panel))g.FillEllipse(circle,cx-25,y+29,50,50);
        using(var ring=new Pen(Palette.Border))g.DrawEllipse(ring,cx-26,y+28,52,52);
        LunarIcons.Draw(g,glyph,new Point(cx,y+54),Palette.Accent,23);
        Center(g,label,8,Palette.Accent,new Rectangle(bounds.Left,y+101,bounds.Width,25),true);
        Center(g,"Start VALORANT to connect",21,Palette.Text,new Rectangle(bounds.Left+12,y+129,bounds.Width-24,38),true);
        int textWidth=Math.Min(475,bounds.Width-44);
        Center(g,description,10,Palette.Muted,new Rectangle(cx-textWidth/2,y+171,textWidth,46),false);
        var pill=new Rectangle(cx-112,y+232,224,31);
        using(var path=Skin.Round(pill,15))
        using(var fill=new SolidBrush(Palette.Panel))
        using(var outline=new Pen(Palette.Border)){
            g.FillPath(fill,path);
            g.DrawPath(outline,path);
        }
        using(var dot=new SolidBrush(Palette.Cyan))g.FillEllipse(dot,pill.X+17,pill.Y+12,7,7);
        Center(g,"Ready when you launch VALORANT",8,Palette.Muted,new Rectangle(pill.X+24,pill.Y,pill.Width-27,pill.Height),true);
    }
    static void Center(Graphics g,string text,int size,Color color,Rectangle bounds,bool bold){
        using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))
            LunarText.DrawText(g,text,font,bounds,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
}
static class AgentArt {
    static readonly Dictionary<string,Bitmap> thumbnails=new Dictionary<string,Bitmap>();
    public static void Draw(Graphics g,string id,Rectangle r){
        Guid parsed;
        string key=Guid.TryParse(id??"",out parsed)?parsed.ToString():Catalog.AgentId(id);
        Image image=ApiArt.Agent(key);
        if(image==null){
            using(var brush=new SolidBrush(Color.FromArgb(38,Palette.Accent)))
            using(var shape=Skin.Round(r,Math.Min(7,Math.Max(1,r.Height/4))))g.FillPath(brush,shape);
            return;
        }
        if(r.Width<=0||r.Height<=0)return;
        string sizeKey=key+"|"+r.Width+"|"+r.Height;
        Bitmap thumb;
        if(!thumbnails.TryGetValue(sizeKey,out thumb)){
            thumb=new Bitmap(r.Width,r.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var area=new Rectangle(Point.Empty,r.Size);
            using(var buffer=Graphics.FromImage(thumb))
            using(var clip=Skin.Round(area,Math.Min(7,r.Height/4))){
                buffer.SetClip(clip);
                buffer.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                buffer.DrawImage(image,area);
            }
            if(thumbnails.Count>=192){
                foreach(var existing in thumbnails.Values)existing.Dispose();
                thumbnails.Clear();
            }
            thumbnails.Add(sizeKey,thumb);
        }
        g.DrawImageUnscaled(thumb,r.Location);
    }
}
class Choice {public string Value,Label;public override string ToString(){return Label;}}
sealed class OutlineActionButton:Button {
    bool hovered;
    public bool BackArrow;
    public OutlineActionButton(){
        FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;
        Cursor=Cursors.Hand;
        Font=new Font("Segoe UI",9,FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;
        g.Clear(Parent.BackColor);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color hint=Enabled?Palette.Text:Palette.Muted;
        using(var outline=Skin.Round(new Rectangle(1,1,Width-3,Height-3),8))
        using(var fill=new SolidBrush(hovered&&Enabled?Palette.Raised:Palette.Panel))
        using(var border=new Pen(Focused&&ShowFocusCues?Palette.Accent:Palette.Border)){
            g.FillPath(fill,outline);
            g.DrawPath(border,outline);
        }
        int middle=Height/2;
        using(var icon=new Pen(Enabled?Palette.Accent:Palette.Muted,1.4f)){
            if(BackArrow){
                g.DrawLine(icon,15,middle,25,middle);
                g.DrawLines(icon,new[]{new Point(19,middle-4),new Point(15,middle),new Point(19,middle+4)});
            }else{
                g.DrawLine(icon,20,middle-5,20,middle+5);
                g.DrawLines(icon,new[]{new Point(16,middle+1),new Point(20,middle+5),new Point(24,middle+1)});
            }
        }
        ProfileCanvas.T(g,Text,9,hint,new Rectangle(32,0,Width-38,Height),true);
    }
}
class HistoryView:UserControl {
    Panel scoreboard;
    System.Threading.CancellationTokenSource archiveRequest,scoreboardRequest;
    readonly Dictionary<string,MatchRow> archive=new Dictionary<string,MatchRow>(StringComparer.OrdinalIgnoreCase);
    readonly Button loadArchive;
    string archiveOwner;
    readonly Dictionary<string,ArchiveCursor> archiveCursors=new Dictionary<string,ArchiveCursor>(StringComparer.OrdinalIgnoreCase);
    sealed class ArchiveCursor {
        public int Page=1;
        public bool Complete;
        public string Status="";
        public readonly HashSet<string> Pages=new HashSet<string>();
    }
    ArchiveCursor CurrentArchive(string queue){
        queue=queue??"";
        ArchiveCursor cursor;
        if(!archiveCursors.TryGetValue(queue,out cursor))archiveCursors.Add(queue,cursor=new ArchiveCursor());
        return cursor;
    }
    public event Action<Player> OpenPlayer;
    public Player Player;public PlayerProfile Profile;
    HomeWelcome welcome;
    bool homeMode;
    public bool HomeMode{
        get{return homeMode;}
        set{
            homeMode=value;
            if(homeMode&&welcome==null){
                welcome=new HomeWelcome{Dock=DockStyle.Fill,Visible=false};
                Controls.Add(welcome);
            }
            ApplyHomeState();
        }
    }
    readonly Panel header=new ProfileHeader();
    readonly System.Windows.Forms.Timer copiedTimer=new System.Windows.Forms.Timer{Interval=1600};
    Rectangle nameHit;bool copied;
    bool CanCopyName{get{return Player!=null&&RiotStatsClient.HasRiotId(Player.Name);}}
    void CopyName(){
        try{Clipboard.SetText(Player.Name);copied=true;}
        catch(System.Runtime.InteropServices.ExternalException){copied=false;}
        copiedTimer.Stop();copiedTimer.Start();header.Invalidate();
    }
    readonly Panel loading=new ProfileSkeleton{Dock=DockStyle.Fill};
    readonly Label loadingTitle=new Label{Text="Loading profile",TextAlign=ContentAlignment.MiddleCenter,ForeColor=Palette.Text,Font=new Font("Segoe UI",18,FontStyle.Bold)};
    readonly Label loadingDetail=new Label{Text="Fetching rank and 10 recent matches…",TextAlign=ContentAlignment.MiddleCenter,ForeColor=Palette.Muted};
    readonly ProgressBar loadingProgress=new ProgressBar{Style=ProgressBarStyle.Marquee,MarqueeAnimationSpeed=25};
    Image playerCard;
    byte[] cardBytes;readonly FlowLayoutPanel tabs=new FlowLayoutPanel(),filters=new FlowLayoutPanel();
    readonly SoftSelect mode=new SoftSelect(),map=new SoftSelect(),period=new SoftSelect(),act=new SoftSelect();
    readonly List<Button> buttons=new List<Button>();readonly ProfileCanvas canvas;bool updating;int activeTab;
    public HistoryView(){
        BackColor=Palette.Bg;ForeColor=Palette.Text;Font=new Font("Segoe UI",9);
        header.Dock=DockStyle.Top;header.Height=158;header.Paint+=DrawHeader;
        header.MouseMove+=delegate(object s,MouseEventArgs e){header.Cursor=CanCopyName&&nameHit.Contains(e.Location)?Cursors.Hand:Cursors.Default;};
        header.MouseClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left&&CanCopyName&&nameHit.Contains(e.Location))CopyName();};
        copiedTimer.Tick+=delegate{copiedTimer.Stop();copied=false;header.Invalidate();};
        var toolbar=Palette.Sidebar;
        tabs.Dock=DockStyle.Top;tabs.Height=45;tabs.Padding=new Padding(24,4,0,0);tabs.BackColor=toolbar;
        foreach(var title in new[]{"Overview","Matches","Agents","Maps"}){int index=buttons.Count;var b=new SoftButton{Text=title,Width=108,Height=32,Margin=new Padding(0,0,6,0),TextAlign=ContentAlignment.MiddleCenter,FlatStyle=FlatStyle.Flat,ForeColor=Palette.Muted,BackColor=toolbar,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;b.Click+=delegate{SetTab(index);};buttons.Add(b);tabs.Controls.Add(b);}
        filters.Dock=DockStyle.Top;filters.Height=53;filters.Padding=new Padding(24,5,0,0);filters.BackColor=toolbar;
        foreach(var combo in new[]{mode,map,act,period}){combo.Width=170;combo.Height=36;combo.Margin=new Padding(0,0,10,0);combo.SelectedIndexChanged+=delegate{if(!updating)RefreshRows();};filters.Controls.Add(combo);}
        mode.Items.AddRange(new object[]{"All modes","Competitive"});mode.SelectedIndex=1;map.Items.Add("All maps");map.SelectedIndex=0;act.Items.Add(new Choice{Value="",Label="All acts"});act.SelectedIndex=0;
        mode.SelectedIndexChanged+=delegate{if(!updating&&archiveRequest!=null)archiveRequest.Cancel();};
        period.Items.AddRange(new object[]{"Any date","Today","Last 7 days","Last 30 days"});period.SelectedIndex=0;
        canvas=new ProfileCanvas{Dock=DockStyle.Fill};canvas.OpenMatch+=OpenMatch;
        loadArchive=canvas.ArchiveButton;
        loadArchive.Click+=delegate{LoadArchive();};
        Controls.Add(loading);
        Controls.Add(canvas);Controls.Add(filters);Controls.Add(tabs);Controls.Add(header);UpdateProfile(null);SetTab(0);
    }
    public void SetTab(int index){activeTab=index;for(int i=0;i<buttons.Count;i++){buttons[i].ForeColor=i==index?Palette.Text:Palette.Muted;buttons[i].BackColor=i==index?Palette.AccentSoft:tabs.BackColor;}if(canvas!=null){canvas.Tab=index;RefreshRows();}}
    public void ActivateTab(int index){buttons[index].PerformClick();if(activeTab!=index)throw new InvalidOperationException("Tab did not activate");}
    public void UpdateProfile(PlayerProfile value){
        bool preserveScroll=Profile!=null&&value!=null&&Profile.Id==value.Id;
        string owner=Player==null?null:Player.Id;
        if(archiveOwner!=owner){
            if(archiveRequest!=null){archiveRequest.Cancel();archiveRequest=null;}
            CloseScoreboard();
            archiveOwner=owner;
            archive.Clear();
            archiveCursors.Clear();
        }
        Profile=value;updating=true;
        var incoming=value==null?null:value.CardArt;
        if(!Object.ReferenceEquals(incoming,cardBytes)){
            if(playerCard!=null)playerCard.Dispose();
            cardBytes=incoming;
            playerCard=null;
            if(cardBytes!=null)using(var stream=new MemoryStream(cardBytes))playerCard=TrackArt.Decode(stream);
        }
        var rows=(value==null?Enumerable.Empty<MatchRow>():value.Matches).Concat(archive.Values);
        Fill(mode,"All modes",Catalog.Modes.Concat(rows.Select(m=>m.Queue)));Fill(map,"All maps",MapCatalog.ForMode(Convert.ToString(mode.SelectedItem)));
        var selectedAct=act.SelectedItem as Choice;
        string old=selectedAct==null?"":selectedAct.Value;
        act.Items.Clear();
        act.Items.Add(new Choice{Value="",Label="All acts"});
        foreach(string id in Catalog.ActIds().Concat(rows.Select(m=>m.SeasonId)).Where(s=>s!="").Distinct()){
            string label=Catalog.Season(id).Replace(" // "," · ").Replace(" / "," · ")
                .Replace("EPISÓDIO ","Episode ").Replace("Episódio ","Episode ")
                .Replace("ATO ","Act ").Replace("Ato ","Act ");
            act.Items.Add(new Choice{Value=id,Label=label});
        }
        int index=act.Items.FindIndex(item=>((Choice)item).Value==old);
        act.SelectedIndex=Math.Max(0,index);
        act.Enabled=act.Items.Count>1;
        act.AccessibleName="Act";
        act.AccessibleDescription="Filter matches by act";
        updating=false;
        header.Invalidate();
        RefreshRows(preserveScroll);
        ApplyHomeState();
    }
    void ApplyHomeState(){
        bool disconnected=homeMode&&Player==null;
        bool pending=!disconnected&&Player!=null&&(Profile==null||Profile.HistoryLoading)&&scoreboard==null;
        loading.Visible=pending;
        if(pending){loadingTitle.Text="Loading profile";loadingDetail.Text="Fetching rank and 10 recent matches…";loading.BringToFront();}
        bool showProfile=!disconnected&&scoreboard==null&&!pending;
        header.Visible=tabs.Visible=filters.Visible=canvas.Visible=showProfile;
        if(welcome!=null){
            welcome.Visible=disconnected;
            if(disconnected)welcome.BringToFront();
        }
    }
    void Fill(SoftSelect combo,string first,IEnumerable<string> options){string old=Convert.ToString(combo.SelectedItem);combo.Items.Clear();combo.Items.Add(first);foreach(var s in options.Where(s=>!String.IsNullOrEmpty(s)).Distinct().OrderBy(s=>s))combo.Items.Add(s);combo.SelectedItem=combo.Items.Contains(old)?old:first;}
    void RefreshRows(bool preserveScroll=false){
        if(canvas==null)return;
        int days=period.SelectedIndex==1?1:period.SelectedIndex==2?7:period.SelectedIndex==3?30:0;
        string season=act.SelectedItem is Choice?((Choice)act.SelectedItem).Value:"";
        IEnumerable<MatchRow> rows=Profile==null?Enumerable.Empty<MatchRow>():Profile.Matches;
        string selectedMode=Convert.ToString(mode.SelectedItem);
        bool wasUpdating=updating;updating=true;
        Fill(map,"All maps",MapCatalog.ForMode(selectedMode));
        updating=wasUpdating;
        string selectedMap=Convert.ToString(map.SelectedItem);
        DateTime now=DateTime.Now;
        if(archive.Count>0)rows=rows.Concat(archive.Values).GroupBy(match=>match.Id,StringComparer.OrdinalIgnoreCase).Select(group=>group.First());
        var history=StatsMetrics.Filter(rows,selectedMode,selectedMap,season,days,now);
        var cursor=CurrentArchive(selectedMode);
        canvas.Profile=Profile;
        canvas.Connected=Player!=null;
        canvas.ArchiveStatus=activeTab<=1?cursor.Status:"";
        canvas.Rows=history;
        canvas.SetHistoryRows(activeTab<=1?history:Enumerable.Empty<MatchRow>());
        canvas.ShowArchiveFooter=activeTab<=1&&Profile!=null&&Player!=null&&RiotStatsClient.HasRiotId(Player.Name);
        loadArchive.Text=archiveRequest!=null?"Stop loading":cursor.Complete?"Archive loaded":cursor.Page>1?"Resume loading":"Load all matches";
        loadArchive.Enabled=Profile!=null&&Player!=null&&RiotStatsClient.HasRiotId(Player.Name)&&!cursor.Complete;
        if(preserveScroll)canvas.Invalidate();else canvas.ResetScroll();
    }
    async void LoadArchive(){
        if(archiveRequest!=null){archiveRequest.Cancel();return;}
        string queue=Convert.ToString(mode.SelectedItem);
        var cursor=CurrentArchive(queue);
        if(Player==null||cursor.Complete)return;
        var request=new System.Threading.CancellationTokenSource();
        archiveRequest=request;
        Player player=Player;
        cursor.Status="Loading archived matches…";
        RefreshRows(true);
        try{
            while(!request.IsCancellationRequested){
                object[] page=null;bool lastPage=false;
                var retryAt=DateTime.MinValue;
                try{var result=await RiotStatsClient.LoadArchivePage(player,cursor.Page,queue,request.Token);page=result.Items;lastPage=result.EndReached;}
                catch(RiotStatsException limit){
                    if(limit.Status!=429||limit.RetryAfter>DateTime.UtcNow.AddMinutes(5))throw;
                    retryAt=limit.RetryAfter;
                }
                if(page==null){
                    cursor.Status="Rate limit · retry at "+retryAt.ToLocalTime().ToString("HH:mm:ss");
                    RefreshRows(true);
                    await Task.Delay(TimeSpan.FromSeconds(RiotStatsClient.RetrySeconds(retryAt)),request.Token);
                    continue;
                }
                request.Token.ThrowIfCancellationRequested();
                if(page.Length==0){cursor.Complete=true;break;}
                var parsed=page.Select(item=>RiotStatsClient.ParseArchivedMatch(item,player.Id)).Where(match=>match!=null).ToList();
                if(parsed.Count==0)throw new InvalidOperationException("This archived page did not contain recognizable match records.");
                string pageKey=String.Join("|",parsed.Select(match=>match.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase));
                if(!cursor.Pages.Add(pageKey))throw new InvalidOperationException("The provider repeated a page. Loaded matches are still available.");
                foreach(var match in parsed.Where(match=>queue=="All modes"||match.Queue==queue))
                    if(!archive.ContainsKey(match.Id))archive.Add(match.Id,match);
                cursor.Page++;
                if(lastPage){cursor.Complete=true;break;}
                cursor.Status="Loading archived matches…";
                RefreshRows(true);
            }
            request.Token.ThrowIfCancellationRequested();
            cursor.Status="Available archive loaded";
        }catch(OperationCanceledException){cursor.Status="Loading paused";}
         catch(Exception error){cursor.Status="Archive incomplete · "+error.Message;}
        finally{
            request.Cancel();
            if(Object.ReferenceEquals(archiveRequest,request)){
                archiveRequest=null;
                if(!IsDisposed&&!Disposing)UpdateProfile(Profile);
            }
            request.Dispose();
        }
    }

    protected override void Dispose(bool disposing) {
        if(disposing){
            if(playerCard!=null){playerCard.Dispose();playerCard=null;}
            if(archiveRequest!=null){archiveRequest.Cancel();archiveRequest=null;}
            if(scoreboardRequest!=null){scoreboardRequest.Cancel();scoreboardRequest=null;}
        }
        base.Dispose(disposing);
    }
    void DrawHeader(object sender,PaintEventArgs e) {
        var g=e.Graphics;
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds=new Rectangle(22,12,header.ClientSize.Width-44,134);
        if(bounds.Width<200)return;
        var state=g.Save();
        using(var shape=Skin.Round(bounds,14))g.SetClip(shape,System.Drawing.Drawing2D.CombineMode.Intersect);
        var background=Palette.Panel;
        using(var fill=new SolidBrush(background))g.FillRectangle(fill,bounds);
        if(playerCard!=null)
            TrackArt.CoverWithFade(g,playerCard,Rectangle.Inflate(bounds,-1,-1),background,.31f,116);
        g.Restore(state);
        using(var shape=Skin.Round(bounds,14))
        using(var outline=new Pen(Palette.Border))g.DrawPath(outline,shape);
        int x=bounds.X+22;
        ProfileCanvas.T(g,HomeMode?"YOUR PROFILE":"PLAYER PROFILE",9,Palette.Cyan,new Rectangle(x,32,300,24),true);
        string shownName=Player==null?"Open VALORANT to connect":Player.Name;
        ProfileCanvas.T(g,shownName,25,Color.White,new Rectangle(x,59,bounds.Width-210,51),true);
        nameHit=Rectangle.Empty;
        if(CanCopyName){
            int nameWidth;
            using(var font=new Font("Segoe UI",25,FontStyle.Bold))
                nameWidth=Math.Min(bounds.Width-250,LunarText.MeasureText(g,shownName,font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Width);
            var chip=new Rectangle(x+nameWidth+12,72,copied?74:28,26);
            nameHit=Rectangle.Union(new Rectangle(x,59,nameWidth,51),chip);
            using(var shape=Skin.Round(chip,7))
            using(var fill=new SolidBrush(copied?Palette.AccentSoft:Color.FromArgb(70,Palette.Raised)))
            using(var edge=new Pen(copied?Palette.Accent:Palette.Border)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
            if(copied)ProfileCanvas.T(g,"Copied",8,Palette.Accent,new Rectangle(chip.X+10,chip.Y,chip.Width-12,chip.Height),true);
            else using(var pen=new Pen(Palette.Muted,1.4f)){
                g.DrawRectangle(pen,chip.X+11,chip.Y+9,9,10);
                g.DrawLines(pen,new[]{new Point(chip.X+8,chip.Y+16),new Point(chip.X+8,chip.Y+6),new Point(chip.X+17,chip.Y+6)});
            }
        }
        ProfileCanvas.T(g,AccountLevels.Label(Player,Profile)+(Profile!=null&&Profile.AccountLevelFromMatch&&!(Player!=null&&Player.AccountLevel>0)?" · recent match":""),9,Palette.Cyan,new Rectangle(x,111,bounds.Width-210,23));
        if(Profile!=null) {
            ProfileCanvas.T(g,"TODAY · W / L",8,Color.White,new Rectangle(bounds.Right-138,43,110,22),true);
            ProfileCanvas.T(g,Profile.Record,24,Profile.RecordColor==Palette.Muted?Color.White:Profile.RecordColor,new Rectangle(bounds.Right-138,63,110,45),true);
        }
    }
    void CloseScoreboard(){
        if(scoreboardRequest!=null){scoreboardRequest.Cancel();scoreboardRequest=null;}
        if(scoreboard==null)return;
        scoreboard.Dispose();
        scoreboard=null;
        ApplyHomeState();
    }
    internal bool BackFromScoreboard(){
        if(scoreboard==null)return false;
        CloseScoreboard();
        return true;
    }
    async void OpenMatch(MatchRow row){
        CloseScoreboard();
        var detail=new MatchDetailView{Dock=DockStyle.Fill,Match=row};
        detail.OpenPlayer+=delegate(Player player){if(OpenPlayer!=null)OpenPlayer(player);};
        detail.BackRequested+=delegate{CloseScoreboard();};
        scoreboard=new Panel{Dock=DockStyle.Fill,BackColor=Palette.Bg};
        scoreboard.Controls.Add(detail);
        header.Visible=tabs.Visible=filters.Visible=canvas.Visible=false;
        Controls.Add(scoreboard);
        scoreboard.BringToFront();
        if(row.Roster.Count>0&&row.Roster.All(p=>RiotStatsClient.HasRiotId(p.Name)))return;
        var request=new System.Threading.CancellationTokenSource();
        scoreboardRequest=request;
        detail.Status="Loading scoreboard…";
        try{
            var match=await RiotStatsClient.LoadScoreboard(row,Player,request.Token);
            if(request.IsCancellationRequested||detail.IsDisposed)return;
            if(match!=null&&match.Roster.Count>0){
                row.Roster=match.Roster;
                detail.Match=match;detail.Status="";
            }else detail.Status="Scoreboard unavailable for this archived match.";
        }catch(OperationCanceledException){}
         catch(Exception error){
            if(!request.IsCancellationRequested&&!detail.IsDisposed)
                detail.Status="Scoreboard unavailable · "+error.Message;
        }finally{
            if(!detail.IsDisposed)detail.Invalidate();
            if(Object.ReferenceEquals(scoreboardRequest,request))scoreboardRequest=null;
            request.Dispose();
        }
    }
    public void RenderMatch(string path){
        ActivateTab(1);
        canvas.Refresh();
        Application.DoEvents();
        canvas.ActivateFirstMatch();
        Application.DoEvents();
        if(scoreboard==null)throw new InvalidOperationException("Match click failed");
        using(var bitmap=new Bitmap(scoreboard.Width,scoreboard.Height)){
            scoreboard.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
            bitmap.Save(path);
        }
        CloseScoreboard();
    }
}
class ProfileCanvas:Control {
    public bool Connected;public int Tab;public PlayerProfile Profile;public List<MatchRow> Rows=new List<MatchRow>();public event Action<MatchRow> OpenMatch;
    public string ArchiveStatus="";
    public readonly Button ArchiveButton=new OutlineActionButton{Visible=false};
    public bool ShowArchiveFooter;
    const int MatchRowHeight=66,DayHeadingHeight=31;
    sealed class HistoryLine {
        public int Offset,Height;
        public MatchRow Match;
        public string Heading,Tally;
    }
    readonly List<HistoryLine> historyLines=new List<HistoryLine>();
    int historyHeight,historyCount,footerAfter,footerCenter;
    [ThreadStatic] static Dictionary<KeyValuePair<float,bool>,Font> textFonts;
    readonly List<KeyValuePair<Rectangle,MatchRow>> hits=new List<KeyValuePair<Rectangle,MatchRow>>();int scroll,contentHeight;
    public ProfileCanvas(){
        DoubleBuffered=true;
        ResizeRedraw=true;
        BackColor=Palette.Bg;
        MouseEnter+=delegate{Focus();};
        TabStop=true;
        Controls.Add(ArchiveButton);
    }
    public static void T(Graphics g,string text,float size,Color color,Rectangle r,bool bold=false){
        text=LunarLanguage.Translate(text);
        const TextFormatFlags flags=TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter
            |TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
        if(textFonts==null)textFonts=new Dictionary<KeyValuePair<float,bool>,Font>();
        var key=new KeyValuePair<float,bool>(size,bold);
        Font font;
        if(!textFonts.TryGetValue(key,out font)){
            font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);
            textFonts.Add(key,font);
        }
        LunarText.DrawText(g,text,font,r,color,flags);
    }
    public void SetHistoryRows(IEnumerable<MatchRow> matches){
        historyLines.Clear();
        historyCount=0;
        int offset=47;
        DateTime today=DateTime.Today;
        foreach(var day in matches.GroupBy(match=>match.Started.Date)){
            int count=day.Count();
            string name=day.Key==today?"Today":day.Key==today.AddDays(-1)?"Yesterday":day.Key.ToString("dd/MM/yyyy");
            int wins=day.Count(match=>match.Completed&&match.Result==Outcome.Win);
            int losses=day.Count(match=>match.Completed&&match.Result==Outcome.Loss);
            historyLines.Add(new HistoryLine{
                Offset=offset,Height=DayHeadingHeight,Heading=name+" · "+count+(count==1?" match":" matches"),Tally=wins+" W  | "+losses+" L"
            });
            offset+=DayHeadingHeight;
            foreach(var match in day){
                historyLines.Add(new HistoryLine{Offset=offset,Height=MatchRowHeight,Match=match});
                offset+=MatchRowHeight;
            }
            historyCount+=count;
        }
        historyHeight=offset+7;
    }
    static void Box(Graphics g,Rectangle r){Skin.Card(g,r);}
    public void ResetScroll(){scroll=0;Invalidate();}
    void ScrollTo(int position){
        int next=Math.Max(0,Math.Min(Math.Max(0,contentHeight-Height),position));
        if(next==scroll)return;
        scroll=next;
        Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e){ScrollTo(scroll-e.Delta*65/120);base.OnMouseWheel(e);}
    protected override bool IsInputKey(Keys key){
        return key==Keys.End||key==Keys.Home||key==Keys.PageDown||key==Keys.PageUp||base.IsInputKey(key);
    }
    protected override void OnKeyDown(KeyEventArgs e){
        switch(e.KeyCode){
            case Keys.End:ScrollTo(contentHeight);break;
            case Keys.Home:ScrollTo(0);break;
            case Keys.PageDown:ScrollTo(scroll+Height-45);break;
            case Keys.PageUp:ScrollTo(scroll-Height+45);break;
            default:base.OnKeyDown(e);return;
        }
        e.Handled=true;
    }
    void LayoutArchiveFooter(int afterRows,int center){
        if(!ShowArchiveFooter||Tab>1){ArchiveButton.Visible=false;return;}
        const int width=166,height=36,gap=16;
        int top=afterRows+gap;
        contentHeight=Math.Max(contentHeight,top+height+gap);
        int y=top-Math.Min(scroll,Math.Max(0,contentHeight-Height));
        ArchiveButton.Visible=y>=33&&y+height<=Height-8;
        if(ArchiveButton.Visible)ArchiveButton.SetBounds(center-width/2,y,width,height);
    }
    protected override void OnMouseMove(MouseEventArgs e){Cursor=e.Y>=33&&hits.Any(h=>h.Key.Contains(e.Location))?Cursors.Hand:Cursors.Default;base.OnMouseMove(e);}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Y<33||e.Button!=MouseButtons.Left)return;var match=hits.Where(h=>h.Key.Contains(e.Location)).Select(h=>h.Value).FirstOrDefault();if(match!=null&&OpenMatch!=null)OpenMatch(match);}
    public void ActivateFirstMatch(){var r=hits.Where(h=>h.Key.Y>=33).Select(h=>h.Key).First();OnMouseClick(new MouseEventArgs(MouseButtons.Left,1,r.X+4,r.Y+4,0));}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;hits.Clear();
        if(Profile==null){ArchiveButton.Visible=false;T(g,Connected?"Loading match history…":"Open VALORANT to load your match history.",12,Palette.Muted,new Rectangle(28,70,Width-56,45));return;}
        if(Profile.HistoryLoading&&Rows.Count==0){
            ArchiveButton.Visible=false;
            int statusX=28;
            if(Tab==0&&Profile.Tier>=0){
                int side=Math.Min(314,Math.Max(228,(Width-48)/4));
                DrawRank(g,new Rectangle(24,40,side,164));
                statusX=side+48;
            }
            T(g,Profile.Message,11,Palette.Muted,new Rectangle(statusX,66,Width-statusX-28,60));
            return;
        }
        if(Rows.Count==0&&(Tab>1||historyCount==0)){
            if(ArchiveStatus!="")T(g,ArchiveStatus,8,Palette.Muted,new Rectangle(26,4,Width-52,26));
            int x=28;
            if(Tab==0&&Profile.Tier>=0){
                int side=Math.Min(314,Math.Max(228,(Width-48)/4));
                DrawProfileSidebar(g,new Rectangle(24,40,side,0));
                x=24+side+24;
            }
            string message=Profile.Message;
            if(message=="")message=!Profile.HistoryAvailable?"Match history unavailable.":Profile.Matches.Count==0?"No matches were returned for this profile.":"No loaded matches match these filters.";
            T(g,message,11,Palette.Muted,new Rectangle(x,66,Width-x-28,60));
            contentHeight=Height;
            LayoutArchiveFooter(126,Width/2);
            return;
        }
        string note=(Tab<=1?historyCount:Rows.Count)+" matches in the current filter · "+(ArchiveStatus==""?"recent sample only":ArchiveStatus);
        if(Profile.Message!="")note+=" · "+Profile.Message;
        T(g,note,8,Palette.Muted,new Rectangle(26,4,Width-52,26));
        var state=g.Save();
        g.SetClip(new Rectangle(0,33,Width,Math.Max(0,Height-33)),System.Drawing.Drawing2D.CombineMode.Intersect);
        g.TranslateTransform(0,-scroll);
        if(Tab==0)Overview(g);else if(Tab==1)Matches(g);else Groups(g,Tab==2);
        g.Restore(state);
        LayoutArchiveFooter(footerAfter,footerCenter);
        int maxScroll=Math.Max(0,contentHeight-Height);
        if(scroll>maxScroll){scroll=maxScroll;Invalidate();return;}
        int track=Height-40;
        if(maxScroll>0&&track>0){
            int thumb=Math.Min(track,Math.Max(28,track*Height/contentHeight));
            using(var b=new SolidBrush(Palette.Accent))
                g.FillRectangle(b,Width-9,34+(track-thumb)*scroll/maxScroll,3,thumb);
        }
    }

    static readonly Color Purple=Palette.Accent, Cyan=Palette.Cyan;
    static readonly Color PanelColor=Palette.Panel, BorderColor=Palette.Border;
    static Color ResultColor(Outcome result) {
        return result==Outcome.Win?Palette.Win:result==Outcome.Loss?Palette.Loss:result==Outcome.Draw?Palette.Draw:Palette.Muted;
    }
    static string ResultName(Outcome result) {
        switch(result) {
            case Outcome.Win:return "Win";
            case Outcome.Loss:return "Loss";
            case Outcome.Draw:return "Draw";
            default:return "No result";
        }
    }
    static void DashboardCard(Graphics g,Rectangle rect) {
        using(var path=Skin.Round(rect,12))
        using(var fill=new SolidBrush(PanelColor))
        using(var pen=new Pen(BorderColor)){
            g.FillPath(fill,path);
            g.DrawPath(pen,path);
        }
    }
    static void Section(Graphics g,string title,Rectangle r) {
        T(g,title,11,Palette.Text,new Rectangle(r.X+16,r.Y+4,r.Width-32,38),true);
        using(var pen=new Pen(BorderColor))g.DrawLine(pen,r.X+1,r.Y+44,r.Right-1,r.Y+44);
    }
    void Overview(Graphics g) {
        const int x=24,y=40,gap=16;
        int side=Math.Min(314,Math.Max(228,(Width-48)/4));
        int main=x+side+gap, mainWidth=Width-main-24;
        if(scroll+33<700)DrawProfileSidebar(g,new Rectangle(x,y,side,0));
        if(scroll+33<y+220)DrawOverviewSummary(g,new Rectangle(main,y,mainWidth,220));
        int bottom=DrawHistory(g,new Rectangle(main,y+235,mainWidth,0),ArchiveStatus==""?"Recent matches":"Match history");
        footerAfter=bottom;
        footerCenter=main+mainWidth/2;
        contentHeight=Math.Max(700,bottom+20);
    }
    void DrawOverviewSummary(Graphics g,Rectangle panel){
        int main=panel.X,y=panel.Y,mainWidth=panel.Width;
        var summary=new Summary(Rows);
        DashboardCard(g,panel);
        var queues=Rows.Select(row=>row.Queue).Distinct().ToList();
        Section(g,queues.Count==1&&queues[0]=="Competitive"?"Competitive summary":"Match summary",panel);
        string[] labels={"DAMAGE / ROUND","K/D","HEADSHOT %","WIN RATE","ACS","MATCHES","WINS","LOSSES"};
        string[] values={StatsMetrics.F(summary.ADR),StatsMetrics.KD(summary.Totals),StatsMetrics.F(summary.Totals.HS,"%"),
            StatsMetrics.F(summary.WinRate,"%"),StatsMetrics.F(summary.ACS),summary.Games.ToString(),summary.Wins.ToString(),summary.Losses.ToString()};
        int w=(mainWidth-60)/4;
        for(int i=0;i<labels.Length;i++){
            var metric=new Rectangle(main+15+(w+10)*(i%4),y+58+76*(i/4),w,64);
            DashboardCard(g,metric);
            Color top=i==6?Palette.Win:i==7?Palette.Loss:i==3?Palette.ForRate(summary.WinRate):i==2?Cyan:Purple;
            using(var line=new Pen(top,1.5f))g.DrawLine(line,metric.X+11,metric.Y+1,metric.Right-11,metric.Y+1);
            T(g,labels[i],8,Palette.Muted,new Rectangle(metric.X+9,metric.Y+8,w-16,18),true);
            T(g,values[i],18,i==6?Palette.Win:i==7?Palette.Loss:i==3?Palette.ForRate(summary.WinRate):Palette.Text,new Rectangle(metric.X+9,metric.Y+25,w-16,32),true);
        }
    }
    void DrawRank(Graphics g,Rectangle rank){
        int x=rank.X,y=rank.Y,w=rank.Width;
        DashboardCard(g,rank);
        Section(g,"Competitive rank",rank);
        RankArt.Draw(g,Profile.Tier,new Rectangle(x+14,y+55,55,55));
        // "Radiant · last known rank": big rank name, the qualifier goes small next to the RR so nothing is cut.
        string label=Profile.RankLabel??"",note="";
        int split=label.IndexOf(" · ",StringComparison.Ordinal);
        if(split>0){note=label.Substring(split+3);label=label.Substring(0,split);}
        T(g,label,15,Palette.Text,new Rectangle(x+76,y+52,w-88,33),true);
        string rr=Profile.RR<0?"":Profile.RR+" RR";
        int rrWidth=0;
        if(rr!=""){T(g,rr,10,Purple,new Rectangle(x+76,y+82,w-88,24),true);using(var f=new Font("Segoe UI",10,FontStyle.Bold))rrWidth=LunarText.MeasureText(g,rr,f).Width;}
        if(note!="")T(g,(rr==""?"":"· ")+LunarLanguage.Translate(note),8,Palette.Muted,new Rectangle(x+76+rrWidth,y+83,Math.Max(0,w-88-rrWidth),24));
        RankArt.Draw(g,Profile.PeakTier,new Rectangle(x+16,y+122,24,24));
        string peak=Profile.PeakTier<0?"—":RankArt.Name(Profile.PeakTier)+(Profile.PeakRR>=0?" · "+Profile.PeakRR+" RR":"");
        T(g,"Peak · "+peak,9,Palette.Muted,new Rectangle(x+48,y+119,w-60,29),true);
    }
    void DrawProfileSidebar(Graphics g,Rectangle column) {
        int x=column.X,y=column.Y,w=column.Width;
        DrawRank(g,new Rectangle(x,y,w,164));
        y+=178;
        var agents=Rows.Where(m=>!String.IsNullOrEmpty(m.AgentId)).GroupBy(m=>m.AgentId)
            .OrderByDescending(a=>a.Count()).Take(3).ToList();
        var agentCard=new Rectangle(x,y,w,49+Math.Max(1,agents.Count)*48);
        DashboardCard(g,agentCard);
        Section(g,"Top agents",agentCard);
        if(agents.Count==0)T(g,"Data unavailable",9,Palette.Muted,new Rectangle(x+16,y+52,w-32,35));
        for(int i=0;i<agents.Count;i++){
            var agent=agents[i];
            var stats=new Summary(agent);
            int row=y+47+i*48;
            AgentArt.Draw(g,agent.Key,new Rectangle(x+15,row+8,32,32));
            T(g,Catalog.Agent(agent.Key),10,Palette.Text,new Rectangle(x+54,row+3,w-126,21),true);
            T(g,stats.Games+(stats.Games==1?" match":" matches"),8,Palette.Muted,new Rectangle(x+54,row+23,w-126,18));
            T(g,StatsMetrics.F(stats.WinRate,"%"),10,Palette.ForRate(stats.WinRate),new Rectangle(x+w-66,row+3,60,21),true);
            T(g,StatsMetrics.KD(stats.Totals)+" K/D",8,Palette.Muted,new Rectangle(x+w-66,row+23,60,18));
        }
        y=agentCard.Bottom+14;
        var maps=Rows.GroupBy(m=>m.Map).OrderByDescending(m=>m.Count()).Take(4).ToList();
        var mapCard=new Rectangle(x,y,w,49+maps.Count*47);
        DashboardCard(g,mapCard);
        Section(g,"Most played maps",mapCard);
        for(int i=0;i<maps.Count;i++){
            var stats=new Summary(maps[i]);
            int row=y+46+i*47;
            TrackArt.DrawMapPreview(g,maps[i].First(),new Rectangle(x+15,row+7,50,32));
            T(g,maps[i].Key,10,Palette.Text,new Rectangle(x+74,row+3,w-145,22),true);
            T(g,stats.Wins+" W  /  "+stats.Losses+" L",8,Palette.Muted,new Rectangle(x+74,row+23,w-145,19));
            T(g,StatsMetrics.F(stats.WinRate,"%"),10,Palette.ForRate(stats.WinRate),new Rectangle(x+w-66,row+4,60,21),true);
        }
    }
    void Matches(Graphics g) {
        footerAfter=DrawHistory(g,new Rectangle(24,40,Width-48,0),"Match history");
        footerCenter=Width/2;
        contentHeight=footerAfter+20;
    }
    int DrawHistory(Graphics g,Rectangle area,string title) {
        area.Height=historyHeight;
        int firstVisible=scroll+33,lastVisible=scroll+Height;
        if(area.Bottom<=firstVisible||area.Top>=lastVisible)return area.Bottom;
        DashboardCard(g,area);
        if(area.Y+45>firstVisible)Section(g,title,area);
        int low=0,high=historyLines.Count;
        while(low<high){
            int mid=low+(high-low)/2;
            var line=historyLines[mid];
            if(area.Y+line.Offset+line.Height<=firstVisible)low=mid+1;
            else high=mid;
        }
        for(int i=low;i<historyLines.Count;i++){
            var line=historyLines[i];
            int y=area.Y+line.Offset;
            if(y>=lastVisible)break;
            if(line.Match!=null)DrawMatchRow(g,line.Match,new Rectangle(area.X+7,y,area.Width-14,MatchRowHeight-4));
            else{
                T(g,line.Heading,9,Palette.Muted,new Rectangle(area.X+15,y,area.Width-125,DayHeadingHeight),true);
                var tally=System.Text.RegularExpressions.Regex.Match(line.Tally??"",@"(\d+)\D+(\d+)");
                Color tallyColor=Palette.Muted;
                if(tally.Success){int w=int.Parse(tally.Groups[1].Value),l=int.Parse(tally.Groups[2].Value);tallyColor=w>l?Palette.Win:w<l?Palette.Loss:Palette.Draw;}
                T(g,line.Tally,9,tallyColor,new Rectangle(area.Right-94,y,86,DayHeadingHeight),true);
            }
        }
        return area.Bottom;
    }
    void DrawMatchRow(Graphics g,MatchRow match,Rectangle row) {
        Color accent=ResultColor(match.Result);
        TrackArt.DrawMap(g,match,row,accent);
        AgentArt.Draw(g,match.AgentId,new Rectangle(row.X+14,row.Y+12,37,37));
        int mapX=row.X+59, scoreX=row.X+row.Width*36/100;
        T(g,match.Map,11,Palette.Text,new Rectangle(mapX,row.Y+8,scoreX-mapX-6,23),true);
        T(g,match.Queue+" · "+match.Started.ToString("HH:mm"),8,Palette.Muted,new Rectangle(mapX,row.Y+31,scoreX-mapX-6,20));
        string score=match.Score=="—"?ResultName(match.Result):match.Score;
        T(g,score,12,accent,new Rectangle(scoreX,row.Y+7,row.Width/5,25),true);
        int columnWidth=row.X+row.Width*56/100-scoreX-8;
        int pillWidth=ResultPill(g,match,accent,new Point(scoreX,row.Y+33),columnWidth);
        MvpBadge(g,match,new Point(scoreX+pillWidth+6,row.Y+33),columnWidth-pillWidth-6);
        MatchMetric(g,row,56,match.Kda,"K / D / A");
        MatchMetric(g,row,74,StatsMetrics.F(match.Metrics.HS,"%"),"HS%");
        var stats=match.Metrics;
        MatchMetric(g,row,87,StatsMetrics.F(stats.HasScore&&stats.Rounds>0?(double?)stats.Score/stats.Rounds:null),"ACS");
        hits.Add(new KeyValuePair<Rectangle,MatchRow>(new Rectangle(row.X,row.Y-scroll,row.Width,row.Height),match));
    }
    // Filled result badge (WIN / LOSS / DRAW) plus the RR change, so the outcome reads at a glance.
    static readonly Font PillFont=new Font("Segoe UI",7.5f,FontStyle.Bold);
    // Text drawn inside the scrolling canvas must keep its translate transform and clip (scroll offset).
    const TextFormatFlags CanvasText=TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
    static Color Lighter(Color c,int by){return Color.FromArgb(Math.Min(255,c.R+by),Math.Min(255,c.G+by),Math.Min(255,c.B+by));}
    static void Pill(Graphics g,Rectangle pill,string text,Color fillColor,Color edgeColor,Color textColor){
        var smooth=g.SmoothingMode;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var shape=Skin.Round(pill,pill.Height/2))
        using(var fill=new SolidBrush(fillColor))
        using(var edge=new Pen(edgeColor)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        g.SmoothingMode=smooth;
        LunarText.DrawText(g,text,PillFont,pill,textColor,CanvasText|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    // Filled result badge with the RR change inside: "WIN · +18 RR", "LOSS · -16 RR", "DRAW".
    static int ResultPill(Graphics g,MatchRow match,Color accent,Point origin,int maxWidth){
        string label=match.Result==Outcome.Win?"WIN":match.Result==Outcome.Loss?"LOSS":match.Result==Outcome.Draw?"DRAW":"No result";
        if(match.RRChange.HasValue)label+=" · "+match.RRChange.Value.ToString("+0;-0;0")+" RR";
        var size=LunarText.MeasureText(g,label,PillFont,Size.Empty,CanvasText);
        var pill=new Rectangle(origin.X,origin.Y,Math.Min(maxWidth,size.Width+18),18);
        Pill(g,pill,label,Color.FromArgb(match.Result==Outcome.Draw?70:62,accent),Color.FromArgb(160,accent),Lighter(accent,34));
        return pill.Width;
    }
    // ★ MVP (gold) or ★ TEAM MVP (silver) next to the score.
    static readonly Color MvpGold=Color.FromArgb(246,201,92),MvpSilver=Color.FromArgb(196,204,218);
    static void MvpBadge(Graphics g,MatchRow match,Point origin,int maxWidth){
        if(match.Mvp==MvpKind.None||maxWidth<40)return;
        bool gold=match.Mvp==MvpKind.Match;
        string label="★ "+LunarLanguage.Translate(gold?"MATCH MVP":"TEAM MVP");
        var size=LunarText.MeasureText(g,label,PillFont,Size.Empty,CanvasText);
        // Not enough room before the K/D/A column: short "★ MVP" (gold = match, silver = team).
        if(size.Width+16>maxWidth){label="★ MVP";size=LunarText.MeasureText(g,label,PillFont,Size.Empty,CanvasText);}
        if(size.Width+16>maxWidth)return;
        var badge=new Rectangle(origin.X,origin.Y,size.Width+16,18);
        var color=gold?MvpGold:MvpSilver;
        Pill(g,badge,label,Color.FromArgb(gold?64:46,color),Color.FromArgb(170,color),Lighter(color,10));
    }
    static void MatchMetric(Graphics g,Rectangle row,int position,string value,string label) {
        int x=row.X+row.Width*position/100;
        int width=Math.Min(row.Right-x-3,row.Width*17/100);
        T(g,value,10,Palette.Text,new Rectangle(x,row.Y+8,width,24),true);
        T(g,label,8,Palette.Muted,new Rectangle(x,row.Y+32,width,19));
    }
    void Groups(Graphics g,bool agents){var groups=Rows.Where(m=>m.Completed&&m.Result!=Outcome.Ignored).GroupBy(m=>agents?m.AgentId:m.Map).OrderByDescending(gr=>gr.Count()).ToList();
        string[] titles={agents?"AGENT":"MAP","MATCHES","W–L","WIN RATE","K/D","ADR","HS%"};int[] xs={42,Width-570,Width-475,Width-385,Width-270,Width-185,Width-100};for(int i=0;i<titles.Length;i++)T(g,titles[i],8,Palette.Muted,new Rectangle(xs[i],44,i==0?Width-650:100,24));
        for(int i=0;i<groups.Count;i++){var group=groups[i];var s=new Summary(group);int y=80+i*62;var card=new Rectangle(26,y,Width-52,54);if(agents)Box(g,card);else TrackArt.DrawMap(g,group.First(),card,Color.FromArgb(123,83,174),true);string[] cells={agents?Catalog.Agent(group.Key):group.Key,s.Games.ToString(),s.Wins+"-"+s.Losses,StatsMetrics.F(s.WinRate,"%"),StatsMetrics.KD(s.Totals),StatsMetrics.F(s.ADR),StatsMetrics.F(s.Totals.HS,"%")};for(int c=0;c<cells.Length;c++){if(c==0&&agents){AgentArt.Draw(g,group.Key,new Rectangle(34,y+12,30,30));T(g,cells[c],11,Palette.Text,new Rectangle(73,y+12,Width-723,28),true);}else T(g,cells[c],c==0?11:10,c==2?(s.Wins>s.Losses?Palette.Win:s.Wins<s.Losses?Palette.Loss:Palette.Muted):c==3?Palette.ForRate(s.WinRate):Palette.Text,new Rectangle(xs[c],y+12,c==0?Width-650:100,28),c==0);}}
        if(groups.Count==0)T(g,"No data matches these filters.",12,Palette.Muted,new Rectangle(42,100,Width-84,45));contentHeight=Math.Max(220,groups.Count*62+100);
    }
}

class MatchDetailView:Control {
    const int BoardTop=178, GroupHeaderHeight=54, GroupGap=12;
    static readonly Color BoardBorder=Palette.Border;
    static readonly Color Blue=Palette.Cyan;
    static readonly Color Red=Palette.Red;
    readonly Button backButton=new OutlineActionButton{Text="Back to matches",BackArrow=true};
    readonly List<KeyValuePair<Rectangle,ScorePlayer>> playerHits=new List<KeyValuePair<Rectangle,ScorePlayer>>();
    int scroll,maxScroll;

    public MatchRow Match;
    public string Status="Scoreboard unavailable.";
    public event Action<Player> OpenPlayer;
    public event Action BackRequested;

    public MatchDetailView(){
        DoubleBuffered=true;
        ResizeRedraw=true;
        BackColor=Palette.Bg;
        TabStop=true;
        backButton.Click+=delegate{if(BackRequested!=null)BackRequested();};
        Controls.Add(backButton);
    }
    int ContentWidth{get{return Math.Min(1120,Math.Max(0,ClientSize.Width-48));}}
    int ContentLeft{get{return (ClientSize.Width-ContentWidth)/2;}}
    protected override void OnResize(EventArgs e){
        base.OnResize(e);
        backButton.SetBounds(ContentLeft,18,153,33);
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Focus();}
    protected override void OnMouseWheel(MouseEventArgs e){
        ScrollTo(scroll-e.Delta*60/120);
        base.OnMouseWheel(e);
    }
    protected override bool IsInputKey(Keys key){
        return key==Keys.PageDown||key==Keys.PageUp||key==Keys.Home||key==Keys.End||base.IsInputKey(key);
    }
    protected override void OnKeyDown(KeyEventArgs e){
        switch(e.KeyCode){
            case Keys.PageDown:ScrollTo(scroll+Height-BoardTop-18);break;
            case Keys.PageUp:ScrollTo(scroll-Height+BoardTop+18);break;
            case Keys.Home:ScrollTo(0);break;
            case Keys.End:ScrollTo(maxScroll);break;
            default:base.OnKeyDown(e);return;
        }
        e.Handled=true;
    }
    void ScrollTo(int offset){
        int next=Math.Max(0,Math.Min(maxScroll,offset));
        if(scroll==next)return;
        scroll=next;
        Invalidate();
    }
    ScorePlayer PlayerAt(Point location){
        return playerHits.Where(hit=>hit.Key.Contains(location)).Select(hit=>hit.Value).FirstOrDefault();
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        Cursor=PlayerAt(e.Location)==null?Cursors.Default:Cursors.Hand;
    }
    protected override void OnMouseClick(MouseEventArgs e){
        base.OnMouseClick(e);
        var player=PlayerAt(e.Location);
        if(player!=null&&e.Button==MouseButtons.Left&&OpenPlayer!=null)
            OpenPlayer(new Player{Id=player.Id,Name=player.Name});
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        playerHits.Clear();
        if(Match==null||ContentWidth<360)return;
        var g=e.Graphics;
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        DrawMatchHeader(g,new Rectangle(ContentLeft,65,ContentWidth,97));
        if(Match.Roster.Count==0){
            ProfileCanvas.T(g,Status,11,Palette.Muted,new Rectangle(ContentLeft+12,BoardTop,ContentWidth-24,65));
            return;
        }
        var teams=Match.Roster.GroupBy(player=>player.Team??"",StringComparer.OrdinalIgnoreCase)
            .OrderBy(team=>team.Key,StringComparer.OrdinalIgnoreCase).ToList();
        int available=Height-BoardTop-16;
        int reserved=teams.Count*(GroupHeaderHeight+GroupGap+6);
        int rowHeight=Math.Max(36,Math.Min(46,(available-reserved)/Match.Roster.Count));
        maxScroll=Math.Max(0,reserved+Match.Roster.Count*rowHeight-available);
        scroll=Math.Min(scroll,maxScroll);
        var state=g.Save();
        g.SetClip(new Rectangle(0,BoardTop,Width,Math.Max(0,Height-BoardTop)),
            System.Drawing.Drawing2D.CombineMode.Intersect);
        int top=BoardTop-scroll;
        foreach(var team in teams){
            var players=team.OrderByDescending(player=>player.Stats.Kills).ToList();
            int panelHeight=GroupHeaderHeight+players.Count*rowHeight+6;
            DrawTeam(g,team.Key,players,new Rectangle(ContentLeft,top,ContentWidth,panelHeight),rowHeight);
            top+=panelHeight+GroupGap;
        }
        g.Restore(state);
        if(maxScroll>0&&available>0){
            int thumb=Math.Min(available,Math.Max(24,available*available/(available+maxScroll)));
            using(var brush=new SolidBrush(Palette.Accent))
                g.FillRectangle(brush,ContentLeft+ContentWidth+9,BoardTop+(available-thumb)*scroll/maxScroll,3,thumb);
        }
    }
    void DrawMatchHeader(Graphics g,Rectangle rect){
        string outcome=Match.Result==Outcome.Win?"WIN":Match.Result==Outcome.Loss?"LOSS":Match.Result==Outcome.Draw?"DRAW":"";
        Color accent=Match.Result==Outcome.Win?Palette.Green:Match.Result==Outcome.Loss?Red:Palette.Accent;
        TrackArt.DrawMap(g,Match,rect,accent);
        ProfileCanvas.T(g,Match.Map,28,Palette.Text,new Rectangle(rect.X+22,rect.Y+10,rect.Width-202,46),true);
        ProfileCanvas.T(g,Match.Queue+"  ·  "+Match.Started.ToString("dd/MM/yyyy HH:mm"),10,Palette.Muted,
            new Rectangle(rect.X+23,rect.Y+55,rect.Width-202,30));
        Center(g,Match.Score,24,Palette.Text,new Rectangle(rect.Right-156,rect.Y+10,139,45),true);
        if(outcome!="")Center(g,outcome,8,accent,new Rectangle(rect.Right-156,rect.Y+56,139,22),true);
    }
    void DrawTeam(Graphics g,string team,IList<ScorePlayer> players,Rectangle panel,int rowHeight){
        if(panel.Bottom<=BoardTop||panel.Top>=Height)return;
        Color accent=String.Equals(team,"Blue",StringComparison.OrdinalIgnoreCase)?Blue
            :String.Equals(team,"Red",StringComparison.OrdinalIgnoreCase)?Red:Palette.Accent;
        using(var shape=Skin.Round(panel,11))
        using(var fill=new SolidBrush(Palette.Panel))
        using(var border=new Pen(BoardBorder)){
            g.FillPath(fill,shape);
            g.DrawPath(border,shape);
            g.DrawLine(border,panel.Left+1,panel.Top+31,panel.Right-1,panel.Top+31);
            g.DrawLine(border,panel.Left+1,panel.Top+GroupHeaderHeight,panel.Right-1,panel.Top+GroupHeaderHeight);
        }
        using(var marker=new SolidBrush(accent))g.FillEllipse(marker,panel.X+15,panel.Y+13,7,7);
        ProfileCanvas.T(g,team==""?"Players":team+" team",10,accent,new Rectangle(panel.X+29,panel.Y+2,panel.Width-44,28),true);

        int[] centers={panel.X+panel.Width*51/100,panel.X+panel.Width*64/100,
            panel.X+panel.Width*79/100,panel.X+panel.Width*92/100};
        string[] headings={"RANK","K / D / A","ADR","HS%"};
        ProfileCanvas.T(g,"PLAYER / AGENT",8,Palette.Muted,new Rectangle(panel.X+15,panel.Y+32,250,21));
        for(int i=0;i<headings.Length;i++)
            Center(g,headings[i],8,Palette.Muted,new Rectangle(centers[i]-45,panel.Y+32,90,21),false);

        for(int i=0;i<players.Count;i++){
            int y=panel.Y+GroupHeaderHeight+i*rowHeight;
            if(y>=Height)break;
            if(y+rowHeight<=BoardTop)continue;
            var player=players[i];
            if(i%2==0){
                using(var shade=new SolidBrush(Palette.Raised))
                    g.FillRectangle(shade,panel.X+7,y+2,panel.Width-14,rowHeight-3);
            }
            int center=y+rowHeight/2;
            AgentArt.Draw(g,player.Agent,new Rectangle(panel.X+15,center-14,28,28));
            bool canOpen=!String.IsNullOrEmpty(player.Id)&&RiotStatsClient.HasRiotId(player.Name);
            int nameX=panel.X+54,nameWidth=centers[0]-43-nameX;
            if(canOpen){
                var hit=Rectangle.Intersect(new Rectangle(panel.X+14,y,centers[0]-panel.X-53,rowHeight),
                    new Rectangle(0,BoardTop,Width,Math.Max(0,Height-BoardTop)));
                if(!hit.IsEmpty)playerHits.Add(new KeyValuePair<Rectangle,ScorePlayer>(hit,player));
            }
            ProfileCanvas.T(g,player.Name,10,canOpen?Palette.Text:Palette.Muted,new Rectangle(nameX,y+3,nameWidth,20),true);
            ProfileCanvas.T(g,(player.AccountLevel>0?"Level "+player.AccountLevel:"Level —")+" · "+Catalog.Agent(player.Agent),8,Palette.Muted,new Rectangle(nameX,y+21,nameWidth,rowHeight-23));
            RankArt.Draw(g,player.Tier,new Rectangle(centers[0]-15,center-15,30,30));
            var stats=player.Stats;
            string kda=stats.HasKda?stats.Kills+" / "+stats.Deaths+" / "+stats.Assists:"—";
            Center(g,kda,10,Palette.Text,new Rectangle(centers[1]-50,y,100,rowHeight),true);
            Center(g,StatsMetrics.F(stats.ADR),9,Palette.Text,new Rectangle(centers[2]-40,y,80,rowHeight),false);
            Center(g,StatsMetrics.F(stats.HS,"%"),9,Palette.Text,new Rectangle(centers[3]-40,y,80,rowHeight),false);
        }
    }
    static void Center(Graphics g,string text,int size,Color color,Rectangle bounds,bool bold){
        using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))
            LunarText.DrawText(g,text,font,bounds,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter
                |TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.PreserveGraphicsClipping);
    }
}
static class ProfileChecks {
    static int count;static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
    static object D(object o){return Json.Codec.DeserializeObject(Json.Codec.Serialize(o));}
    public static int Run(){
        count=0;
        var fixture=D(new {matchInfo=new {seasonId="act"},players=new object[]{new {subject="me",teamId="Blue",characterId="agent",gameName="Name",tagLine="BR1",stats=new {kills=8,deaths=2,assists=1,roundsPlayed=1},roundDamage=new[]{new {receiver="enemy",damage=150.5},new {receiver="ally",damage=999.0}}},new {subject="enemy",teamId="Red"},new {subject="ally",teamId="Blue"}},roundResults=new[]{new {playerStats=new[]{new {subject="me",damage=new[]{new {receiver="enemy",headshots=1,bodyshots=3,legshots=0},new {receiver="ally",headshots=99,bodyshots=0,legshots=0}}}}}}});
        var me=Json.A(Json.Get(fixture,"players")).First();var metric=StatsMetrics.ForPlayer(fixture,me);
        Check(metric.KD==4&&metric.Kills==8,"K/D from player totals");
        Check(metric.HasDamage&&Math.Abs(metric.ADR.Value-150.5)<0.001,"ADR preserves decimals and excludes team damage");
        Check(metric.HasHits&&metric.HS==25,"HS uses hit counts and excludes friendly hits");
        var row=new MatchRow();StatsMetrics.Enrich(fixture,row,"me");Check(row.AgentId=="agent"&&row.SeasonId=="act"&&row.Roster.Count==3&&row.Roster[0].Name=="Name#BR1","match enrichment includes agent season and scoreboard");
        var missing=StatsMetrics.ForPlayer(D(new {players=new object[0]}),D(new {subject="me"}));Check(!missing.HasKda&&!missing.HasDamage&&!missing.HasHits&&StatsMetrics.KD(missing)=="—","missing metrics are not zeros");
        var culture=System.Threading.Thread.CurrentThread.CurrentCulture;try{System.Threading.Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo("pt-BR");Check(Math.Abs(StatsMetrics.ForPlayer(fixture,me).Damage-150.5)<0.001,"numeric metrics work under Portuguese culture");}finally{System.Threading.Thread.CurrentThread.CurrentCulture=culture;}
        var rows=new List<MatchRow>{new MatchRow{Id="a",Completed=true,Result=Outcome.Win,Metrics=new Performance{HasKda=true,HasDamage=true,HasHits=true,Kills=10,Deaths=1,Rounds=1,Damage=100,Head=1}},new MatchRow{Id="b",Completed=true,Result=Outcome.Loss,Metrics=new Performance{HasKda=true,HasDamage=true,HasHits=true,Kills=0,Deaths=9,Rounds=3,Damage=900,Head=0,Body=9}}};
        var summary=new Summary(rows);Check(summary.WinRate==50&&summary.Totals.KD==1,"aggregate K/D uses totals rather than average ratios");Check(summary.ADR==250&&summary.Totals.HS==10,"ADR and HS use weighted denominators");
        rows.Add(rows[0]);Check(new Summary(rows).Games==2,"summary deduplicates match IDs");rows.Add(new MatchRow{Id="ignored",Completed=true,Result=Outcome.Ignored,Metrics=new Performance{HasKda=true,Kills=999,Deaths=1}});Check(new Summary(rows).Totals.Kills==10,"ignored games excluded from performance totals");
        DateTime now=new DateTime(2026,11,15,21,0,0);var samples=new[]{new MatchRow{Id="today",Started=now.AddHours(-1),Queue="Competitive",Map="Ascent",SeasonId="a"},new MatchRow{Id="old",Started=now.AddDays(-8),Queue="Unrated",Map="Haven",SeasonId="b"},new MatchRow{Id="future",Started=now.AddDays(1),Queue="Competitive",Map="Ascent",SeasonId="a"}};
        Check(StatsMetrics.Filter(samples,"All modes","All maps","",0,now).Count==2,"future records excluded from filters");
        Check(StatsMetrics.Filter(samples,"Competitive","Ascent","a",1,now).Single().Id=="today","mode map act and date filters combine");
        Check(StatsMetrics.Filter(samples,"All modes","All maps","",7,now).Count==1,"seven day filter uses date boundary");
        Check(new Summary(new MatchRow[0]).WinRate==null&&StatsMetrics.KD(new Performance())=="—","empty summary has no fabricated ratio");
        var zero=new Performance{HasKda=true,Kills=5,Deaths=0};Check(StatsMetrics.KD(zero)=="∞"&&!zero.KD.HasValue,"zero deaths handled without division error");
        Console.WriteLine(count+" profile checks passed.");return 0;
    }
}
}
