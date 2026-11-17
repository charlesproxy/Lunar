using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Threading.Tasks;

[assembly: System.Reflection.AssemblyTitle("Lunar")]
[assembly: System.Reflection.AssemblyProduct("Lunar")]
[assembly: System.Reflection.AssemblyFileVersion("1.60.0.0")]

namespace Lunar {
static class Json {
    public static JavaScriptSerializer Codec { get { return new JavaScriptSerializer { MaxJsonLength = 8388608 }; } }
    public static object Get(object o, string key) { var d=o as Dictionary<string,object>; return d!=null && d.ContainsKey(key)?d[key]:null; }
    public static string S(object o,string key) { return Convert.ToString(Get(o,key)) ?? ""; }
    public static object GetAnyCase(object o,params string[] keys) {
        var values=o as Dictionary<string,object>;
        if(values==null)return null;
        foreach(var key in keys){
            object value;
            if(values.TryGetValue(key,out value))return value;
            var match=values.FirstOrDefault(pair=>String.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase));
            if(match.Key!=null)return match.Value;
        }
        return null;
    }
    public static IEnumerable<object> A(object o) { return (o as object[]) ?? new object[0]; }
    public static bool B(object o,string key) { return Get(o,key) is bool && (bool)Get(o,key); }
}
class ApiFailure:Exception {
    public int Code; public ApiFailure(int c):base("HTTP "+c) { Code=c; }
}
class Player {
    public string Id,Name,Team,State,CardId,AgentId,PartyId,Region;
    public int AccountLevel;
    public override string ToString(){return Name??"";}
    public Player Copy(){return (Player)MemberwiseClone();}
}
class Snapshot {
    public Player Self;
    public string Status="Waiting for VALORANT",Detail="Open VALORANT and enter agent select. Lunar connects automatically.",Side="Starting side unavailable",Match="",Team="";
    public string Map="";
    public int? AllyScore,EnemyScore;
    public List<Player> Allies=new List<Player>(),Enemies=new List<Player>();
    public int Delay=5000;
    public string Diagnostics="No match data has been requested.";
    public Snapshot CopyRoster(){
        var copy=(Snapshot)MemberwiseClone();
        copy.Allies=Allies.Select(player=>player.Copy()).ToList();
        copy.Enemies=Enemies.Select(player=>player.Copy()).ToList();
        copy.Self=Self==null?null:Self.Copy();
        return copy;
    }

}
partial class RiotReader {
    Player cachedSelf;
    Player GetSelfPlayer(){
        if(cachedSelf!=null&&cachedSelf.Id==self)return cachedSelf;
        var me=new Player{Id=self,Name="Your profile",State="YOU",Region=region};
        try{var response=Request("https://pd."+shard+".a.pvp.net/name-service/v2/players","PUT",new[]{self},false,false);var entry=Json.A(response).FirstOrDefault(x=>Json.S(x,"Subject")==self);if(Json.S(entry,"GameName")!=""){me.Name=Json.S(entry,"GameName")+"#"+Json.S(entry,"TagLine");cachedSelf=me;}}
        catch(ApiFailure e){if(e.Code==429)throw;}catch(WebException){}
        return me;
    }
    string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string basic,origin,access,entitlement,version,shard,region,self;
    string nameMatch=""; Dictionary<string,string> names=new Dictionary<string,string>();
    static string ReadShared(string path,int limit) {
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            if(limit>0 && f.Length>limit) f.Seek(-limit,SeekOrigin.End);
            using(var r=new StreamReader(f)) return r.ReadToEnd();
        }
    }
    object Request(string url,string method,object body,bool isLocal,bool absent,int timeout=6000) {
        var uri=new Uri(url);
        if(isLocal ? uri.Host!="127.0.0.1" : !Regex.IsMatch(uri.Host,@"^((?:pd|shared)\.(na|eu|ap|kr|pbe)|glz-(na|latam|br|eu|ap|kr)-1\.(na|eu|ap|kr|pbe))\.a\.pvp\.net$")) throw new InvalidOperationException();
        var req=(HttpWebRequest)WebRequest.Create(uri);
        req.UserAgent="Lunar/1.60";req.Method=method; req.Timeout=timeout; req.ReadWriteTimeout=timeout; req.AllowAutoRedirect=false;
        req.Accept="application/json"; req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        if(isLocal) {
            req.Proxy=null;
            req.ServerCertificateValidationCallback=delegate { return true; };
            req.Headers[HttpRequestHeader.Authorization]="Basic "+basic;
        } else {
            req.Headers[HttpRequestHeader.Authorization]="Bearer "+access;
            req.Headers["X-Riot-Entitlements-JWT"]=entitlement;
            req.Headers["X-Riot-ClientVersion"]=version;
            req.Headers["X-Riot-ClientPlatform"]=Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"platformType\":\"PC\",\"platformOS\":\"Windows\",\"platformOSVersion\":\"10.0.19042.1.256.64bit\",\"platformChipset\":\"Unknown\"}"));
        }
        if(body!=null) {
            byte[] data=Encoding.UTF8.GetBytes(Json.Codec.Serialize(body)); req.ContentType="application/json"; req.ContentLength=data.Length;
            using(var s=req.GetRequestStream()) s.Write(data,0,data.Length);
        }
        try {
            using(var res=(HttpWebResponse)req.GetResponse()) {
                if((int)res.StatusCode>=300) throw new ApiFailure((int)res.StatusCode);
                using(var sr=new StreamReader(res.GetResponseStream())) return Json.Codec.DeserializeObject(sr.ReadToEnd());
            }
        } catch(WebException e) {
            var res=e.Response as HttpWebResponse;
            if(res==null) throw;
            int code=(int)res.StatusCode; res.Dispose();
            if(code==404 && absent) return null;
            throw new ApiFailure(code);
        }
    }
    static bool HasProcess(string name) {
        var ps=Process.GetProcessesByName(name); bool found=ps.Length>0;
        foreach(var p in ps) p.Dispose(); return found;
    }
    bool ConnectLocalClient(){
        string[] parts=ReadShared(Path.Combine(local,@"Riot Games\Riot Client\Config\lockfile"),4096).Trim().Split(':');
        int port,pid;
        if(parts.Length!=5||parts[4]!="https"||!Int32.TryParse(parts[1],out pid)||!ClientProcessAlive(pid)||
            !Int32.TryParse(parts[2],out port)||port<1||port>65535)return false;
        origin="https://127.0.0.1:"+port;
        basic=Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+parts[3]));
        return true;
    }
    bool wasInMatch;
    public Snapshot Poll(){return Poll(null);}
    internal Snapshot Poll(Action<Snapshot> publishRoster) {
        try { return PollInternal(publishRoster); }
        catch(ApiFailure e) {
            var s=new Snapshot();
            s.Status=e.Code==401?"Session expired":e.Code==403?"Request rejected by the service":e.Code==429?"Rate limit reached":"Service temporarily unavailable";
            s.Detail="HTTP "+e.Code+". Retrying automatically.";
            s.Delay=e.Code==429?60000:15000; return s;
        }
        catch(WebException) { return new Snapshot {Status="Client or service unreachable",Detail="Retrying in 15 seconds.",Delay=15000}; }
        catch(IOException) { return new Snapshot {Status="Waiting for Riot Client files",Detail="Open Riot Client and VALORANT. Lunar will retry automatically."}; }
        catch(UnauthorizedAccessException) { return new Snapshot {Status="Cannot access local Riot Client files",Detail="Open Lunar under the same Windows account as the game.",Delay=15000}; }
        catch(Exception) { return new Snapshot {Status="Unexpected client response",Detail="The service may have changed its response format.",Delay=15000}; }
        finally { basic=access=entitlement=null; }
    }
    Snapshot InitializeSession(bool requireGame=true,bool fetchCurrentVersion=false) {
        if(requireGame&&!HasProcess("VALORANT-Win64-Shipping")) { names.Clear(); nameMatch=""; return new Snapshot(); }
        string[] lf=ReadShared(Path.Combine(local,@"Riot Games\Riot Client\Config\lockfile"),4096).Trim().Split(':');
        int port;
        if(lf.Length!=5 || !Int32.TryParse(lf[2],out port) || port<1 || port>65535 || lf[4]!="https") throw new InvalidOperationException();
        int clientPid;
        if(fetchCurrentVersion&&(!Int32.TryParse(lf[1],out clientPid)||!ClientProcessAlive(clientPid)))
            return new Snapshot {Status="Waiting for Riot Client"};
        basic=Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+lf[3])); origin="https://127.0.0.1:"+port;
        if(fetchCurrentVersion&&!ReadSessionAuthorization())
            return new Snapshot {Status="Waiting for Riot Client sign-in"};
        var sessions=Request(origin+"/product-session/v1/external-sessions","GET",null,true,fetchCurrentVersion) as Dictionary<string,object>;
        version=""; region=""; shard="";
        if(sessions!=null) foreach(var session in sessions.Values) {
            if(Json.S(session,"productId")!="valorant") continue;
            version=Json.S(session,"version");
            string args=String.Join(" ",Json.A(Json.Get(Json.Get(session,"launchConfiguration"),"arguments")).Select(x=>Convert.ToString(x)));
            var r=Regex.Match(args,@"-ares-deployment=([a-z]+)"); if(r.Success) region=r.Groups[1].Value;
        }
        string logPath=Path.Combine(local,@"VALORANT\Saved\Logs\ShooterGame.log");
        if(File.Exists(logPath)) {
            string log=ReadShared(logPath,4*1024*1024);
            var hosts=Regex.Matches(log,@"https://glz-(na|latam|br|eu|ap|kr)-1\.(na|eu|ap|kr|pbe)\.a\.pvp\.net");
            if(hosts.Count>0) { var h=hosts[hosts.Count-1]; region=h.Groups[1].Value; shard=h.Groups[2].Value; }
        }
        if(fetchCurrentVersion&&!version.StartsWith("release-",StringComparison.OrdinalIgnoreCase))version=RiotStore.CurrentClientVersion();
        if(version==""){try{using(var stream=typeof(RiotReader).Assembly.GetManifestResourceStream("version.json"))using(var rd=new StreamReader(stream))version=Json.S(Json.Get(Json.Codec.DeserializeObject(rd.ReadToEnd()),"data"),"riotClientVersion");}catch{}}
        if(region==""){try{var location=Request(origin+"/riotclient/region-locale","GET",null,true,false);string candidate=Json.S(location,"region").ToLowerInvariant();if(Regex.IsMatch(candidate,@"^(na|latam|br|eu|ap|kr)$"))region=candidate;}catch(ApiFailure){}catch(WebException){}}
        if(shard=="") shard=(region=="br"||region=="latam")?"na":region;
        if((!fetchCurrentVersion&&!Regex.IsMatch(region,@"^(na|latam|br|eu|ap|kr)$")) || !Regex.IsMatch(shard,@"^(na|eu|ap|kr|pbe)$") || version=="")
            return new Snapshot {Status="Detecting region and client version",Detail="Waiting for the client to finish loading."};
        if(!fetchCurrentVersion&&!ReadSessionAuthorization())
            return new Snapshot {Status="Waiting for Riot Client sign-in"};
        return null;
    }
    bool ReadSessionAuthorization(){
        var auth=Request(origin+"/entitlements/v1/token","GET",null,true,false);
        access=Json.S(auth,"accessToken"); entitlement=Json.S(auth,"token"); self=Json.S(auth,"subject");
        Guid id;
        return access!=""&&entitlement!=""&&Guid.TryParse(self,out id);
    }
    Snapshot PollInternal(Action<Snapshot> publishRoster) {
        var pending=InitializeSession();if(pending!=null)return pending;Guid id;
        string glz="https://glz-"+region+"-1."+shard+".a.pvp.net";
        bool pregame=!wasInMatch;
        var pointer=Request(glz+(pregame?"/pregame/v1/players/":"/core-game/v1/players/")+self,"GET",null,false,true);
        if(pointer==null){
            pregame=!pregame;
            pointer=Request(glz+(pregame?"/pregame/v1/players/":"/core-game/v1/players/")+self,"GET",null,false,true);
        }
        wasInMatch=pointer!=null&&!pregame;
        if(pointer==null){
            names.Clear();nameMatch="";
            RefreshLobbyParty(glz);
            return new Snapshot{Self=GetSelfPlayer(),Status="Connected · waiting for agent select",Detail="Refreshes automatically.",Delay=3500};
        }
        string match=Json.S(pointer,"MatchID");
        if(!Guid.TryParse(match,out id)) throw new InvalidOperationException();
        var data=Request(glz+(pregame?"/pregame/v1/matches/":"/core-game/v1/matches/")+match,"GET",null,false,true);
        if(data==null) return new Snapshot {Status="Match phase changed",Detail="Waiting for the next game update."};
        var snap=Parse(data,self,pregame);snap.Match=match;snap.Delay=3500;
        BeginMatchParties(snap);
        if(nameMatch!=match) { names.Clear(); nameMatch=match; }
        var all=snap.Allies.Concat(snap.Enemies).ToList();
        foreach(var player in all)player.Region=region;
        var missing=all.Where(p=>p.Id!="" && !names.ContainsKey(p.Id)).Select(p=>p.Id).Distinct().ToArray();
        string remoteStatus="cache",localStatus="not needed";
        if(missing.Length>0&&publishRoster!=null){
            ApplyNames(all,names);
            snap.Self=all.FirstOrDefault(p=>p.Id==self)??new Player{Id=self,Name="Your profile",State="YOU",Region=region};
            if(snap.Self.Name=="Name unavailable"&&cachedSelf!=null&&cachedSelf.Id==self)
                snap.Self.Name=cachedSelf.Name;
            snap.Detail="Loading Riot IDs…";
            publishRoster(snap.CopyRoster());
        }
        if(missing.Length>0) {
            try {
                var result=Request("https://pd."+shard+".a.pvp.net/name-service/v2/players","PUT",missing,false,false);
                remoteStatus="OK; records="+Json.A(result).Count();
                foreach(var n in Json.A(result)) {
                    string subject=Json.S(n,"Subject"),name=Json.S(n,"GameName"),tag=Json.S(n,"TagLine");
                    if(missing.Contains(subject) && name!="") names[subject]=name+(tag==""?"":"#"+tag);
                }
            } catch(ApiFailure e) { remoteStatus="HTTP "+e.Code; if(e.Code==429) snap.Delay=60000; }
              catch(WebException) { remoteStatus="connection failed"; }
              catch(ArgumentException) { remoteStatus="invalid response"; }
        }
        var unresolved=all.Where(p=>!names.ContainsKey(p.Id)).Select(p=>p.Id).Distinct().ToArray();
        if(unresolved.Length>0 && snap.Delay!=60000) {
            try {
                var result=Request(origin+"/player-account/lookup/v2/namesets-for-puuids","POST",new Dictionary<string,object>{{"puuids",unresolved}},true,false);
                var additional=LocalNames(result,unresolved);
                foreach(var entry in additional) names[entry.Key]=entry.Value;
                localStatus="OK; records="+Json.A(Json.Get(result,"namesets")).Count()+"; names="+additional.Count;
            } catch(ApiFailure e) { localStatus="HTTP "+e.Code; if(e.Code==429) snap.Delay=60000; }
              catch(WebException) { localStatus="connection failed"; }
              catch(ArgumentException) { localStatus="invalid response"; }
        } else if(unresolved.Length>0) localStatus="rate limit reached";
        int rawCount=pregame?Json.A(Json.Get(Json.Get(data,"AllyTeam"),"Players")).Count()+Json.A(Json.Get(Json.Get(data,"EnemyTeam"),"Players")).Count():Json.A(Json.Get(data,"Players")).Count();
        int resolvedCount=all.Count(p=>names.ContainsKey(p.Id));
        snap.Diagnostics="Lunar 1.60\r\nPhase: "+snap.Status+"\r\nPlayers in response: "+rawCount+"\r\nAllies: "+snap.Allies.Count+"\r\nOpponents: "+snap.Enemies.Count+"\r\nResolved names: "+resolvedCount+"/"+all.Count+"\r\nRemote service: "+remoteStatus+"\r\nLocal Riot Client: "+localStatus+"\r\nDiagnostics exclude tokens, IDs, names and raw responses.";
        ApplyNames(all,names);
        snap.Self=all.FirstOrDefault(p=>p.Id==self)??GetSelfPlayer();
        snap.Detail=(pregame?"Agent select":"Match in progress")+" · allies: "+snap.Allies.Count+" · opponents: "+snap.Enemies.Count+" · names: "+resolvedCount+"/"+all.Count;
        if(snap.Enemies.Count==0) snap.Detail+=" · opponent roster unavailable";
        if(resolvedCount<all.Count) snap.Detail+=" · some names are still loading";
        if(publishRoster!=null)publishRoster(snap.CopyRoster());
        EnrichParties(snap,glz);
        var parties=LiveMatchArt.Parties(snap.Allies.Concat(snap.Enemies));
        snap.Diagnostics+="\r\nConfirmed parties: "+parties.Count;
        return snap;
    }
    public static Dictionary<string,string> LocalNames(object result,IEnumerable<string> requested) {
        var allowed=new HashSet<string>(requested,StringComparer.OrdinalIgnoreCase);
        var output=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var record in Json.A(Json.Get(result,"namesets"))) {
            string id=Json.S(record,"puuid");
            var alias=Json.Get(record,"alias");
            string name=Json.S(alias,"gameName"),tag=Json.S(alias,"tagLine");
            if(allowed.Contains(id) && String.IsNullOrEmpty(Json.S(record,"error")) && !String.IsNullOrWhiteSpace(name)) output[id]=name+(String.IsNullOrWhiteSpace(tag)?"":"#"+tag);
        }
        return output;
    }
    public static void ApplyNames(IEnumerable<Player> players,Dictionary<string,string> resolved) {
        foreach(var p in players) {
            string name;
            p.Name=resolved.TryGetValue(p.Id,out name) && !String.IsNullOrWhiteSpace(name)?name:"Name unavailable";
        }
    }
    public static string Side(string team,bool pregame) {
        if(!pregame) return "Starting side unavailable in this phase";
        return team=="Red"?"ATTACK · estimated":team=="Blue"?"DEFENSE · estimated":"Starting side unavailable";
    }
    public static Snapshot Parse(object data,string self,bool pregame) {
        var s=new Snapshot {Status=pregame?"AGENT SELECT":"IN MATCH"};
        if(pregame) {
            object ally=Json.Get(data,"AllyTeam"),enemy=Json.Get(data,"EnemyTeam");
            // Never classify teams by array position. The user's ID must anchor a fallback.
            if(ally==null) ally=Json.A(Json.Get(data,"Teams")).FirstOrDefault(t=>Json.A(Json.Get(t,"Players")).Any(p=>Json.S(p,"Subject")==self));
            s.Team=Json.S(ally,"TeamID");
            s.Allies=Players(Json.Get(ally,"Players"),s.Team,self);
            if(enemy!=null && s.Team!="" && Json.S(enemy,"TeamID")!=s.Team) s.Enemies=Players(Json.Get(enemy,"Players"),Json.S(enemy,"TeamID"),self);
            if(enemy==null && s.Team!="") foreach(var t in Json.A(Json.Get(data,"Teams"))) {
                string team=Json.S(t,"TeamID");
                if(team!="" && team!=s.Team) s.Enemies.AddRange(Players(Json.Get(t,"Players"),team,self));
            }
        } else {
            var all=Players(Json.Get(data,"Players"),"",self);
            var me=all.FirstOrDefault(p=>p.Id==self); s.Team=me==null?"":me.Team;
            if(s.Team!="") { s.Allies=all.Where(p=>p.Team==s.Team).ToList(); s.Enemies=all.Where(p=>p.Team!=""&&p.Team!=s.Team).ToList(); }
        }
        s.Map=HistoryLogic.MapName(Json.S(data,"MapID"));
        s.Side=Side(s.Team,pregame); return s;
    }
    static List<Player> Players(object array,string team,string self) {
        var result=new List<Player>(); var seen=new HashSet<string>();
        foreach(var p in Json.A(array)) {
            string subject=Json.S(p,"Subject"); Guid id;
            if(!Guid.TryParse(subject,out id) || !seen.Add(subject) || Json.B(p,"IsCoach")) continue;
            string selection=Json.S(p,"CharacterSelectionState");
            var identity=Json.GetAnyCase(p,"PlayerIdentity","playerIdentity","player_identity")??p;
            int level;
            var hidden=Json.GetAnyCase(identity,"HideAccountLevel","hideAccountLevel","hide_account_level");
            bool hide=hidden is bool&&(bool)hidden;
            var rawLevel=Json.GetAnyCase(identity,"AccountLevel","accountLevel","account_level","Level","level");
            if(hide||!Int32.TryParse(Convert.ToString(rawLevel,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out level)||level<1||level>10000)level=0;
            result.Add(new Player {Id=subject,Name="Name unavailable",AgentId=Json.S(p,"CharacterID"),PartyId=ReadPartyId(p),CardId=Json.S(identity,"PlayerCardID"),AccountLevel=level,Team=team==""?Json.S(p,"TeamID"):team,
                State=subject==self?"YOU":selection=="locked"?"LOCKED IN":selection=="selected"?"SELECTING":"PLAYER"});
        } return result;
    }
}
static class Palette {
    public static Color Bg=Color.FromArgb(13,13,13),Panel=Color.FromArgb(19,19,19),Border=Color.FromArgb(40,40,40),Text=Color.FromArgb(236,237,239),Muted=Color.FromArgb(133,139,149),Accent=Color.FromArgb(159,120,226),Green=Color.FromArgb(88,191,150),Red=Color.FromArgb(220,125,130);
}
class TrackerSurface:Control {
    public Dictionary<string,PlayerProfile> Profiles=new Dictionary<string,PlayerProfile>();
    public event Action<Player> PlayerClicked;
    readonly List<KeyValuePair<Rectangle,Player>> targets=new List<KeyValuePair<Rectangle,Player>>();
    readonly ToolTip tip=new ToolTip();string hover="";
    public Player At(Point point){return targets.Where(t=>t.Key.Contains(point)).Select(t=>t.Value).FirstOrDefault();}
    public void ActivateFirstPlayer(){if(targets.Count==0)throw new InvalidOperationException("No player hit regions");var r=targets[0].Key;OnMouseClick(new MouseEventArgs(MouseButtons.Left,1,r.X+3,r.Y+3,0));}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);var p=At(e.Location);if(e.Button==MouseButtons.Left&&p!=null&&PlayerClicked!=null)PlayerClicked(p);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var p=At(e.Location);Cursor=p==null?Cursors.Default:Cursors.Hand;string id=p==null?"":p.Id??p.Name;if(id!=hover){
        hover=id;
        PlayerProfile profile;
        string peak=p!=null&&p.Id!=null&&Profiles.TryGetValue(p.Id,out profile)&&profile.PeakTier>=3
            ?" · Peak: "+RankArt.Name(profile.PeakTier):"";
        tip.SetToolTip(this,p==null?"":"Click to view match history for "+p.Name+peak);
    }}
    protected override void Dispose(bool disposing){if(disposing){tip.Dispose();if(card!=null)card.Dispose();}base.Dispose(disposing);}
    public Snapshot Data=new Snapshot(); public string Updated="Automatic connection";
    Image card;
    public void SetCard(byte[] data){
        if(card!=null){card.Dispose();card=null;}
        if(data!=null)using(var stream=new MemoryStream(data))card=TrackArt.Decode(stream);
        Invalidate();
    }
    public TrackerSurface(){DoubleBuffered=true;BackColor=Palette.Bg;ResizeRedraw=true;}
    void T(Graphics g,string text,float size,Color color,Rectangle r,bool bold=false){using(var f=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular))TextRenderer.DrawText(g,text,f,r,color,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);}
    System.Drawing.Drawing2D.GraphicsPath Shape(Rectangle r,int radius){var p=new System.Drawing.Drawing2D.GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    void Box(Graphics g,Rectangle r,Color fill,int radius=12){using(var p=Shape(r,radius))using(var b=new SolidBrush(fill))using(var pen=new Pen(Palette.Border)){g.FillPath(b,p);g.DrawPath(pen,p);}}
    void L(Graphics g,int x,int y,int w){using(var pen=new Pen(Palette.Border))g.DrawLine(pen,x,y,x+w,y);}
    void Badge(Graphics g,string text,Rectangle r,Color color){using(var p=Shape(r,5))using(var b=new SolidBrush(Color.FromArgb(35,color)))g.FillPath(b,p);T(g,text,8,color,r,true);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);targets.Clear();var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(Data.Self==null){
            HomeWelcome.Draw(g,ClientRectangle,LunarGlyph.Live,"LIVE MATCH","Your live board loads automatically when VALORANT is running.");
            return;
        }
        int m=24,pw=236,x=m+pw+16,w=Width-x-m;
        var me=Data.Self;
        var parties=LiveMatchArt.RosterParties(Data,Profiles,DateTime.Now);
        Box(g,new Rectangle(m,24,pw,300),Palette.Panel);
        var banner=new Rectangle(m+1,25,pw-2,108);
        var state=g.Save();
        using(var shape=Shape(banner,11)){
            g.SetClip(shape,System.Drawing.Drawing2D.CombineMode.Intersect);
            using(var fill=new System.Drawing.Drawing2D.LinearGradientBrush(banner,Color.FromArgb(45,28,67),Color.FromArgb(19,16,29),15f))g.FillPath(fill,shape);
            if(card!=null){
                TrackArt.Cover(g,card,banner);
                using(var shade=new SolidBrush(Color.FromArgb(66,15,10,23)))g.FillRectangle(shade,banner);
            }
        }
        g.Restore(state);
        Box(g,new Rectangle(m+18,91,48,48),Color.FromArgb(44,31,65),10);
        AgentArt.Draw(g,me.AgentId,new Rectangle(m+20,93,44,44));
        string nick=me==null?"Waiting for player":me.Name;T(g,nick,11,Palette.Text,new Rectangle(m+17,153,pw-30,28),true);
        PlayerProfile mine=null;if(me!=null&&me.Id!=null)Profiles.TryGetValue(me.Id,out mine);
        RankArt.Draw(g,mine==null?-1:mine.Tier,new Rectangle(m+18,184,22,22));T(g,mine==null?"Loading rank...":mine.RankLabel,8,Palette.Muted,new Rectangle(m+47,183,pw-60,24));
        RankArt.Draw(g,mine==null?-1:mine.PeakTier,new Rectangle(m+18,210,22,22));
        T(g,mine==null||mine.PeakTier<3?"Peak rank unavailable":"Peak · "+RankArt.Name(mine.PeakTier),
            8,mine==null||mine.PeakTier<3?Palette.Muted:RankArt.ColorFor(mine.PeakTier),new Rectangle(m+47,209,pw-60,24));
        L(g,m+18,239,pw-36);
        T(g,"STARTING SIDE",8,Palette.Muted,new Rectangle(m+18,244,pw-36,20));
        string side=LiveMatchArt.StartingSide(Data);
        Color sideColor=LiveMatchArt.SideColor(side);
        T(g,side,15,sideColor,new Rectangle(m+18,263,pw-36,29),true);
        LiveMatchArt.Recent(g,mine,m+18,298);
        int ph=Height-348-24;Box(g,new Rectangle(m,340,pw,ph),Palette.Panel);T(g,"Your session",10,Palette.Text,new Rectangle(m+18,352,pw-36,28),true);L(g,m,394,pw);
        T(g,"●",20,Palette.Accent,new Rectangle(m+96,419,45,40));T(g,Data.Status=="IN MATCH"?"Match in progress":Data.Status=="AGENT SELECT"?"Agent select":"Waiting for connection",10,Palette.Text,new Rectangle(m+18,469,pw-36,30),true);
        T(g,"Click a player to view",9,Palette.Muted,new Rectangle(m+18,507,pw-36,24));T(g,"their match history.",9,Palette.Muted,new Rectangle(m+18,531,pw-36,24));T(g,"Today: wins and losses",9,Palette.Muted,new Rectangle(m+18,569,pw-36,24));
        T(g,Updated,8,Palette.Muted,new Rectangle(m+18,Height-65,pw-36,24));
        Box(g,new Rectangle(x,24,w,Height-48),Palette.Panel);
        T(g,Data.Map==""||Data.Map=="Map unavailable"?"Live Match":Data.Map+"  ·  Live Match",12,Palette.Text,new Rectangle(x+18,34,w-210,32),true);
        Badge(g,"  "+(Data.Status=="IN MATCH"?"IN MATCH":Data.Status=="AGENT SELECT"?"AGENT SELECT":"WAITING"),new Rectangle(x+w-150,40,130,22),Palette.Muted);
        L(g,x,79,w);int sectionH=(Height-128)/2;
        Team(g,new Rectangle(x,80,w,sectionH),"Your team",Data.Allies,side,sideColor,parties);
        string enemySide=side=="Defense"?"Attack":side=="Attack"?"Defense":"";
        Team(g,new Rectangle(x,80+sectionH,w,sectionH),"Enemy team",Data.Enemies,enemySide,side=="Defense"?Palette.Red:Color.FromArgb(94,158,215),parties);
    }
    void Team(Graphics g,Rectangle r,string title,List<Player> players,string side,Color color,Dictionary<string,Color> parties){
        T(g,title,10,Palette.Text,new Rectangle(r.X+18,r.Y+5,112,30),true);
        if(side!=""&&side!="Waiting")Badge(g,"  "+side.ToUpperInvariant(),new Rectangle(r.X+131,r.Y+11,73,19),color);
        T(g,players.Count+" players",8,Palette.Muted,new Rectangle(r.Right-96,r.Y+7,83,25));L(g,r.X,r.Y+40,r.Width);
        bool showPeakName=r.Width>=690;
        int peakX=r.Right-(showPeakName?307:238),rankX=peakX-145;
        T(g,"PLAYER",7,Palette.Muted,new Rectangle(r.X+18,r.Y+43,110,22));
        T(g,"RANK",7,Palette.Muted,new Rectangle(rankX,r.Y+43,100,22));
        T(g,"PEAK",7,Palette.Muted,new Rectangle(peakX,r.Y+43,showPeakName?103:43,22));T(g,"RECENT FORM",7,Palette.Muted,new Rectangle(r.Right-191,r.Y+43,100,22));T(g,"TODAY  W–L",7,Palette.Muted,new Rectangle(r.Right-88,r.Y+43,76,22));L(g,r.X,r.Y+68,r.Width);
        if(players.Count==0){T(g,"Waiting for players...",10,Palette.Muted,new Rectangle(r.X+30,r.Y+110,r.Width-60,42));return;}
        int rowH=Math.Min(49,(r.Height-72)/Math.Max(5,players.Count));
        for(int i=0;i<players.Count;i++){
            var p=players[i];int y=r.Y+70+i*rowH;bool me=p.State=="YOU";
            targets.Add(new KeyValuePair<Rectangle,Player>(new Rectangle(r.X+1,y,r.Width-2,rowH),p));
            if(me){using(var b=new SolidBrush(Color.FromArgb(27,26,30)))g.FillRectangle(b,r.X+1,y,r.Width-2,rowH);using(var b=new SolidBrush(Palette.Accent))g.FillRectangle(b,r.X+1,y,2,rowH);}
            Box(g,new Rectangle(r.X+17,y+6,30,30),Color.FromArgb(32,33,37),6);
            string name=p.Name??"Name unavailable";
            AgentArt.Draw(g,p.AgentId,new Rectangle(r.X+18,y+7,28,28));
            LiveMatchArt.Stripe(g,parties,p,new Rectangle(r.X+11,y+7,3,28));
            T(g,name,9,Palette.Text,new Rectangle(r.X+58,y+1,Math.Max(20,rankX-r.X-69),23),true);
            string agent=Catalog.Agent(p.AgentId);
            if(agent=="Agent unavailable")agent=Data.Status=="AGENT SELECT"?"Selecting agent":"Agent unavailable";
            int labelWidth=Math.Max(20,rankX-r.X-69);
            T(g,(me?"You · ":"")+agent,8,me?Palette.Accent:Palette.Muted,
                new Rectangle(r.X+58,y+22,labelWidth,18));
            PlayerProfile profile=null;if(p.Id!=null)Profiles.TryGetValue(p.Id,out profile);
            bool fresh=profile!=null&&profile.Day==DateTime.Today;
            RankArt.Draw(g,profile==null?-1:profile.Tier,new Rectangle(rankX,y+10,25,25));
            T(g,profile==null?"...":RankArt.Name(profile.Tier)+(profile.Tier>=3&&!profile.RankIsCurrent?"*":""),8,profile==null?Palette.Muted:RankArt.ColorFor(profile.Tier),new Rectangle(rankX+31,y+7,118,24));
            int peak=profile==null?-1:profile.PeakTier;
            if(peak>=3){
                RankArt.Draw(g,peak,new Rectangle(peakX,y+10,25,25));
                if(showPeakName)T(g,RankArt.Name(peak),8,RankArt.ColorFor(peak),new Rectangle(peakX+29,y+7,82,24));
            }else T(g,"—",9,Palette.Muted,new Rectangle(peakX+5,y+7,25,26));
            LiveMatchArt.Recent(g,profile,r.Right-190,y+14);
            T(g,!fresh?"...":profile.Record,11,!fresh?Palette.Muted:profile.RecordColor,new Rectangle(r.Right-86,y+7,75,30),true);
            if(i<players.Count-1)L(g,r.X+17,y+rowH,r.Width-34);
        }
        L(g,r.X,r.Bottom,r.Width);
    }
}

class LunarForm:Form {
    const int ImmersiveDarkMode=20, LegacyImmersiveDarkMode=19, CaptionColor=35, CaptionTextColor=36;

    [DllImport("dwmapi.dll",ExactSpelling=true)]
    static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int valueSize);

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        if(SystemInformation.HighContrast)return;

        int enabled=1;
        if(DwmSetWindowAttribute(Handle,ImmersiveDarkMode,ref enabled,sizeof(int))!=0)
            DwmSetWindowAttribute(Handle,LegacyImmersiveDarkMode,ref enabled,sizeof(int));

        int caption=ColorTranslator.ToWin32(Color.FromArgb(14,10,22));
        int text=ColorTranslator.ToWin32(Palette.Text);
        DwmSetWindowAttribute(Handle,CaptionColor,ref caption,sizeof(int));
        DwmSetWindowAttribute(Handle,CaptionTextColor,ref text,sizeof(int));
    }
}

class Window:LunarForm,IMessageFilter {
    HistoryView home;SearchView tracker;StoreView storeView;FriendsView friendsView;CrosshairView crosshairView;
    NavButton trackerNav,storeNav,homeNav,liveNav,friendsNav,crosshairNav;
    DesktopIntegration desktop;
    const int ParallelLiveProfiles=10;
    readonly HashSet<string> statsLoading=new HashSet<string>();
    readonly HashSet<string> artLoading=new HashSet<string>();
    readonly Dictionary<string,byte[]> cardCache=new Dictionary<string,byte[]>();
    readonly Timer statsTimer=new Timer();readonly System.Threading.CancellationTokenSource statsCancel=new System.Threading.CancellationTokenSource();
    readonly Dictionary<string,PlayerProfile> profiles=new Dictionary<string,PlayerProfile>();
    readonly Dictionary<string,PlayerProfile> liveProfiles=new Dictionary<string,PlayerProfile>();
    readonly Timer liveBatchTimer=new Timer{Interval=550};
    readonly Dictionary<string,PlayerProfile> liveRanks=new Dictionary<string,PlayerProfile>();
    Snapshot current=new Snapshot();Player selected;Panel profilePage;HistoryView historyView;
    readonly List<KeyValuePair<Control,NavButton>> previousPages=new List<KeyValuePair<Control,NavButton>>();
    KeyValuePair<Control,NavButton> activePage;
    bool isPreview;DateTime statsHold=DateTime.MinValue;
    TrackerSurface surface;bool busy;
    Panel updateNotice;
    Action restartForUpdate;
    string diagnostics="Waiting for data.";
    readonly Timer timer=new Timer();readonly BackgroundWorker worker=new BackgroundWorker();readonly RiotReader reader=new RiotReader();
    public Window(bool preview=false){
        isPreview=preview;
        Icon=Icon.ExtractAssociatedIcon(typeof(Window).Assembly.Location);Text="Lunar";ClientSize=new Size(1220,790);MinimumSize=new Size(1060,750);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Palette.Bg;ForeColor=Palette.Text;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        var sidebar=new Panel{Dock=DockStyle.Left,Width=198,BackColor=Color.FromArgb(14,10,22),Padding=new Padding(18)};
        sidebar.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Palette.Border))e.Graphics.DrawLine(pen,sidebar.Width-1,0,sidebar.Width-1,sidebar.Height);};
        var mark=new PictureBox{Image=TrackArt.Asset("lunar.png"),Location=new Point(17,21),Size=new Size(44,44),SizeMode=PictureBoxSizeMode.Zoom};
        mark.Disposed+=delegate{if(mark.Image!=null)mark.Image.Dispose();};
        sidebar.Controls.Add(mark);
        var brand=new Label{Text="Lunar",Location=new Point(68,20),Size=new Size(116,30),Font=new Font("Segoe UI",16,FontStyle.Bold),ForeColor=Palette.Text};sidebar.Controls.Add(brand);
        sidebar.Controls.Add(new Label{Text="VALORANT TRACKER",Location=new Point(70,51),Size=new Size(120,16),Font=new Font("Segoe UI",7),ForeColor=Palette.Muted});
        var brandDivider=new Panel{
            Location=new Point(13,83),Size=new Size(172,1),BackColor=Color.FromArgb(51,40,66),
            Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,TabStop=false
        };
        sidebar.Controls.Add(brandDivider);
        homeNav=Navigation("Home",LunarGlyph.Home,111);homeNav.Click+=delegate{SwitchPage(true);};sidebar.Controls.Add(homeNav);
        liveNav=Navigation("Live Match",LunarGlyph.Live,158);liveNav.Click+=delegate{SwitchPage(false);};sidebar.Controls.Add(liveNav);
        friendsNav=Navigation("Friends",LunarGlyph.Friends,205);friendsNav.Click+=delegate{ShowFriends();};sidebar.Controls.Add(friendsNav);
        trackerNav=Navigation("Tracker",LunarGlyph.Search,252);trackerNav.Click+=delegate{ShowTracker();};sidebar.Controls.Add(trackerNav);
        storeNav=Navigation("Daily Store",LunarGlyph.Shop,299);storeNav.Click+=delegate{ShowStore();};sidebar.Controls.Add(storeNav);
        crosshairNav=Navigation("Crosshair",LunarGlyph.Crosshair,346);crosshairNav.Click+=delegate{ShowCrosshair();};sidebar.Controls.Add(crosshairNav);
        var bottom=new Panel{Dock=DockStyle.Bottom,Height=110};
        bottom.Controls.Add(new Label{Text="LUNAR   /   1.60",Dock=DockStyle.Bottom,Height=26,ForeColor=Palette.Muted,Font=new Font("Segoe UI",8)});sidebar.Controls.Add(bottom);
        surface=new TrackerSurface{Dock=DockStyle.Fill,Profiles=liveProfiles};surface.PlayerClicked+=OpenHistory;
        home=new HistoryView{Dock=DockStyle.Fill,HomeMode=true};
        home.OpenPlayer+=OpenHistory;
        var content=new Panel{Dock=DockStyle.Fill};
        tracker=new SearchView{Dock=DockStyle.Fill};
        tracker.OpenPlayer+=OpenHistory;
        tracker.FindKnownPlayer=FindTrackerPlayer;
        tracker.PreferredRegion=()=>current.Self==null?null:current.Self.Region;
        tracker.OpenResolvedPlayer+=OpenTrackerHistory;
        storeView=new StoreView(preview){Dock=DockStyle.Fill};
        friendsView=new FriendsView(preview){Dock=DockStyle.Fill};
        crosshairView=new CrosshairView{Dock=DockStyle.Fill};
        friendsView.OpenPlayer+=OpenHistory;
        var pages=new Control[]{storeView,tracker,surface,friendsView,crosshairView,home};
        foreach(var page in pages)page.Visible=false;
        content.Controls.AddRange(pages);
        Controls.Add(content);
        Controls.Add(sidebar);
        updateNotice=BuildUpdateNotice();
        Controls.Add(updateNotice);
        SwitchPage(true);
        var discord=new DiscordToggle{Checked=true,Location=new Point(0,36),Size=new Size(162,37)};
        discord.Hint.SetBounds(0,1,162,30);
        bottom.Controls.Add(discord.Hint);
        bottom.Controls.Add(discord);
        if(!preview)desktop=new DesktopIntegration(this,liveProfiles,discord,OpenHistory);
        liveBatchTimer.Tick+=delegate{PublishLiveProfiles();};
        statsTimer.Interval=1500;statsTimer.Tick+=delegate{ScheduleStats();};if(!preview)statsTimer.Start();
        worker.WorkerReportsProgress=true;
        worker.DoWork+=delegate(object sender,DoWorkEventArgs e){e.Result=reader.Poll(snapshot=>worker.ReportProgress(0,snapshot));};
        worker.ProgressChanged+=delegate(object sender,ProgressChangedEventArgs e){
            if(IsDisposed||Disposing)return;
            var roster=(Snapshot)e.UserState;
            bool changed=current.Match!=roster.Match||current.Status!=roster.Status;
            int visibleNames=current.Allies.Concat(current.Enemies).Count(player=>HenrikClient.HasRiotId(player.Name));
            int fetchedNames=roster.Allies.Concat(roster.Enemies).Count(player=>HenrikClient.HasRiotId(player.Name));
            if(changed||fetchedNames>visibleNames)ShowSnapshot(roster);
        };
        worker.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){busy=false;if(IsDisposed||Disposing)return;var s=e.Error==null?(Snapshot)e.Result:new Snapshot{Status="Temporary connection issue",Delay=15000};ShowSnapshot(s);timer.Interval=s.Delay;timer.Start();};
        timer.Interval=5000;timer.Tick+=delegate{RefreshData();};if(!preview)Shown+=delegate{if(!HenrikKey.IsConfigured)HenrikKey.Prompt(this);RefreshData();};FormClosed+=delegate{if(desktop!=null)desktop.Dispose();timer.Stop();timer.Dispose();statsTimer.Stop();statsTimer.Dispose();liveBatchTimer.Stop();liveBatchTimer.Dispose();statsCancel.Cancel();};
    }
    Panel BuildUpdateNotice(){
        var panel=new Panel{Size=new Size(326,76),Anchor=AnchorStyles.Right|AnchorStyles.Bottom,
            BackColor=Color.FromArgb(24,20,33),Visible=false,TabStop=false};
        panel.Location=new Point(ClientSize.Width-panel.Width-18,ClientSize.Height-panel.Height-18);
        panel.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Color.FromArgb(62,51,78)))e.Graphics.DrawRectangle(pen,0,0,panel.Width-1,panel.Height-1);};
        panel.Controls.Add(new Label{Text="Update ready",Location=new Point(16,13),Size=new Size(160,22),
            ForeColor=Palette.Text,Font=new Font("Segoe UI",10,FontStyle.Bold)});
        panel.Controls.Add(new Label{Text="Restart Lunar to install it",Location=new Point(16,38),Size=new Size(185,19),
            ForeColor=Palette.Muted,Font=new Font("Segoe UI",8)});
        var restart=new Button{Text="Restart",Location=new Point(229,20),Size=new Size(82,36),
            FlatStyle=FlatStyle.Flat,BackColor=Palette.Accent,ForeColor=Color.White,Font=new Font("Segoe UI",9,FontStyle.Bold),Cursor=Cursors.Hand};
        restart.FlatAppearance.BorderSize=0;
        restart.Click+=delegate{if(restartForUpdate!=null)restartForUpdate();};
        panel.Controls.Add(restart);
        return panel;
    }
    internal void ShowUpdateReady(Action restart){
        restartForUpdate=restart;
        if(updateNotice==null||IsDisposed)return;
        updateNotice.Location=new Point(Math.Max(12,ClientSize.Width-updateNotice.Width-18),Math.Max(12,ClientSize.Height-updateNotice.Height-18));
        updateNotice.Visible=true;
        updateNotice.BringToFront();
    }
    internal void HideUpdateReady(){if(updateNotice!=null)updateNotice.Visible=false;restartForUpdate=null;}
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        Application.AddMessageFilter(this);
    }
    protected override void OnHandleDestroyed(EventArgs e){
        Application.RemoveMessageFilter(this);
        base.OnHandleDestroyed(e);
    }
    internal static bool IsMouseBack(Message message){
        const int XButtonUp=0x020C,AppCommand=0x0319,BrowserBack=1;
        return message.Msg==XButtonUp&&((message.WParam.ToInt64()>>16)&0xffff)==1||
            message.Msg==AppCommand&&((message.LParam.ToInt64()>>16)&0x0fff)==BrowserBack;
    }
    bool IMessageFilter.PreFilterMessage(ref Message message){
        if(!IsMouseBack(message))return false;
        var control=Control.FromChildHandle(message.HWnd);
        return control!=null&&control.FindForm()==this&&GoBack();
    }
    protected override void WndProc(ref Message message){
        if(message.Msg==0x0319&&IsMouseBack(message)&&GoBack()){message.Result=new IntPtr(1);return;}
        if(desktop!=null)desktop.HandleMessage(message);
        base.WndProc(ref message);
    }
    NavButton Navigation(string title,LunarGlyph glyph,int top){
        return new NavButton(title,glyph){Location=new Point(12,top),Size=new Size(174,42)};
    }
    void MarkPage(NavButton active){
        foreach(var button in new[]{homeNav,liveNav,friendsNav,trackerNav,storeNav,crosshairNav})button.Active=button==active;
    }
    void ShowInfo(string title,string text,bool copyable){
        using(var dialog=new Form{Text=title,Size=new Size(640,440),StartPosition=FormStartPosition.CenterParent,BackColor=Palette.Panel,ForeColor=Palette.Text,Padding=new Padding(22)}){
            var box=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,Text=text,ScrollBars=ScrollBars.None,BackColor=Palette.Panel,ForeColor=Palette.Text,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",11)};
            dialog.Controls.Add(box);if(copyable){var copy=new SoftButton{Text="Copy diagnostics",Dock=DockStyle.Bottom,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Palette.Panel,ForeColor=Palette.Text};copy.Click+=delegate{try{Clipboard.SetText(text);copy.Text="Copied";}catch(System.Runtime.InteropServices.ExternalException){copy.Text="Try again";}};dialog.Controls.Add(copy);}dialog.ShowDialog(this);
        }
    }
    void RefreshData(){if(busy)return;timer.Stop();busy=true;worker.RunWorkerAsync();}
    void ShowSnapshot(Snapshot s){
        var previousSelf=current.Self;
        bool newRoster=current.Match!=s.Match;
        bool finished=current.Status=="IN MATCH"&&s.Self!=null&&s.Match=="";
        current=s;
        if(newRoster)liveRanks.Clear();
        if(newRoster||isPreview)PublishLiveProfiles();
        if(finished){
            PlayerProfile cached;
            if(profiles.TryGetValue(s.Self.Id,out cached))cached.RetryAfter=DateTime.MinValue;
        }
        home.Player=s.Self;
        PlayerProfile own=null;
        if(s.Self!=null)profiles.TryGetValue(s.Self.Id,out own);
        if(!Object.ReferenceEquals(home.Profile,own)||!String.Equals(previousSelf==null?null:previousSelf.Name,s.Self==null?null:s.Self.Name))
            home.UpdateProfile(own);
        if(profiles.Count>60){var active=new HashSet<string>(s.Allies.Concat(s.Enemies).Select(p=>p.Id));foreach(var key in profiles.Keys.Where(k=>!active.Contains(k)&&(selected==null||selected.Id!=k)).ToList())profiles.Remove(key);}
        diagnostics=s.Diagnostics+"\r\nStatus: "+s.Status+"\r\n"+s.Detail;surface.Data=s;surface.Updated="Updated at "+DateTime.Now.ToString("HH:mm:ss");surface.Invalidate();if(desktop!=null)desktop.Update(s);RefreshLiveCard();ScheduleStats();
        if(!isPreview)QueueLiveProfiles();
    }
    void ScheduleStats(){
        if(isPreview||statsCancel.IsCancellationRequested)return;
        var focusPlayer=profilePage!=null&&profilePage.Visible?selected:null;
        if(focusPlayer!=null&&!String.IsNullOrEmpty(focusPlayer.Id)&&HenrikClient.HasRiotId(focusPlayer.Name)&&
            !statsLoading.Contains(focusPlayer.Id)&&(!profiles.ContainsKey(focusPlayer.Id)||
                profiles[focusPlayer.Id].Day!=DateTime.Today||profiles[focusPlayer.Id].RetryAfter<=DateTime.UtcNow)&&
            DateTime.UtcNow>=HenrikClient.RemoteRetryAt)
            LoadStats(focusPlayer,null,true);
        if(DateTime.UtcNow<statsHold)return;
        var priority=new[]{profilePage!=null&&profilePage.Visible?selected:null,current.Self};
        var candidates=priority.Concat(AlternateTeams(current.Allies,current.Enemies))
            .Where(p=>p!=null&&!String.IsNullOrEmpty(p.Id)&&HenrikClient.HasRiotId(p.Name))
            .GroupBy(p=>p.Id).Select(group=>group.First()).ToList();
        Func<Player,bool> due=p=>!statsLoading.Contains(p.Id)&&(!profiles.ContainsKey(p.Id)||
            profiles[p.Id].Day!=DateTime.Today||profiles[p.Id].RetryAfter<=DateTime.UtcNow);
        foreach(var player in candidates.Where(p=>priority.Any(focus=>focus!=null&&focus.Id==p.Id)).Where(due)){
            if(statsLoading.Count>=ParallelLiveProfiles+1)break;
            LoadStats(player);
        }
        foreach(var player in candidates.Where(due).OrderBy(p=>profiles.ContainsKey(p.Id))){
            if(statsLoading.Count>=ParallelLiveProfiles)break;
            LoadStats(player);
        }
    }
    internal static IEnumerable<Player> AlternateTeams(IList<Player> allies,IList<Player> enemies){
        for(int i=0;i<Math.Max(allies.Count,enemies.Count);i++){
            if(i<enemies.Count)yield return enemies[i];
            if(i<allies.Count)yield return allies[i];
        }
    }
    async void LoadStats(Player player,Task<PlayerProfile> pending=null,bool interactive=false){
        if(!statsLoading.Add(player.Id))return;
        try{
            if(pending==null){
                pending=HenrikClient.LoadLiveAsync(player,statsCancel.Token,new Progress<PlayerProfile>(rank=>{
                    if(IsDisposed||Disposing||statsCancel.IsCancellationRequested||profiles.ContainsKey(rank.Id))return;
                    if(selected!=null&&selected.Id==rank.Id&&historyView!=null&&!historyView.IsDisposed&&historyView.Profile==null)
                        historyView.UpdateProfile(rank);
                    if(!LiveRoster().Any(member=>member.Id==rank.Id))return;
                    liveRanks[rank.Id]=rank;
                    QueueLiveProfiles();
                }),!interactive);
            }
            var profile=await pending;
            if(IsDisposed||Disposing||statsCancel.IsCancellationRequested)return;
            PlayerProfile previous;
            profiles.TryGetValue(profile.Id,out previous);
            if(profile.Limited){
                if(profile.RetryAfter>statsHold)statsHold=profile.RetryAfter;
                if(previous!=null&&previous.Day==DateTime.Today&&previous.HistoryAvailable&&previous.RankIsCurrent
                    &&(!profile.HistoryAvailable||!profile.RankIsCurrent)){
                    previous.RetryAfter=statsHold;
                    return;
                }
            }
            byte[] picture;
            if(!String.IsNullOrEmpty(profile.CardId)&&cardCache.TryGetValue(profile.CardId,out picture))
                profile.CardArt=picture;
            profiles[profile.Id]=profile;
            RenderProfile(profile);
            if(WantsCard(profile.Id))LoadProfileArt(profile);
        }catch(OperationCanceledException){}
         catch(Exception){
            if(!IsDisposed&&!statsCancel.IsCancellationRequested){
                PlayerProfile cached;
                if(profiles.TryGetValue(player.Id,out cached))cached.RetryAfter=DateTime.UtcNow.AddMinutes(2);
                else{
                    var unavailable=new PlayerProfile{Id=player.Id,Name=player.Name,Message="Profile temporarily unavailable. Retrying automatically."};
                    profiles[player.Id]=unavailable;
                    RenderProfile(unavailable);
                }
            }
        }finally{
            statsLoading.Remove(player.Id);
            liveRanks.Remove(player.Id);
            ScheduleStats();
            if(!IsDisposed&&!Disposing&&!statsCancel.IsCancellationRequested)QueueLiveProfiles();
        }
    }
    IEnumerable<Player> LiveRoster(){
        return current.Allies.Concat(current.Enemies).Where(player=>!String.IsNullOrEmpty(player.Id));
    }
    PlayerProfile LiveProfile(string id){
        PlayerProfile profile;
        return profiles.TryGetValue(id,out profile)||liveRanks.TryGetValue(id,out profile)?profile:null;
    }
    void QueueLiveProfiles(){
        var roster=LiveRoster().ToArray();
        if(roster.Length==0)return;
        bool changed=roster.Any(player=>{
            var newest=LiveProfile(player.Id);
            PlayerProfile displayed;
            liveProfiles.TryGetValue(player.Id,out displayed);
            return !Object.ReferenceEquals(newest,displayed);
        });
        if(!changed)return;
        bool stillLoading=roster.Any(player=>HenrikClient.HasRiotId(player.Name)&&
            (statsLoading.Contains(player.Id)||!profiles.ContainsKey(player.Id)));
        if(!stillLoading)PublishLiveProfiles();
        else if(!liveBatchTimer.Enabled)liveBatchTimer.Start();
    }
    void PublishLiveProfiles(){
        liveBatchTimer.Stop();
        liveProfiles.Clear();
        foreach(var player in LiveRoster()){
            var profile=LiveProfile(player.Id);
            if(profile!=null)liveProfiles[player.Id]=profile;
        }
        surface.Invalidate();
        if(desktop!=null)desktop.RefreshStats();
    }
    void RenderProfile(PlayerProfile profile){
        if(current.Self!=null&&current.Self.Id==profile.Id){
            RefreshLiveCard();
            home.Player=current.Self;
            home.UpdateProfile(profile);
        }
        if(selected!=null&&selected.Id==profile.Id&&historyView!=null&&!historyView.IsDisposed)
            historyView.UpdateProfile(profile);
    }
    string liveCardId="";
    async void RefreshLiveCard(){
        var player=current.Self;
        if(isPreview)return;
        string card=player==null?"":player.CardId??"";
        if(card==""){
            PlayerProfile profile;
            if(player!=null&&player.Id!=null&&profiles.TryGetValue(player.Id,out profile))card=profile.CardId??"";
        }
        if(liveCardId==card)return;
        liveCardId=card;
        SetLiveCard(null);
        if(card=="")return;
        try{
            byte[] picture;
            if(!cardCache.TryGetValue(card,out picture)){
                picture=await Task.Run(()=>HenrikClient.LoadCard(card),statsCancel.Token);
                if(picture!=null&&!IsDisposed&&!statsCancel.IsCancellationRequested){
                    if(cardCache.Count>=32)cardCache.Clear();
                    cardCache[card]=picture;
                }
            }
            if(!IsDisposed&&!statsCancel.IsCancellationRequested&&liveCardId==card){
                if(picture!=null)SetLiveCard(picture);
                else liveCardId=null;
            }
        }catch(OperationCanceledException){}
    }
    void SetLiveCard(byte[] picture){
        surface.SetCard(picture);

    }
    bool WantsCard(string id){
        return (home.Visible&&current.Self!=null&&current.Self.Id==id)
            ||(profilePage!=null&&profilePage.Visible&&selected!=null&&selected.Id==id);
    }
    async void LoadProfileArt(PlayerProfile profile){
        string card=profile.CardId;
        if(isPreview||String.IsNullOrEmpty(card))return;
        byte[] cachedArt;
        if(cardCache.TryGetValue(card,out cachedArt)){
            if(profile.CardArt!=cachedArt){
                profile.CardArt=cachedArt;
                RenderProfile(profile);
            }
            return;
        }
        if(!artLoading.Add(card))return;
        try{
            byte[] picture=await Task.Run(()=>HenrikClient.LoadCard(card),statsCancel.Token);
            if(picture==null||IsDisposed||statsCancel.IsCancellationRequested)return;
            if(cardCache.Count>=32)cardCache.Clear();
            cardCache[card]=picture;
            foreach(var cached in profiles.Values.Where(p=>p.CardId==card&&WantsCard(p.Id)).ToArray()){
                cached.CardArt=picture;
                RenderProfile(cached);
            }
        }catch(OperationCanceledException){}
         catch(Exception){}
        finally{artLoading.Remove(card);}
    }
    Player FindTrackerPlayer(string riotId){
        var player=new[]{current.Self}.Concat(LiveRoster()).FirstOrDefault(entry=>entry!=null&&
            String.Equals(entry.Name,riotId,StringComparison.OrdinalIgnoreCase));
        if(player!=null)return player;
        var cached=profiles.Values.FirstOrDefault(entry=>entry.Day==DateTime.Today&&
            String.Equals(entry.Name,riotId,StringComparison.OrdinalIgnoreCase));
        return cached==null?null:new Player{Id=cached.Id,Name=cached.Name,CardId=cached.CardId};
    }
    async void OpenTrackerHistory(Player player,Task<PlayerProfile> pending,Task<PlayerProfile> rankPreview){
        OpenHistory(player,pending);
        var target=historyView;
        var rank=await rankPreview;
        if(rank!=null&&!IsDisposed&&!Disposing&&target==historyView&&!target.IsDisposed&&target.Profile==null)
            target.UpdateProfile(rank);
    }
    void OpenHistory(Player player){OpenHistory(player,null);}
    void OpenHistory(Player player,Task<PlayerProfile> pending){
        tracker.Remember(player);
        PlayerProfile cached;profiles.TryGetValue(player.Id??"",out cached);if(cached!=null&&cached.Day!=DateTime.Today)cached=null;
        var view=new HistoryView{Dock=DockStyle.Fill,Player=player};
        view.OpenPlayer+=OpenHistory;
        view.UpdateProfile(cached);
        var page=new Panel{Dock=DockStyle.Fill,BackColor=Palette.Bg,Visible=false,Tag=view};
        page.Controls.Add(view);
        home.Parent.Controls.Add(page);
        ShowPage(page,trackerNav);
        if(pending!=null&&(cached==null||cached.RetryAfter<=DateTime.UtcNow||cached.Limited))LoadStats(player,pending,true);
        else if(!isPreview&&cached!=null&&cached.Limited&&HenrikClient.RemoteRetryAt<=DateTime.UtcNow)
            LoadStats(player,null,true);
        ScheduleStats();
        if(cached!=null)LoadProfileArt(cached);

    }
    public void Demo(){
        var s=new Snapshot{Status="AGENT SELECT",Map="Lotus",Match="demo-select",Side="DEFENSE · estimated",Diagnostics="Preview with sample players."};
        for(int i=0;i<5;i++){s.Allies.Add(new Player{Id="demo-a"+i,Name=new[]{"Lunar#BR","Brisa#BR1","Aurora#BR2","Café#123","Domingo#BR1"}[i],State=i==0?"YOU":"LOCKED IN"});s.Enemies.Add(new Player{Id="demo-e"+i,Name=new[]{"Neblina#BR1","Horizonte#BR2","Maré#222","Intervalo#BR1","Lua#999"}[i],State="PLAYER"});}
        int n=0;foreach(var player in s.Allies.Concat(s.Enemies)){
            var p=new PlayerProfile{Id=player.Id,Name=player.Name,Tier=new[]{23,20,17,15,12}[n%5],RR=42,RankIsCurrent=true,HistoryAvailable=true,DayComplete=true,Message="PREVIEW · sample matches"};p.RankLabel=RankArt.Name(p.Tier);
            for(int j=0;j<5;j++)p.Matches.Add(new MatchRow{Id="demo-match-"+j,Map=new[]{"Ascent","Haven","Lotus","Icebox","Bind"}[j],Queue="Competitive",Started=DateTime.Now.AddHours(-j-1),Completed=true,Result=n%3==0?(j==3?Outcome.Loss:Outcome.Win):n%3==1?(j==0?Outcome.Win:Outcome.Loss):(j<2?Outcome.Win:j<4?Outcome.Loss:Outcome.Draw),Score=j==3?"9 : 13":"13 : 8",Kda="21 / 14 / 6"});
            HistoryLogic.CountDay(p,DateTime.Today);profiles[p.Id]=p;n++;
        }
        for(int i=0;i<5;i++){
            s.Allies[i].AgentId=i%2==0?"add6443a-41bd-e414-f6ad-e58d267f4e95":"320b2a48-4d9b-a075-30f1-1f93a9b638fa";
            s.Enemies[i].AgentId=i%2==0?"320b2a48-4d9b-a075-30f1-1f93a9b638fa":"add6443a-41bd-e414-f6ad-e58d267f4e95";
        }
        s.Allies[0].PartyId=s.Allies[1].PartyId="demo-party-1";
        s.Enemies[2].PartyId=s.Enemies[3].PartyId="demo-party-2";
        PopulateDemo(s);s.Self=s.Allies[0];ShowSnapshot(s);surface.Updated="PREVIEW · sample data";
    }
    public void ShowStore(){ShowPage(storeView,storeNav);}
    public void ShowFriends(){ShowPage(friendsView,friendsNav);}
    public void ShowCrosshair(){ShowPage(crosshairView,crosshairNav);}
    public void ShowTracker(){ShowPage(tracker,trackerNav);}
    void SwitchPage(bool showHome){
        if(showHome)ShowPage(home,homeNav);else ShowPage(surface,liveNav);
    }
    void ShowPage(Control page,NavButton navigation,bool record=true){
        var leaving=activePage.Key;
        if(leaving==page)return;
        if(leaving!=null){
            if(record)previousPages.Add(activePage);
            leaving.Visible=false;
        }
        activePage=new KeyValuePair<Control,NavButton>(page,navigation);
        historyView=page.Tag as HistoryView;
        profilePage=historyView==null?null:(Panel)page;
        selected=historyView==null?null:historyView.Player;
        page.Visible=true;
        page.BringToFront();
        MarkPage(navigation);
        if(page==storeView&&!isPreview)storeView.CheckOnOpen();
        if(page==home&&home.Profile!=null)LoadProfileArt(home.Profile);
        if(selected!=null&&!record){
            PlayerProfile cached;
            if(profiles.TryGetValue(selected.Id??"",out cached)&&cached.Day==DateTime.Today&&cached!=historyView.Profile)
                historyView.UpdateProfile(cached);
            ScheduleStats();
        }
        if(previousPages.Count>24){
            var oldest=previousPages[0].Key;
            previousPages.RemoveAt(0);
            ReleaseProfilePage(oldest);
        }
    }
    void ReleaseProfilePage(Control page){
        if(page.Tag is HistoryView&&page!=activePage.Key&&!previousPages.Any(entry=>entry.Key==page))page.Dispose();
    }
    internal bool GoBack(){
        var profile=historyView??(activePage.Key==home?home:null);
        if(profile!=null&&profile.BackFromScoreboard())return true;
        if(previousPages.Count==0)return false;
        var leaving=activePage.Key;
        var previous=previousPages[previousPages.Count-1];
        previousPages.RemoveAt(previousPages.Count-1);
        ShowPage(previous.Key,previous.Value,false);
        ReleaseProfilePage(leaving);
        return true;
    }
    void PopulateDemo(Snapshot s){
        Catalog.Demo();foreach(var p in profiles.Values){p.PeakTier=26;int i=0;foreach(var m in p.Matches){m.AgentId=i%3==0?"8e253930-4c05-31dd-1b6c-968525494517":"add6443a-41bd-e414-f6ad-e58d267f4e95";m.SeasonId="demo-act";m.RRChange=m.Result==Outcome.Win?18:-16;m.Metrics=new Performance{HasKda=true,HasDamage=true,HasHits=true,Kills=25-i*2,Deaths=12+i,Assists=6,Rounds=21,Damage=3500-i*100,Head=30,Body=60,Leg=10};m.Kda=m.Metrics.Kills+" / "+m.Metrics.Deaths+" / 6";int j=0;foreach(var player in s.Allies.Concat(s.Enemies)){m.Roster.Add(new ScorePlayer{Id=player.Id,Name=player.Name,Team=j<5?"Blue":"Red",Tier=20,Agent=j%2==0?"add6443a-41bd-e414-f6ad-e58d267f4e95":"320b2a48-4d9b-a075-30f1-1f93a9b638fa",Stats=new Performance{HasKda=true,HasDamage=true,HasHits=true,Kills=25-j,Deaths=12+j,Assists=4,Rounds=21,Damage=3000-j*50,Head=25,Body=70,Leg=5}});j++;}i++;}}
    }
    public void RenderTab(string path,int tab){SwitchPage(true);home.ActivateTab(tab);home.Refresh();Application.DoEvents();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(path);}}
    public void RenderDetail(string path){home.RenderMatch(path);}
    public void RenderHistory(string path){SwitchPage(false);surface.Refresh();surface.ActivateFirstPlayer();Application.DoEvents();if(profilePage==null||historyView.Player.Id!=current.Allies[0].Id)throw new InvalidOperationException("Profile navigation failed");using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(path);}}
}

static class Program {
    [STAThread] static int Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        if(args.Length==1&&args[0]=="--apply-update")return LunarUpdates.Apply();
        if(args.Contains("--self-test")) {SearchChecks.Run();Tests.Run();HistoryChecks.Run();return ProfileChecks.Run();}
        if(args.Contains("--probe")) {var s=new RiotReader().Poll();Console.WriteLine(s.Status+" | "+s.Detail);return 0;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && args[0].StartsWith("--render")) {
            using(var window=new Window(true)) { if(args[0]!="--render")window.Demo();if(args[0]=="--render-small")window.Size=window.MinimumSize;window.Show();Application.DoEvents();if(args[0]=="--render-tracker"){window.ShowTracker();Application.DoEvents();using(var b=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1]);}}else if(args[0]=="--render-history")window.RenderHistory(args[1]);else if(args[0]=="--render-matches")window.RenderTab(args[1],1);else if(args[0]=="--render-agents")window.RenderTab(args[1],2);else if(args[0]=="--render-maps")window.RenderTab(args[1],3);else if(args[0]=="--render-detail")window.RenderDetail(args[1]);else using(var bitmap=new Bitmap(window.Width,window.Height)) {window.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(args[1]);}window.Close();}return 0;
        }
        Application.Run(new Window());return 0;
    }
}
static class Tests {
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    public static int Run(){
        string me="11111111-1111-1111-1111-111111111111",other="22222222-2222-2222-2222-222222222222";
        Func<string,object> parse=Json.Codec.DeserializeObject;
        var s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\"}]},\"EnemyTeam\":null}"),me,true);
        Check(s.Allies.Count==1&&s.Enemies.Count==0,"enemy null does not fabricate identities");
        Check(s.Side.Contains("estimated"),"starting side labelled as inference");
        Check(RiotReader.Side("Unknown",true).Contains("unavailable"),"unknown side is not mapped to defense");
        Check(RiotReader.Side("Red",false).Contains("unavailable"),"ingame team does not claim starting side");
        s=RiotReader.Parse(parse("{\"Teams\":[{\"TeamID\":\"Red\",\"Players\":[{\"Subject\":\""+other+"\"}]},{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\"}]}]}"),me,true);
        Check(s.Allies[0].Id==me&&s.Enemies[0].Id==other,"fallback anchors team to local player");
        RiotReader.ApplyNames(s.Enemies,new Dictionary<string,string>{{other,"Teste#BR1"}});
        Check(s.Enemies[0].Name=="Teste#BR1","missing privacy flag does not suppress returned name");
        s=RiotReader.Parse(parse("{\"Players\":[{\"Subject\":\""+me+"\",\"TeamID\":\"Red\"},{\"Subject\":\""+other+"\",\"TeamID\":\"Blue\",\"PlayerIdentity\":{\"Incognito\":true}}]}"),me,false);
        Check(s.Allies.Count==1&&s.Enemies.Count==1,"ingame grouping");
        s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\",\"PlayerIdentity\":{\"AccountLevel\":276}}]}}"),me,true);
        Check(s.Allies[0].AccountLevel==276,"account level parsed from the Riot identity");
        s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\",\"player_identity\":{\"account_level\":\"143\"}}]}}"),me,true);
        Check(s.Allies[0].AccountLevel==143,"account level parsed from alternate field casing");
        s=RiotReader.Parse(parse("{\"AllyTeam\":{\"TeamID\":\"Blue\",\"Players\":[{\"Subject\":\""+me+"\",\"PlayerIdentity\":{\"HideAccountLevel\":true,\"AccountLevel\":276}}]}}"),me,true);
        Check(s.Allies[0].AccountLevel==0,"hidden account level remains private");
        s=RiotReader.Parse(parse("{\"Players\":[{\"Subject\":\""+me+"\",\"TeamID\":\"Red\",\"playerIdentity\":{\"Level\":52}}]}"),me,false);
        Check(s.Allies[0].AccountLevel==52,"account level parsed from lowercase live response");
        s=RiotReader.Parse(parse("{\"Players\":[{\"Subject\":\""+me+"\",\"TeamID\":\"Red\"},{\"Subject\":\""+other+"\",\"TeamID\":\"Blue\",\"PlayerIdentity\":{\"Incognito\":true}}]}"),me,false);
        RiotReader.ApplyNames(s.Enemies,new Dictionary<string,string>{{other,"Teste#BR1"}});
        Check(s.Enemies[0].Name=="Teste#BR1","Incognito does not suppress returned name");
        RiotReader.ApplyNames(s.Enemies,new Dictionary<string,string>());
        Check(s.Enemies[0].Name=="Name unavailable","missing name is not fabricated or labelled hidden");
        s=RiotReader.Parse(parse("{}"),me,true);Check(s.Allies.Count==0&&s.Enemies.Count==0,"empty response produces empty state");
        var local=RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"alias\":{\"gameName\":\"Local\",\"tagLine\":\"BR1\"}}]}"),new[]{other});
        Check(local.ContainsKey(other)&&local[other]=="Local#BR1","local V2 alias resolves requested player");
        Check(RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"alias\":{\"gameName\":\"Local\"}}]}"),new[]{me}).Count==0,"unrequested identities discarded");
        Check(RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"error\":\"unavailable\",\"alias\":{\"gameName\":\"Local\"}}]}"),new[]{other}).Count==0,"local entry error not cached as a name");
        Check(RiotReader.LocalNames(parse("{\"namesets\":[{\"puuid\":\""+other+"\",\"alias\":null}]}"),new[]{other}).Count==0,"null local alias tolerated");
        Console.WriteLine("18 checks passed.");return 0;
    }
}
}
