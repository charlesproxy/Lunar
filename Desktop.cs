using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lunar {
sealed class HotkeyBinding {
    public Keys Key,Modifiers;
    public string Display{
        get{
            var parts=new List<string>();
            if((Modifiers&Keys.Control)==Keys.Control)parts.Add("Ctrl");
            if((Modifiers&Keys.Alt)==Keys.Alt)parts.Add("Alt");
            if((Modifiers&Keys.Shift)==Keys.Shift)parts.Add("Shift");
            parts.Add(Key.ToString());
            return String.Join("+",parts.ToArray());
        }
    }
}
static class HotkeyPreference {
    static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","hotkey.txt");
    static HotkeyBinding current=Load();
    public static HotkeyBinding Current{get{return new HotkeyBinding{Key=current.Key,Modifiers=current.Modifiers};}}
    static HotkeyBinding Load(){
        try{
            var values=File.ReadAllText(FilePath).Trim().Split(',');int key,modifiers;
            if(values.Length==2&&Int32.TryParse(values[0],out key)&&Int32.TryParse(values[1],out modifiers)){
                var result=new HotkeyBinding{Key=(Keys)key,Modifiers=(Keys)modifiers};
                if(Valid(result.Key,result.Modifiers))return result;
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        return new HotkeyBinding{Key=Keys.T,Modifiers=Keys.Alt};
    }
    public static bool Valid(Keys key,Keys modifiers){
        Keys allowed=Keys.Alt|Keys.Control|Keys.Shift;
        return key!=Keys.None&&Enum.IsDefined(typeof(Keys),key)&&(key&Keys.KeyCode)==key&&key!=Keys.Menu&&key!=Keys.ControlKey&&key!=Keys.ShiftKey&&
            (modifiers&~allowed)==Keys.None&&(modifiers&allowed)!=Keys.None;
    }
    public static void Save(HotkeyBinding value){
        current=new HotkeyBinding{Key=value.Key,Modifiers=value.Modifiers};
        try{Directory.CreateDirectory(Path.GetDirectoryName(FilePath));File.WriteAllText(FilePath,((int)current.Key)+","+((int)current.Modifiers));}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
partial class RiotReader {
    string partyLookupMatch="",partyLookupAccount="",ownParty="";
    Snapshot previousPartyRoster;
    DateTime nextPartyLookup,partyVerified;
    int? lastAllyScore,lastEnemyScore;
    string chatPid="";
    readonly HashSet<string> partyMembers=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> matchParties=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> ownMatchParties=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

    void ResetPartyAccount(){
        if(partyLookupAccount==self)return;
        partyLookupAccount=self;
        partyLookupMatch=ownParty="";
        previousPartyRoster=null;
        partyMembers.Clear();
        chatPid="";
        lastAllyScore=lastEnemyScore=null;
        matchParties.Clear();
        ownMatchParties.Clear();
        partyVerified=nextPartyLookup=DateTime.MinValue;
    }
    void RefreshLobbyParty(string glz){
        ResetPartyAccount();
        if(partyLookupMatch!=""){
            partyLookupMatch="";
            previousPartyRoster=null;
            lastAllyScore=lastEnemyScore=null;
            matchParties.Clear();
            ownMatchParties.Clear();
            nextPartyLookup=DateTime.MinValue;
        }
        RefreshOwnParty(glz);
    }
    void RefreshOwnParty(string glz){
        var now=DateTime.UtcNow;
        if(now<nextPartyLookup)return;
        nextPartyLookup=now.AddSeconds(6);
        try{
            var pointer=Request(glz+"/parties/v1/players/"+self,"GET",null,false,true,1800);
            string id=Json.S(pointer,"CurrentPartyID");
            if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[A-Za-z0-9_-]{12,128}$"))return;
            var party=Request(glz+"/parties/v1/parties/"+id,"GET",null,false,true,1800);
            var members=Json.A(Json.Get(party,"Members")).Select(member=>Json.S(member,"Subject"))
                .Where(member=>member!="").ToArray();
            if(!members.Contains(self,StringComparer.OrdinalIgnoreCase))return;
            ownParty=id;
            partyMembers.Clear();
            foreach(string member in members)partyMembers.Add(member);
            partyVerified=now;
        }catch(ApiFailure){}catch(IOException){}catch(System.Net.WebException){}catch(ArgumentException){}catch(InvalidOperationException){}
    }
    void BeginMatchParties(Snapshot snapshot){
        ResetPartyAccount();
        if(partyLookupMatch!=snapshot.Match){
            partyLookupMatch=snapshot.Match;
            lastAllyScore=lastEnemyScore=null;
            if(!ContinuesAgentSelect(previousPartyRoster,snapshot)){
                matchParties.Clear();
                ownMatchParties.Clear();
            }
            RememberOwnMatchParty(snapshot,TimeSpan.FromSeconds(20));
            nextPartyLookup=DateTime.MinValue;
        }
        previousPartyRoster=snapshot;
        RememberMatchParties(snapshot);
    }
    void RememberOwnMatchParty(Snapshot snapshot,TimeSpan freshness){
        if(ownParty==""||DateTime.UtcNow-partyVerified>freshness||!partyMembers.Contains(self))return;
        var partners=snapshot.Allies.Where(player=>partyMembers.Contains(player.Id)).ToArray();
        if(partners.Length<2)return;
        ownMatchParties.Clear();
        foreach(var player in partners)ownMatchParties[player.Id]=ownParty;
    }
    internal static bool ContinuesAgentSelect(Snapshot before,Snapshot after){
        return before!=null&&before.Status=="AGENT SELECT"&&after.Status=="IN MATCH"&&
            before.Map!=""&&before.Map!="Map unavailable"&&String.Equals(before.Map,after.Map,StringComparison.OrdinalIgnoreCase)&&
            before.Allies.Count>=2&&new HashSet<string>(before.Allies.Select(player=>player.Id),StringComparer.OrdinalIgnoreCase)
                .SetEquals(after.Allies.Select(player=>player.Id));
    }
    void EnrichParties(Snapshot snapshot,string glz){
        if(nextPartyLookup==DateTime.MinValue||ownParty==""||partyMembers.Count<2||
            DateTime.UtcNow-partyVerified>TimeSpan.FromMinutes(2))RefreshOwnParty(glz);
        RememberOwnMatchParty(snapshot,TimeSpan.FromSeconds(20));
        try{
            var presences=Request(origin+"/chat/v4/presences","GET",null,true,false,1400);
            ApplyPartyPresence(snapshot,presences);
            if(snapshot.Status=="IN MATCH"&&!snapshot.AllyScore.HasValue){
                if(chatPid==""){
                    var chat=Request(origin+"/chat/v1/session","GET",null,true,true,1000);
                    if(String.Equals(self,Json.S(chat,"puuid"),StringComparison.OrdinalIgnoreCase))chatPid=Json.S(chat,"pid");
                }
                if(chatPid!=""){
                    foreach(var entry in Json.A(Json.Get(presences,"presences")).Where(entry=>Json.S(entry,"pid")==chatPid))
                        ApplyDiscordScore(snapshot,RiotPresenceData(entry));
                }
            }
        }catch(ApiFailure){}catch(IOException){}catch(System.Net.WebException){}catch(ArgumentException){}catch(InvalidOperationException){}
        if(snapshot.Status=="IN MATCH"){
            if(snapshot.AllyScore.HasValue&&snapshot.EnemyScore.HasValue){
                lastAllyScore=snapshot.AllyScore;
                lastEnemyScore=snapshot.EnemyScore;
            }else{
                snapshot.AllyScore=lastAllyScore;
                snapshot.EnemyScore=lastEnemyScore;
            }
        }
        RememberMatchParties(snapshot);
    }
    void RememberMatchParties(Snapshot snapshot){
        foreach(var player in snapshot.Allies.Concat(snapshot.Enemies)){
            string confirmed;
            if(ownMatchParties.TryGetValue(player.Id,out confirmed))player.PartyId=confirmed;
            if(!String.IsNullOrEmpty(player.PartyId))matchParties[player.Id]=player.PartyId;
            else if(matchParties.ContainsKey(player.Id))player.PartyId=matchParties[player.Id];
        }
    }
    internal static string ReadPartyId(object row){
        foreach(string key in new[]{"PartyID","PartyId","partyId","party_id"}){
            string id=Json.S(row,key).Trim();
            if(id!=""&&id!="00000000-0000-0000-0000-000000000000")return id;
        }
        return "";
    }
    internal static object RiotPresenceData(object entry){
        var value=Json.Get(entry,"private");
        if(value is System.Collections.Generic.Dictionary<string,object>)return value;
        var encoded=value as string;
        if(String.IsNullOrEmpty(encoded)||encoded.Length>32768)return null;
        try{
            string json=encoded.StartsWith("{",StringComparison.Ordinal)?encoded:Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return Json.Codec.DeserializeObject(json);
        }catch(FormatException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
    }
    internal static void ApplyPartyPresence(Snapshot snapshot,object payload){
        if(String.IsNullOrEmpty(snapshot.Match))return;
        var players=snapshot.Allies.Concat(snapshot.Enemies).GroupBy(p=>p.Id,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>group.First(),StringComparer.OrdinalIgnoreCase);
        foreach(var entry in Json.A(Json.Get(payload,"presences"))){
            string product=Json.S(entry,"product");
            if(product!=""&&!String.Equals(product,"valorant",StringComparison.OrdinalIgnoreCase))continue;
            string id=Json.S(entry,"puuid");
            Player player;
            players.TryGetValue(id,out player);
            bool own=snapshot.Self!=null&&String.Equals(snapshot.Self.Id,id,StringComparison.OrdinalIgnoreCase);
            if(player==null&&!own)continue;
            var presence=RiotPresenceData(entry);
            if(presence==null)continue;
            string party=ReadPartyId(presence);
            string matchId=Json.S(presence,"matchId");
            string phase=Json.S(presence,"sessionLoopState");
            string expected=snapshot.Status=="IN MATCH"?"INGAME":"PREGAME";
            string map=HistoryLogic.MapName(Json.S(presence,"matchMap"));
            string ownerMap=HistoryLogic.MapName(Json.S(presence,"partyOwnerMatchMap"));
            string ownerPhase=Json.S(presence,"partyOwnerSessionLoopState");
            bool sameMatch=matchId!=""?String.Equals(snapshot.Match,matchId,StringComparison.OrdinalIgnoreCase):
                phase==expected&&(String.Equals(snapshot.Map,map,StringComparison.OrdinalIgnoreCase)&&map!="Map unavailable"||
                    map=="Map unavailable"&&ownerPhase==expected&&ownerMap!="Map unavailable"&&
                    String.Equals(snapshot.Map,ownerMap,StringComparison.OrdinalIgnoreCase));
            if(player!=null&&String.IsNullOrEmpty(player.PartyId)&&party!=""&&sameMatch)player.PartyId=party;
            if(own)ApplyDiscordScore(snapshot,presence);
        }
    }
    internal static void ApplyDiscordScore(Snapshot snapshot,object presence){
        if(snapshot.Status!="IN MATCH"||Json.S(presence,"sessionLoopState")!="INGAME")return;
        string reported=Json.S(presence,"matchId");
        if(reported!=""&&!String.Equals(snapshot.Match,reported,StringComparison.OrdinalIgnoreCase))return;
        string reportedMap=Json.S(presence,"matchMap"),ownerMap=Json.S(presence,"partyOwnerMatchMap");
        if(reportedMap!=""&&!String.Equals(snapshot.Map,HistoryLogic.MapName(reportedMap),StringComparison.OrdinalIgnoreCase))return;
        bool sameMap=ownerMap!=""&&String.Equals(snapshot.Map,HistoryLogic.MapName(ownerMap),StringComparison.OrdinalIgnoreCase);
        if(ownerMap!=""&&!sameMap)return;
        string ownerPhase=Json.S(presence,"partyOwnerSessionLoopState");
        if(ownerPhase!=""&&ownerPhase!="INGAME"&&!Json.B(presence,"isPartyOwner"))return;
        int ally,enemy;
        if(Int32.TryParse(Json.S(presence,"partyOwnerMatchScoreAllyTeam"),out ally)&&ally>=0&&
            Int32.TryParse(Json.S(presence,"partyOwnerMatchScoreEnemyTeam"),out enemy)&&enemy>=0){
            snapshot.AllyScore=ally;
            snapshot.EnemyScore=enemy;
        }
    }

}

sealed class DesktopIntegration:IDisposable {
    const int Shortcut=0x4C54, HotkeyMessage=0x0312;
    const uint Alt=0x0001,Control=0x0002,Shift=0x0004,NoRepeat=0x4000;
    readonly Window main;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu;
    readonly Image trayLogo;
    readonly AgentSelectOverlay selection;
    readonly OverlayBackdrop backdrop=new OverlayBackdrop();
    readonly Timer foreground=new Timer{Interval=350};
    readonly DiscordToggle discordButton;
    readonly ToolStripMenuItem discordMenu;
    readonly ToolStripLabel trayPhase;
    readonly ToolStripMenuItem installUpdate;
    readonly Timer updateTimer=new Timer{Interval=10000};
    readonly System.Threading.CancellationTokenSource updateCancellation=new System.Threading.CancellationTokenSource();
    DateTime nextUpdateCheck=DateTime.UtcNow;
    string pendingUpdate;
    bool checkingUpdate;
    string displayedDiscordStatus="",saveError="";
    readonly DiscordPresence discord=new DiscordPresence();
    string pendingMatch="",dismissedMatch="";
    string activeMatch="";
    bool interactiveOverlay;
    // Fullscreen mode: the overlay is shown topmost WITHOUT taking focus, so VALORANT keeps the foreground and is
    // not minimized. If Windows still minimizes the game (true exclusive fullscreen), we stop trying this session.
    bool passiveOverlay,fullscreenBlocked,fullscreenNoticeShown;
    DateTime fullscreenShownAt=DateTime.MinValue;
    IntPtr fullscreenGame;
    IntPtr interactiveGame;
    Rectangle overlayGame;
    Point overlayOffset;
    bool shortcutActive,exit,registered,watchingFallback,fallbackDown;
    DateTime nextHotkeyAttempt;
    IntPtr registeredHandle;
    Keys hotkey=HotkeyPreference.Current.Key,hotkeyModifiers=HotkeyPreference.Current.Modifiers;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out Rectangle bounds);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);

    public DesktopIntegration(Window main,Dictionary<string,PlayerProfile> profiles,DiscordToggle button,Action<Player> openPlayer){
        this.main=main;
        discordButton=button;
        selection=new AgentSelectOverlay(profiles);
        selection.Dismissed+=HideOverlay;
        selection.MoveRequested+=MoveOverlay;
        selection.PlayerClicked+=delegate(Player player){OpenMain();HideOverlay();openPlayer(player);};
        discordButton.Click+=ToggleDiscord;
        menu=new RoundedTrayMenu{ShowImageMargin=false,ShowCheckMargin=false,BackColor=TrayMenuRenderer.Surface,
            ForeColor=Palette.Text,Padding=new Padding(5),Renderer=new TrayMenuRenderer(),Font=new Font("Segoe UI",10)};
        trayLogo=TrackArt.Asset("lunar.png");
        menu.Items.Add(new ToolStripLabel("Lunar",trayLogo){Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=Palette.Text,
            AutoSize=false,Size=new Size(218,30),Margin=new Padding(9,5,8,0),ImageAlign=ContentAlignment.MiddleLeft,
            TextAlign=ContentAlignment.MiddleLeft,TextImageRelation=TextImageRelation.ImageBeforeText});
        trayPhase=new ToolStripLabel("Waiting for VALORANT"){ForeColor=Palette.Muted,Margin=new Padding(10,0,10,7),
            TextAlign=ContentAlignment.MiddleLeft};
        menu.Items.Add(trayPhase);
        menu.Items.Add(new ToolStripSeparator());
        discordMenu=TrayAction("Discord Presence",ToggleDiscord);
        TrayAction("Open Lunar",delegate{OpenMain();});
        installUpdate=TrayAction("Install update",delegate{ExitWithUpdate(true);});
        installUpdate.Available=false;
        TrayAction("Exit Lunar",delegate{ExitWithUpdate(false);}).ForeColor=Color.FromArgb(236,153,172);
        UpdateDiscordButton();
        tray=new NotifyIcon{Icon=main.Icon,Text="Lunar",ContextMenuStrip=menu,Visible=true};
        tray.DoubleClick+=delegate{OpenMain();};
        main.FormClosing+=Closing;
        main.HandleDestroyed+=delegate{UnregisterShortcut();};
        foreground.Tick+=delegate{SyncOverlay();UpdateDiscordButton();};
        foreground.Start();
        updateTimer.Tick+=delegate{CheckUpdates();};
        updateTimer.Start();
    }
    async void CheckUpdates(){
        if(pendingUpdate!=null)return;
        if(checkingUpdate||DateTime.UtcNow<nextUpdateCheck)return;
        checkingUpdate=true;
        nextUpdateCheck=DateTime.UtcNow.AddHours(3);
        var token=updateCancellation.Token;
        try{
            string folder=await LunarUpdates.Prepare(token);
            if(!token.IsCancellationRequested&&folder!=null){
                pendingUpdate=folder;
                installUpdate.Available=true;
                main.ShowUpdateReady(delegate{ExitWithUpdate(true);},LunarUpdates.StagedVersion(folder));
            }
        }catch(Exception){}
        finally{checkingUpdate=false;}
    }
    internal bool UpdateCheckActive {get{return checkingUpdate;}}
    internal bool UpdateReady {get{return pendingUpdate!=null;}}
    internal void CheckUpdatesNow(){nextUpdateCheck=DateTime.MinValue;CheckUpdates();}
    void ExitWithUpdate(bool restart){
        if(restart&&pendingUpdate!=null){
            try{LunarUpdates.Start(pendingUpdate,restart);}
            catch(Exception){
                pendingUpdate=null;
                installUpdate.Available=false;
                main.HideUpdateReady();
                nextUpdateCheck=DateTime.UtcNow.AddMinutes(5);
                return;
            }
        }else if(restart)return;
        exit=true;
        main.Close();
    }
    ToolStripMenuItem TrayAction(string label,EventHandler handler){
        var item=new ToolStripMenuItem(label,null,handler){
            AutoSize=false,Size=new Size(228,36),TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(7,0,0,0),
            ForeColor=Palette.Text
        };
        menu.Items.Add(item);
        return item;
    }
    void RegisterShortcut(){
        if(registered||!main.IsHandleCreated||DateTime.UtcNow<nextHotkeyAttempt)return;
        nextHotkeyAttempt=DateTime.UtcNow.AddSeconds(2);
        registeredHandle=main.Handle;
        registered=RegisterHotKey(registeredHandle,Shortcut,NativeModifiers(hotkeyModifiers)|NoRepeat,(uint)hotkey);
    }
    static uint NativeModifiers(Keys modifiers){
        uint result=0;
        if((modifiers&Keys.Alt)==Keys.Alt)result|=Alt;
        if((modifiers&Keys.Control)==Keys.Control)result|=Control;
        if((modifiers&Keys.Shift)==Keys.Shift)result|=Shift;
        return result;
    }
    internal string HotkeyDisplay{get{return new HotkeyBinding{Key=hotkey,Modifiers=hotkeyModifiers}.Display;}}
    internal bool SetHotkey(Keys key,Keys modifiers){
        if(!HotkeyPreference.Valid(key,modifiers)||!main.IsHandleCreated)return false;
        UnregisterShortcut();
        bool available=RegisterHotKey(main.Handle,Shortcut,NativeModifiers(modifiers)|NoRepeat,(uint)key);
        if(available)UnregisterHotKey(main.Handle,Shortcut);
        else{nextHotkeyAttempt=DateTime.MinValue;return false;}
        hotkey=key;hotkeyModifiers=modifiers;HotkeyPreference.Save(new HotkeyBinding{Key=key,Modifiers=modifiers});
        nextHotkeyAttempt=DateTime.MinValue;
        return true;
    }
    void UnregisterShortcut(){
        if(registered)UnregisterHotKey(registeredHandle,Shortcut);
        registered=false;
        nextHotkeyAttempt=DateTime.MinValue;
    }
    void Closing(object sender,FormClosingEventArgs e){
        if(!exit&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;HideMain();}
    }
    void HideMain(){main.Hide();}
    void OpenMain(){
        main.Show();
        if(main.WindowState==FormWindowState.Minimized)main.WindowState=FormWindowState.Normal;
        main.Activate();
    }
    void HideOverlay(){
        dismissedMatch=pendingMatch;
        EndInteraction();
        HideOverlayWindows();
    }
    void EndInteraction(){
        bool restore=interactiveOverlay&&!passiveOverlay&&OverlayHasFocus()&&interactiveGame!=IntPtr.Zero;
        var gameWindow=interactiveGame;
        interactiveOverlay=false;
        passiveOverlay=false;
        interactiveGame=IntPtr.Zero;
        if(restore)SetForegroundWindow(gameWindow);
    }
    bool OverlayHasFocus(){
        return selection.IsHandleCreated&&GetForegroundWindow()==selection.Handle;
    }
    IntPtr ShortcutWindow(){
        return interactiveOverlay&&OverlayHasFocus()?interactiveGame:GetForegroundWindow();
    }
    void HideOverlayWindows(){
        backdrop.Hide();
        selection.Hide();
    }
    public void HandleMessage(Message message){
        if(message.Msg!=HotkeyMessage||message.WParam.ToInt32()!=Shortcut||!shortcutActive)return;
        if(!registered)return;
        var window=ShortcutWindow();
        var game=ForegroundGame(window);
        if(!game.IsEmpty)ToggleOverlay(game,window);
    }
    void ToggleOverlay(Rectangle game,IntPtr gameWindow){
        if(selection.Visible&&interactiveOverlay){HideOverlay();return;}
        dismissedMatch="";
        if(ExclusiveFullscreen()){
            if(fullscreenBlocked){ShowFullscreenNotice();return;}
            interactiveGame=gameWindow;
            interactiveOverlay=true;passiveOverlay=true;
            ShowPassive(game,gameWindow);
            return;
        }
        interactiveGame=gameWindow;
        interactiveOverlay=true;
        ShowOverlay(game);
        if(!SetForegroundWindow(selection.Handle)&&!OverlayHasFocus()){
            EndInteraction();
            if(pendingMatch=="")HideOverlayWindows();
            else backdrop.Hide();
        }
    }
    void ReadShortcutFallback(Rectangle game,IntPtr gameWindow){
        bool pressed=KeyDown(hotkey)&&
            (((hotkeyModifiers&Keys.Alt)==Keys.Alt)==KeyDown(Keys.Menu))&&
            (((hotkeyModifiers&Keys.Control)==Keys.Control)==KeyDown(Keys.ControlKey))&&
            (((hotkeyModifiers&Keys.Shift)==Keys.Shift)==KeyDown(Keys.ShiftKey));
        if(FallbackPressed(pressed))ToggleOverlay(game,gameWindow);
    }
    bool FallbackPressed(bool pressed){
        bool toggle=watchingFallback&&pressed&&!fallbackDown;
        fallbackDown=pressed;
        watchingFallback=true;
        return toggle;
    }
    static bool KeyDown(Keys key){return (GetAsyncKeyState((int)key)&0x8000)!=0;}
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    const int RunningExclusiveFullscreen=3; // QUNS_RUNNING_D3D_FULL_SCREEN
    DateTime gameFocusedSince=DateTime.MaxValue;
    IntPtr focusedGame;

    static Rectangle ForegroundGame(IntPtr hwnd){
        if(hwnd==IntPtr.Zero||IsIconic(hwnd)||!IsWindowVisible(hwnd))return Rectangle.Empty;
        uint pid;GetWindowThreadProcessId(hwnd,out pid);
        try{
            using(var process=Process.GetProcessById((int)pid))
                if(!process.ProcessName.StartsWith("VALORANT-Win64-Shipping",StringComparison.OrdinalIgnoreCase))
                    return Rectangle.Empty;
        }catch(ArgumentException){return Rectangle.Empty;}catch(InvalidOperationException){return Rectangle.Empty;}
        var screen=Screen.FromHandle(hwnd).Bounds;
        Rectangle client;
        var origin=Point.Empty;
        // A minimized/alt-tabbed fullscreen game can still be the "foreground" window with an empty client area.
        // Never fall back to the whole screen in that case, or the overlay pops up over the desktop.
        if(!GetClientRect(hwnd,out client)||!ClientToScreen(hwnd,ref origin))return Rectangle.Empty;
        var visible=Rectangle.Intersect(new Rectangle(origin,client.Size),screen);
        return visible.Width>320&&visible.Height>240?visible:Rectangle.Empty;
    }
    static bool ExclusiveFullscreen(){
        int state;
        try{return SHQueryUserNotificationState(out state)==0&&state==RunningExclusiveFullscreen;}
        catch(EntryPointNotFoundException){return false;}catch(DllNotFoundException){return false;}
    }
    // Shows the overlay above a fullscreen game without activating it (the game keeps keyboard focus).
    void ShowPassive(Rectangle game,IntPtr gameWindow){
        bool wasVisible=selection.Visible;
        ShowOverlay(game);
        backdrop.Hide();
        const uint NoSize=0x0001,NoMove=0x0002,NoActivate=0x0010,ShowWindow=0x0040;
        SetWindowPos(selection.Handle,new IntPtr(-1),0,0,0,0,NoSize|NoMove|NoActivate|ShowWindow); // HWND_TOPMOST
        if(!wasVisible){fullscreenShownAt=DateTime.UtcNow;fullscreenGame=gameWindow;}
    }
    // True exclusive fullscreen minimizes the game as soon as a topmost window appears: detect it once and stop.
    bool FullscreenMinimizedGame(){
        if(fullscreenGame==IntPtr.Zero||DateTime.UtcNow-fullscreenShownAt>TimeSpan.FromSeconds(4))return false;
        if(!IsIconic(fullscreenGame))return false;
        var game=fullscreenGame;
        fullscreenBlocked=true;fullscreenGame=IntPtr.Zero;
        selection.Hide();backdrop.Hide();
        interactiveOverlay=false;passiveOverlay=false;interactiveGame=IntPtr.Zero;
        try{ShowWindowAsync(game,9);SetForegroundWindow(game);}catch(Exception){} // SW_RESTORE: bring the game back
        ShowFullscreenNotice();
        return true;
    }
    void ShowFullscreenNotice(){
        if(fullscreenNoticeShown)return;
        fullscreenNoticeShown=true;
        try{
            tray.ShowBalloonTip(8000,"Lunar",LunarLanguage.Text(
                "VALORANT is in exclusive Fullscreen, which hides the overlay. In VALORANT → Settings → Video, set Display Mode to Windowed Fullscreen.",
                "O VALORANT está em Tela cheia exclusiva, que esconde o overlay. Em VALORANT → Configurações → Vídeo, mude o Modo de exibição para Tela cheia em janela."),ToolTipIcon.Info);
        }catch(Exception){}
    }
    void SyncOverlay(){
        if(interactiveOverlay&&!passiveOverlay&&!OverlayHasFocus())HideOverlay();
        var window=ShortcutWindow();
        var game=shortcutActive?ForegroundGame(window):Rectangle.Empty;
        if(game.IsEmpty){
            FullscreenMinimizedGame();
            focusedGame=IntPtr.Zero;gameFocusedSince=DateTime.MaxValue;
            UnregisterShortcut();
            watchingFallback=false;
            foreground.Interval=350;
            if(interactiveOverlay)HideOverlay();else HideOverlayWindows();
            return;
        }
        if(window!=focusedGame){focusedGame=window;gameFocusedSince=DateTime.UtcNow;}
        RegisterShortcut();
        foreground.Interval=registered?350:40;
        if(registered)watchingFallback=false;
        // Automatic agent-select popup: only after VALORANT has stayed focused for a moment. In fullscreen it is shown
        // without taking focus; if that still minimizes the game, fullscreen popups are disabled for this session.
        bool settled=DateTime.UtcNow-gameFocusedSince>=TimeSpan.FromMilliseconds(800);
        bool fullscreen=ExclusiveFullscreen();
        bool autoAllowed=interactiveOverlay||(settled&&!(fullscreen&&fullscreenBlocked));
        if(autoAllowed&&OverlayRequested(pendingMatch,dismissedMatch,interactiveOverlay)){
            if(fullscreen||passiveOverlay)ShowPassive(game,window);else ShowOverlay(game);
        }
        else if(!interactiveOverlay)HideOverlayWindows();
        if(!registered)ReadShortcutFallback(game,window);
    }
    internal static bool OverlayRequested(string select,string dismissed,bool live){
        return select!=""?select!=dismissed:live;
    }
    internal static Rectangle CenterOverlay(Rectangle game,int preferredHeight){
        int width=Math.Min(606,game.Width-24),height=Math.Min(preferredHeight,game.Height-24);
        return new Rectangle(game.X+(game.Width-width)/2,game.Y+(game.Height-height)/2,width,height);
    }
    internal static Point ClampOverlayPosition(Rectangle game,Size size,Point position){
        return new Point(Math.Max(game.Left,Math.Min(position.X,game.Right-size.Width)),
            Math.Max(game.Top,Math.Min(position.Y,game.Bottom-size.Height)));
    }
    void MoveOverlay(Point position){
        if(overlayGame.IsEmpty)return;
        var center=CenterOverlay(overlayGame,selection.PreferredOverlayHeight).Location;
        selection.Location=ClampOverlayPosition(overlayGame,selection.Size,position);
        overlayOffset=new Point(selection.Left-center.X,selection.Top-center.Y);
    }
    void ShowOverlay(Rectangle game){
        overlayGame=game;
        if(!selection.Visible)overlayOffset=Point.Empty;
        var bounds=CenterOverlay(game,selection.PreferredOverlayHeight);
        bounds.Location=ClampOverlayPosition(game,bounds.Size,Point.Add(bounds.Location,new Size(overlayOffset)));
        if(selection.Bounds!=bounds)selection.Bounds=bounds;
        if(!selection.Visible)selection.Show();
        if(interactiveOverlay){
            if(backdrop.Bounds!=game)backdrop.Bounds=game;
            if(!backdrop.Visible){
                backdrop.Show();
                const uint NoSize=0x0001,NoMove=0x0002,NoActivate=0x0010;
                SetWindowPos(backdrop.Handle,selection.Handle,0,0,0,0,NoSize|NoMove|NoActivate);
            }
        }else backdrop.Hide();
    }
    public void Update(Snapshot data){
        trayPhase.Text=data.Status=="IN MATCH"?(data.Map=="Map unavailable"?"In match":data.Map+" · In match"):
            data.Status=="AGENT SELECT"?"Agent select":data.Self!=null?"VALORANT lobby":"Waiting for VALORANT";
        shortcutActive=CanOpenLiveMatch(data);
        string nextMatch=shortcutActive?data.Match:"";
        string nextSelect=data.Status=="AGENT SELECT"?nextMatch:"";
        if(activeMatch!=nextMatch||pendingMatch!=nextSelect){
            dismissedMatch="";
            EndInteraction();
        }
        activeMatch=nextMatch;
        pendingMatch=nextSelect;
        if(pendingMatch=="")dismissedMatch="";
        selection.Apply(data);
        discord.Update(data);
        SyncOverlay();
    }
    internal static bool CanOpenLiveMatch(Snapshot data){
        return !String.IsNullOrEmpty(data.Match)&&(data.Status=="AGENT SELECT"||data.Status=="IN MATCH");
    }
    public void RefreshStats(){selection.Invalidate();}

    void ToggleDiscord(object sender,EventArgs e){
        try{discord.SetEnabled(!discord.Enabled);saveError="";}
        catch(IOException){saveError="Could not save Discord settings.";}
        catch(UnauthorizedAccessException){saveError="Could not save Discord settings.";}
        UpdateDiscordButton();
    }
    void UpdateDiscordButton(){
        string status=saveError==""?discord.Status:saveError;
        if(status==displayedDiscordStatus)return;
        displayedDiscordStatus=status;
        bool enabled=discord.Enabled;
        discordButton.Checked=enabled;
        discordMenu.Checked=enabled;
        discordMenu.Text="Discord Presence";
        discordMenu.Tag=enabled;
        string summary=DiscordToggle.StatusText(status,enabled,discord.Connected,saveError!="");
        discordButton.SetStatus(summary,discord.Connected&&enabled,saveError!="");
        discordButton.AccessibleDescription=summary+". Click to "+(enabled?"turn off":"turn on")+" Discord Presence.";
    }
    public void Dispose(){
        updateTimer.Stop();updateTimer.Dispose();
        updateCancellation.Cancel();updateCancellation.Dispose();
        foreground.Stop();foreground.Dispose();
        UnregisterShortcut();
        EndInteraction();
        selection.Dispose();
        backdrop.Dispose();
        tray.Visible=false;tray.Dispose();menu.Dispose();if(trayLogo!=null)trayLogo.Dispose();
        discord.Dispose();
    }
}
sealed class HotkeyCaptureDialog:Form {
    readonly Label prompt;
    public Keys CapturedKey{get;private set;}
    public Keys CapturedModifiers{get;private set;}
    public HotkeyCaptureDialog(string current,bool portuguese){
        Text=portuguese?"Alterar atalho":"Change hotkey";
        FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterParent;
        ShowInTaskbar=false;MaximizeBox=false;MinimizeBox=false;KeyPreview=true;
        ClientSize=new Size(430,170);BackColor=Palette.Panel;ForeColor=Palette.Text;
        var title=new Label{Text=portuguese?"Pressione a nova combinação":"Press the new key combination",Location=new Point(22,19),Size=new Size(385,28),Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=Palette.Text,BackColor=Color.Transparent};
        prompt=new Label{Text=(portuguese?"Atalho atual: ":"Current shortcut: ")+current+"\r\n"+(portuguese?"Use Alt, Ctrl ou Shift junto com uma tecla. Esc cancela.":"Use Alt, Ctrl or Shift with a key. Esc cancels."),Location=new Point(24,58),Size=new Size(385,48),Font=new Font("Segoe UI",9),ForeColor=Palette.Muted,BackColor=Color.Transparent};
        var cancel=new Button{Text=portuguese?"Cancelar":"Cancel",Location=new Point(314,119),Size=new Size(96,32),FlatStyle=FlatStyle.Flat,BackColor=Palette.Raised,ForeColor=Palette.Text,Font=new Font("Segoe UI",9),DialogResult=DialogResult.Cancel};
        cancel.FlatAppearance.BorderColor=Palette.Border;Controls.Add(title);Controls.Add(prompt);Controls.Add(cancel);CancelButton=cancel;
        KeyDown+=CaptureKey;
    }
    void CaptureKey(object sender,KeyEventArgs e){
        e.Handled=true;e.SuppressKeyPress=true;
        if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();return;}
        if(e.KeyCode==Keys.Menu||e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.LWin||e.KeyCode==Keys.RWin)return;
        Keys modifiers=Keys.None;
        if(e.Alt)modifiers|=Keys.Alt;
        if(e.Control)modifiers|=Keys.Control;
        if(e.Shift)modifiers|=Keys.Shift;
        if(!HotkeyPreference.Valid(e.KeyCode,modifiers)){
            prompt.Text=Text.StartsWith("Alterar",StringComparison.Ordinal)?"Use pelo menos Alt, Ctrl ou Shift com uma tecla.":"Use at least Alt, Ctrl or Shift with a key.";
            return;
        }
        CapturedKey=e.KeyCode;CapturedModifiers=modifiers;DialogResult=DialogResult.OK;Close();
    }
}
sealed class OverlayBackdrop:Form {
    public OverlayBackdrop(){
        StartPosition=FormStartPosition.Manual;
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        TopMost=true;
        BackColor=Color.Black;
        Opacity=.24;
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x00000080;return cp;}}
}

sealed class DiscordToggle:CheckBox {
    readonly Image symbol=TrackArt.Asset("discord-clyde.png");
    public readonly DiscordStatusHint Hint=new DiscordStatusHint();
    bool hover,connected,failed;
    public DiscordToggle(){
        Text="Discord";
        AccessibleName="Discord Rich Presence";
        AutoCheck=false;
        Appearance=Appearance.Button;
        FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;
        Cursor=Cursors.Hand;
        Font=new Font("Segoe UI",9.5f,FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    internal static string StatusText(string status,bool enabled,bool online,bool saveFailed){
        if(saveFailed)return "Couldn’t save changes";
        if(!enabled)return "Presence off";
        if(online)return "Presence active";
        if(status=="Waiting for Discord desktop")return "Open Discord desktop";
        if(status=="Connecting to Discord…")return "Connecting…";
        return "Reconnecting…";
    }
    public void SetStatus(string text,bool online,bool error){
        connected=online;
        failed=error;
        Hint.Text=text;
        Hint.Accent=StatusColor;
        Hint.Invalidate();
        UpdateHint();
        Invalidate();
    }
    Color StatusColor{get{
        return failed?Palette.Red:connected?Palette.Green:Checked?Palette.Accent:Palette.Muted;
    }}
    void UpdateHint(){
        if(!Hint.IsDisposed)Hint.Visible=Visible&&(hover||(Focused&&ShowFocusCues))&&Hint.Text.Length>0;
    }
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
    protected override void OnMouseEnter(EventArgs e){hover=true;UpdateHint();Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;UpdateHint();Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);UpdateHint();Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);UpdateHint();Invalidate();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Hint!=null)UpdateHint();}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<48||Height<16)return;
        var g=e.Graphics;
        g.Clear(Parent==null?Palette.Bg:Parent.BackColor);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Skin.Round(new Rectangle(1,1,Width-3,Height-3),9))
        using(var fill=new SolidBrush(hover?Palette.Raised:Palette.Sidebar))
        using(var border=new Pen(Focused&&ShowFocusCues?Palette.Accent:hover?Palette.Border:Palette.Border)){
            g.FillPath(fill,path);
            g.DrawPath(border,path);
        }
        var icon=new Rectangle(15,(Height-13)/2,18,13);
        if(symbol!=null){
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(symbol,icon);
        }
        Rectangle label=new Rectangle(43,0,Width-70,Height);
        TextRenderer.DrawText(g,Text,Font,label,Palette.Text,
            TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
        var dot=new Rectangle(Width-19,(Height-7)/2,7,7);
        var tint=StatusColor;
        using(var ink=new SolidBrush(tint))
        using(var outline=new Pen(tint)){
            if(Checked||failed)g.FillEllipse(ink,dot);
            else g.DrawEllipse(outline,dot);
        }
    }
    protected override void Dispose(bool disposing){
        if(disposing&&symbol!=null)symbol.Dispose();
        base.Dispose(disposing);
    }
    internal static void DrawTrack(Graphics g,Rectangle bounds,bool enabled){
        using(var path=Skin.Round(bounds,bounds.Height/2))
        using(var fill=new SolidBrush(enabled?Palette.Accent:Palette.Raised))
        using(var border=new Pen(enabled?Palette.Accent:Palette.Border)){
            g.FillPath(fill,path);
            g.DrawPath(border,path);
        }
        int dot=bounds.Height-8;
        using(var handle=new SolidBrush(enabled?Color.White:Color.FromArgb(182,170,197)))
            g.FillEllipse(handle,enabled?bounds.Right-dot-4:bounds.X+4,bounds.Y+4,dot,dot);
    }
}

sealed class DiscordStatusHint:Control {
    public Color Accent=Palette.Muted;
    public DiscordStatusHint(){
        DoubleBuffered=true;
        Visible=false;
        TabStop=false;
        Font=new Font("Segoe UI",8.5f);
    }
    protected override void OnPaint(PaintEventArgs e){
        if(Width<32||Height<15)return;
        var g=e.Graphics;
        g.Clear(Parent==null?Palette.Bg:Parent.BackColor);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Skin.Round(new Rectangle(1,1,Width-3,Height-3),9))
        using(var fill=new SolidBrush(Palette.Panel))
        using(var edge=new Pen(Palette.Border))
        using(var dot=new SolidBrush(Accent)){
            g.FillPath(fill,path);
            g.DrawPath(edge,path);
            g.FillEllipse(dot,11,(Height-6)/2,6,6);
        }
        TextRenderer.DrawText(g,Text,Font,new Rectangle(24,0,Width-31,Height),Palette.Text,
            TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    }
}

sealed class RoundedTrayMenu:ContextMenuStrip {
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        if(Width<24||Height<24)return;
        using(var shape=Skin.Round(new Rectangle(Point.Empty,Size),13))Region=new Region(shape);
    }
}

sealed class TrayMenuRenderer:ToolStripProfessionalRenderer {
    public static readonly Color Surface=Palette.Panel;
    public TrayMenuRenderer(){RoundedEdges=false;}
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Surface);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var outline=Skin.Round(new Rectangle(0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1),13))
        using(var border=new Pen(Palette.Border))e.Graphics.DrawPath(border,outline);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        if(e.Item.Selected)
            using(var shape=Skin.Round(new Rectangle(2,1,e.Item.Width-4,e.Item.Height-2),8))
            using(var fill=new SolidBrush(Palette.AccentSoft))e.Graphics.FillPath(fill,shape);
        if(e.Item.Tag is bool){
            bool enabled=(bool)e.Item.Tag;
            var toggle=new Rectangle(e.Item.Width-49,(e.Item.Height-16)/2,33,16);
            DiscordToggle.DrawTrack(e.Graphics,toggle,enabled);
        }
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){
        using(var border=new Pen(Palette.Border))
            e.Graphics.DrawLine(border,8,e.Item.Height/2,e.Item.Width-8,e.Item.Height/2);
    }
}
sealed class AgentSelectOverlay:Form {
    readonly Dictionary<string,PlayerProfile> profiles;
    readonly SoftButton close;
    Snapshot snapshot=new Snapshot();
    public event Action Dismissed;
    public event Action<Player> PlayerClicked;
    public event Action<Point> MoveRequested;
    Point dragOrigin;
    bool dragging;
    const int Header=91,RowHeight=38;
    const int RankOffset=302,PeakOffset=254,KdOffset=206,HeadshotOffset=154,RecentOffset=105;
    static readonly Color Surface=Palette.Panel, Stroke=Palette.Border;
    readonly Image mark=TrackArt.Asset("lunar.png");
    readonly Font levelFont=new Font("Segoe UI",6,FontStyle.Bold);
    readonly List<KeyValuePair<Rectangle,Player>> hits=new List<KeyValuePair<Rectangle,Player>>();
    bool InMatch{get{return snapshot.Status=="IN MATCH";}}
    int AllyRows{get{return Math.Max(1,snapshot.Allies.Count);}}
    int EnemyRows{get{return InMatch?snapshot.Enemies.Count:0;}}
    public int PreferredOverlayHeight{get{return Header+(AllyRows+EnemyRows)*RowHeight+(EnemyRows>0?27:0)+47;}}
    public AgentSelectOverlay(Dictionary<string,PlayerProfile> profiles){
        this.profiles=profiles;
        StartPosition=FormStartPosition.Manual;
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        TopMost=true;
        BackColor=Surface;
        DoubleBuffered=true;
        ResizeRedraw=true;
        close=new SoftButton{Text="×",Width=37,Height=29,Top=14,FlatStyle=FlatStyle.Flat,BackColor=Palette.Raised,
            ForeColor=Palette.Text,TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.Hand,Anchor=AnchorStyles.Top|AnchorStyles.Right};
        close.Left=ClientSize.Width-51;
        close.FlatAppearance.BorderSize=0;
        close.Click+=delegate{if(Dismissed!=null)Dismissed();};
        Controls.Add(close);
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x00000080;return cp;}}
    public void Apply(Snapshot data){snapshot=data;Invalidate();}
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);
        if(Width<28||Height<28)return;
        using(var shape=Skin.Round(new Rectangle(Point.Empty,Size),15))Region=new Region(shape);
    }
    protected override void Dispose(bool disposing){
        if(disposing){
            if(mark!=null)mark.Dispose();
            levelFont.Dispose();
        }
        base.Dispose(disposing);
    }
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left||e.Y>=60)return;
        dragOrigin=e.Location;
        dragging=true;
        Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        if(dragging&&MoveRequested!=null)MoveRequested(Point.Subtract(PointToScreen(e.Location),new Size(dragOrigin)));
    }
    protected override void OnMouseUp(MouseEventArgs e){
        if(e.Button==MouseButtons.Left&&dragging){
            dragging=false;
            Capture=false;
        }
        base.OnMouseUp(e);
    }
    protected override void OnMouseCaptureChanged(EventArgs e){
        base.OnMouseCaptureChanged(e);
        if(!Capture)dragging=false;
    }
    protected override void OnMouseClick(MouseEventArgs e){
        base.OnMouseClick(e);
        if(e.Button!=MouseButtons.Left||PlayerClicked==null)return;
        var hit=hits.FirstOrDefault(item=>item.Key.Contains(e.Location));
        if(hit.Value!=null)PlayerClicked(hit.Value);
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        hits.Clear();
        if(Width<300||Height<100)return;
        using(var outline=Skin.Round(new Rectangle(0,0,Width-1,Height-1),15))
        using(var border=new Pen(Stroke))g.DrawPath(border,outline);
        if(mark!=null)g.DrawImage(mark,new Rectangle(14,12,38,38));
        ProfileCanvas.T(g,InMatch?"Live Match":"Agent Select",13,Palette.Text,new Rectangle(60,10,Width-222,25),true);
        string map=snapshot.Map=="Map unavailable"?"":snapshot.Map;
        ProfileCanvas.T(g,map,9,Palette.Muted,new Rectangle(60,34,Width-222,19));
        string side=LiveMatchArt.StartingSide(snapshot);
        string badge=InMatch?(snapshot.AllyScore.HasValue&&snapshot.EnemyScore.HasValue
            ?snapshot.AllyScore+" : "+snapshot.EnemyScore:"In match"):side=="Waiting"?"Side pending":side;
        Color sideColor=InMatch?Palette.Accent:LiveMatchArt.SideColor(side);
        var sideBadge=new Rectangle(Width-158,19,96,24);
        using(var outline=Skin.Round(sideBadge,7))
        using(var fill=new SolidBrush(Color.FromArgb(40,sideColor)))g.FillPath(fill,outline);
        ProfileCanvas.T(g,badge,8,sideColor,Rectangle.Inflate(sideBadge,-8,0),true);
        using(var line=new Pen(Stroke))g.DrawLine(line,14,60,Width-15,60);
        int rankX=Width-RankOffset,peakX=Width-PeakOffset,kdX=Width-KdOffset,hsX=Width-HeadshotOffset,formX=Width-RecentOffset;
        ProfileCanvas.T(g,TrackerSurface.SingleRoster(snapshot)?"DEATHMATCH · PLAYERS":"YOUR TEAM",8,Palette.Muted,new Rectangle(15,63,145,25),true);
        ProfileCanvas.T(g,"RANK",8,Palette.Muted,new Rectangle(rankX,63,48,25));
        ProfileCanvas.T(g,"PEAK",8,Palette.Muted,new Rectangle(peakX,63,48,25));
        ProfileCanvas.T(g,"K/D",8,Palette.Muted,new Rectangle(kdX,63,43,25));
        ProfileCanvas.T(g,"HS%",8,Palette.Muted,new Rectangle(hsX,63,49,25));
        ProfileCanvas.T(g,"RECENT",8,Palette.Muted,new Rectangle(formX,63,90,25));
        var colors=LiveMatchArt.RosterParties(snapshot,profiles,DateTime.Now);
        int rowHeight=Math.Min(RowHeight,Math.Max(22,(Height-Header-47-(EnemyRows>0?27:0))/(AllyRows+EnemyRows)));
        DrawTeam(g,colors,snapshot.Allies,Header,rowHeight);
        if(EnemyRows>0){
            int teamTop=Header+AllyRows*rowHeight;
            ProfileCanvas.T(g,"ENEMY TEAM",8,Palette.Muted,new Rectangle(15,teamTop,145,25),true);
            DrawTeam(g,colors,snapshot.Enemies,teamTop+27,rowHeight);
        }
        string agent=snapshot.Self==null?"":Catalog.Agent(snapshot.Self.AgentId);
        if(agent=="Agent unavailable")agent="Choose an agent to see your stats";
        else if(snapshot.Self!=null){
            PlayerProfile own;
            if(profiles.TryGetValue(snapshot.Self.Id,out own)){
                var agentStats=new Summary(own.Matches.Where(match=>String.Equals(match.AgentId,snapshot.Self.AgentId,StringComparison.OrdinalIgnoreCase)));
                if(agentStats.Games>0)agent+="   ·   "+agentStats.Games+" matches   ·   "+StatsMetrics.F(agentStats.WinRate,"%")+" win   ·   "+StatsMetrics.KD(agentStats.Totals)+" K/D   ·   "+StatsMetrics.F(agentStats.Totals.HS,"%")+" HS";
            }
        }
        if(agent!=""){
            using(var line=new Pen(Stroke))g.DrawLine(line,15,Height-40,Width-15,Height-40);
            ProfileCanvas.T(g,agent,9,Palette.Accent,new Rectangle(17,Height-37,Width-34,29),true);
        }
    }
    void DrawTeam(Graphics g,Dictionary<string,Color> colors,List<Player> players,int top,int height){
        int rankX=Width-RankOffset,peakX=Width-PeakOffset,kdX=Width-KdOffset,hsX=Width-HeadshotOffset,formX=Width-RecentOffset;
        if(players.Count==0){
            ProfileCanvas.T(g,"Waiting for teammates…",9,Palette.Muted,new Rectangle(16,top,Width-32,height));
            return;
        }
        for(int i=0;i<players.Count;i++){
            var player=players[i];
            int y=top+i*height;
            int icon=Math.Min(28,height-6),rank=Math.Min(23,height-4);
            var row=new Rectangle(11,y+1,Width-22,height-3);
            hits.Add(new KeyValuePair<Rectangle,Player>(row,player));
            bool me=player.State=="YOU";
            using(var shape=Skin.Round(row,7))
            using(var fill=new SolidBrush(me?Color.FromArgb(47,33,68):Color.FromArgb(28,22,40)))g.FillPath(fill,shape);
            LiveMatchArt.Stripe(g,colors,player,new Rectangle(13,y+(height-icon)/2,3,icon));
            var portrait=new Rectangle(19,y+(height-icon)/2,icon,icon);
            AgentArt.Draw(g,player.AgentId,portrait);
            PlayerProfile levelProfile;profiles.TryGetValue(player.Id,out levelProfile);
            DrawLevel(g,portrait,AccountLevels.Display(player,levelProfile));
            ProfileCanvas.T(g,player.Name,9,me?Palette.Accent:Palette.Text,new Rectangle(54,y,Math.Max(1,rankX-59),height),true);
            PlayerProfile profile;
            profiles.TryGetValue(player.Id,out profile);
            RankArt.Draw(g,profile==null?-1:profile.Tier,new Rectangle(rankX+2,y+(height-rank)/2,rank,rank));
            if(profile!=null&&profile.PeakTier>=3)RankArt.Draw(g,profile.PeakTier,new Rectangle(peakX+2,y+(height-rank)/2,rank,rank));
            var totals=profile==null?null:new Summary(profile.Matches).Totals;
            string kd=totals==null?"—":StatsMetrics.KD(totals);
            ProfileCanvas.T(g,kd,9,Palette.Text,new Rectangle(kdX,y,48,height),true);
            ProfileCanvas.T(g,StatsMetrics.F(totals==null?null:totals.HS,"%"),9,Palette.Text,new Rectangle(hsX,y,45,height),true);
            LiveMatchArt.Recent(g,profile,formX-3,y+(height-16)/2,17);
        }
    }
    void DrawLevel(Graphics g,Rectangle portrait,string text){
        const TextFormatFlags flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter;
        int width=TextRenderer.MeasureText(g,text,levelFont,Size.Empty,flags).Width+6;
        var badge=new Rectangle(portrait.Right-width+1,portrait.Bottom-10,width,11);
        using(var shape=Skin.Round(badge,3))
        using(var fill=new SolidBrush(Color.FromArgb(24,16,36)))
        using(var edge=new Pen(Color.FromArgb(103,76,132))){
            g.FillPath(fill,shape);
            g.DrawPath(edge,shape);
        }
        TextRenderer.DrawText(g,text,levelFont,badge,Palette.Text,flags);
    }

}

static class LiveMatchArt {
    internal static string StartingSide(Snapshot value){
        return value.Side.StartsWith("DEFENSE",StringComparison.Ordinal)?"Defense":
            value.Side.StartsWith("ATTACK",StringComparison.Ordinal)?"Attack":"Waiting";
    }
    internal static Color SideColor(string side){
        return side=="Defense"?Color.FromArgb(94,158,215):side=="Attack"?Palette.Red:Palette.Muted;
    }
    static readonly Color[] PartyColors={Color.FromArgb(111,221,151),Color.FromArgb(103,175,255),
        Color.FromArgb(240,170,85),Color.FromArgb(202,145,245),Color.FromArgb(240,124,170)};
    public static Dictionary<string,Color> Parties(IEnumerable<Player> players){
        var colors=new Dictionary<string,Color>(StringComparer.OrdinalIgnoreCase);
        var groups=players.Where(p=>!String.IsNullOrEmpty(p.PartyId))
            .GroupBy(p=>p.PartyId,StringComparer.OrdinalIgnoreCase).Where(group=>group.Count()>1)
            .OrderBy(group=>group.Key,StringComparer.OrdinalIgnoreCase);
        foreach(var group in groups)
            colors[group.Key]=PartyColors[colors.Count%PartyColors.Length];
        return colors;
    }
    internal static Dictionary<string,Color> RosterParties(Snapshot snapshot,Dictionary<string,PlayerProfile> profiles,DateTime now){
        var roster=snapshot.Allies.Concat(snapshot.Enemies).ToArray();
        var confirmed=Parties(roster);
        var colors=new Dictionary<string,Color>(StringComparer.OrdinalIgnoreCase);
        foreach(var player in roster)
            if(!String.IsNullOrEmpty(player.PartyId)&&confirmed.ContainsKey(player.PartyId))
                colors[player.Id]=confirmed[player.PartyId];

        // Only a player's latest known competitive match can suggest their current premade.
        var matches=roster.Where(player=>profiles.ContainsKey(player.Id))
            .SelectMany(player=>profiles[player.Id].Matches)
            .Where(match=>match.Completed&&match.Id!=""&&match.Id!=snapshot.Match&&match.Queue=="Competitive"&&
                match.Started>=now.AddDays(-7)&&match.Started<=now)
            .OrderByDescending(match=>match.Started).ToArray();
        var newest=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var match in matches)
            foreach(var row in match.Roster)
                if(!String.IsNullOrEmpty(row.Id)&&!newest.ContainsKey(row.Id))newest.Add(row.Id,match.Id);

        int groupIndex=confirmed.Count;
        foreach(var team in new[]{snapshot.Allies,snapshot.Enemies}){
            // Anyone not already colored can still belong to a premade. A lone presence party id
            // (e.g. one friend inside a trio) must not exclude that player from the history match.
            var members=new HashSet<string>(team.Where(player=>!String.IsNullOrEmpty(player.Id)).Select(player=>player.Id),StringComparer.OrdinalIgnoreCase);
            if(!members.Any(id=>!colors.ContainsKey(id)))continue;
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var match in matches){
                if(!seen.Add(match.Id))continue;
                // Whole premade (duo, trio, 4- or 5-stack) from the latest shared match, grouped per team + party.
                var groups=match.Roster.Where(row=>!String.IsNullOrEmpty(row.Id)&&members.Contains(row.Id)&&
                        newest[row.Id]==match.Id&&!String.IsNullOrEmpty(row.Team)&&!String.IsNullOrEmpty(row.PartyId))
                    .GroupBy(row=>row.Team+"/"+row.PartyId,StringComparer.OrdinalIgnoreCase);
                foreach(var group in groups){
                    var ids=group.Select(row=>row.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if(ids.Length<2||ids.All(id=>colors.ContainsKey(id)))continue;
                    // Extend an existing (confirmed) party instead of splitting it into two colors.
                    string colored=ids.FirstOrDefault(id=>colors.ContainsKey(id));
                    var color=colored!=null?colors[colored]:Color.FromArgb(175,PartyColors[groupIndex++%PartyColors.Length]);
                    foreach(string id in ids)if(!colors.ContainsKey(id))colors[id]=color;
                }
                if(members.All(id=>colors.ContainsKey(id)))break;
            }
        }
        return colors;
    }
    public static void Stripe(Graphics g,Dictionary<string,Color> colors,Player player,Rectangle rect){
        Color color;
        if(!String.IsNullOrEmpty(player.Id)&&colors.TryGetValue(player.Id,out color))
            using(var brush=new SolidBrush(color))
            using(var rounded=Skin.Round(rect,1))g.FillPath(brush,rounded);
    }
    public static void Recent(Graphics g,PlayerProfile profile,int x,int y,int step=19){
        if(profile==null)return;
        int i=0;
        foreach(var match in profile.Matches.Where(m=>m.Completed&&(m.Result==Outcome.Win||m.Result==Outcome.Loss||m.Result==Outcome.Draw))
                    .OrderByDescending(m=>m.Started).Take(5)){
            var rect=new Rectangle(x+i*step,y,16,16);
            Color fill=match.Result==Outcome.Win?Color.FromArgb(28,105,74):
                match.Result==Outcome.Loss?Color.FromArgb(147,48,70):Color.FromArgb(64,55,79);
            using(var path=Skin.Round(rect,4))
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            string result=match.Result==Outcome.Win?"W":match.Result==Outcome.Loss?"L":"D";
            using(var font=new Font("Segoe UI",7,FontStyle.Bold))
                TextRenderer.DrawText(g,result,font,rect,Color.White,TextFormatFlags.NoPadding|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            i++;
        }
    }
}
}

namespace Lunar {
sealed class DiscordActivity {
    readonly string details,state,map,agent,mapArt,agentArt;
    public readonly long Started;
    public string Key{get{return String.Join("|",details,state,map,agent,mapArt,agentArt,Started.ToString());}}
    DiscordActivity(string details,string state,string map,string agent,string mapArt,string agentArt,long started){
        this.details=details;this.state=state;this.map=map;this.agent=agent;
        this.mapArt=mapArt;this.agentArt=agentArt;Started=started;
    }
    public static DiscordActivity From(Snapshot data,long started){
        bool live=data.Status=="IN MATCH",select=data.Status=="AGENT SELECT";
        string map=live||select?data.Map:"";
        if(map=="Map unavailable")map="";
        string agentId=(live||select)&&data.Self!=null?data.Self.AgentId:null;
        string agent=Catalog.Agent(agentId);
        if(agent=="Agent unavailable")agent="";
        string mapImage=Art("maps",TrackArt.MapId(map),"splash.png");
        string agentImage=Art("agents",agentId,"displayicon.png");
        string phase=live?"In Match":select?"Agent Select":data.Self==null?"Play Lunar":"In the VALORANT lobby";
        string details=String.IsNullOrEmpty(map)?phase:map+" · "+phase;
        string state=agent;
        if(live&&data.AllyScore.HasValue&&data.EnemyScore.HasValue){
            string score=data.AllyScore.Value+" – "+data.EnemyScore.Value;
            details=String.IsNullOrEmpty(map)?"Live score  ·  "+score:map+"  ·  "+score;
            state=state==""?"In Match":state+"  ·  In Match";
        }
        return new DiscordActivity(details,state,map,agent,mapImage,agentImage,started);
    }
    static string Art(string directory,string id,string file){
        Guid value;
        return Guid.TryParse(id,out value)&&value!=Guid.Empty?"https://media.valorant-api.com/"+directory+"/"+value+"/"+file:"";
    }
    public object Payload(){
        var payload=new Dictionary<string,object>{
            {"details",details},{"type",0},{"status_display_type",2},{"timestamps",new{start=Started}}
        };
        if(state!="")payload["state"]=state;
        if(mapArt!="")payload["assets"]=agentArt==""
            ?(object)new{large_image=mapArt,large_text=map}
            :new{large_image=mapArt,large_text=map,small_image=agentArt,small_text=agent};
        else if(agentArt!="")payload["assets"]=new{large_image=agentArt,large_text=agent};
        return payload;
    }
}

sealed class DiscordPresence:IDisposable {
    const string ApplicationId="1572014557528985731";
    const string ConnectedStatus="Activity sent to Discord";
    const string ConnectingStatus="Connecting to Discord…";
    const string DisabledStatus="Activity sharing is off";
    readonly System.Threading.CancellationTokenSource cancel=new System.Threading.CancellationTokenSource();
    readonly System.Threading.SemaphoreSlim changed=new System.Threading.SemaphoreSlim(0,1);
    readonly object gate=new object();
    readonly string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lunar","discord.json");
    readonly int pid=Process.GetCurrentProcess().Id;
    readonly Task worker;
    DiscordActivity state=DiscordActivity.From(new Snapshot(),DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    string match="";
    volatile string status=ConnectingStatus;
    public bool Enabled{get;private set;}
    public bool Connected{get{return status==ConnectedStatus;}}
    public string Status{get{return status;}}

    public DiscordPresence(){
        Enabled=true;
        try{
            if(File.Exists(path)){
                var config=Json.Codec.DeserializeObject(File.ReadAllText(path));
                var enabled=Json.Get(config,"enabled");
                if(enabled is bool)Enabled=(bool)enabled;
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}catch(InvalidOperationException){}catch(ArgumentException){}
        if(!Enabled)status=DisabledStatus;
        worker=Task.Run((Func<Task>)Run);
    }
    public void SetEnabled(bool enabled){
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,Json.Codec.Serialize(new{enabled=enabled}));
        lock(gate){
            Enabled=enabled;
            status=enabled?ConnectingStatus:DisabledStatus;
        }
        Signal();
    }
    public void Update(Snapshot snapshot){
        lock(gate){
            long start=state.Started;
            if(!String.Equals(match,snapshot.Match,StringComparison.Ordinal)){
                match=snapshot.Match;
                start=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            var next=DiscordActivity.From(snapshot,start);
            if(state.Key==next.Key)return;
            state=next;
        }
        Signal();
    }
    void Signal(){
        if(cancel.IsCancellationRequested)return;
        try{changed.Release();}catch(System.Threading.SemaphoreFullException){}
    }
    async Task Run(){
        NamedPipeClientStream pipe=null;
        string sent="";
        DateTime refreshAt=DateTime.MinValue;
        try{
            while(!cancel.IsCancellationRequested){
                DiscordActivity activity;bool enabled;
                lock(gate){activity=state;enabled=Enabled;}
                try{
                    if(pipe!=null&&!enabled){
                        await SetActivity(pipe,null);
                        pipe.Dispose();pipe=null;
                    }
                    if(enabled&&pipe==null){
                        pipe=Connect();
                        if(pipe!=null){
                            await Write(pipe,0,new{v=1,client_id=ApplicationId});
                            await AwaitResponse(pipe,frame=>Json.S(frame,"evt")=="READY");
                            sent="";
                        }else status="Waiting for Discord desktop";
                    }
                    if(enabled&&pipe!=null&&(sent!=activity.Key||DateTime.UtcNow>=refreshAt)){
                        await SetActivity(pipe,activity);
                        sent=activity.Key;
                        refreshAt=DateTime.UtcNow.AddSeconds(15);
                        status=ConnectedStatus;
                    }
                    if(!enabled)status=DisabledStatus;
                }catch(IOException){
                    if(pipe!=null){pipe.Dispose();pipe=null;}
                    if(enabled)status="Discord connection interrupted";
                }catch(DiscordReplyException){
                    if(pipe!=null){pipe.Dispose();pipe=null;}
                    if(enabled)status="Discord rejected the activity";
                }catch(TimeoutException){
                    if(pipe!=null){pipe.Dispose();pipe=null;}
                    if(enabled)status="Discord timed out · Retrying";
                }
                await changed.WaitAsync(pipe==null&&enabled?5000:10000,cancel.Token);
                if(pipe!=null&&!pipe.IsConnected){pipe.Dispose();pipe=null;}
            }
        }catch(OperationCanceledException){}
        finally{
            if(pipe!=null)pipe.Dispose();
        }
    }
    NamedPipeClientStream Connect(){
        for(int index=0;index<10&&!cancel.IsCancellationRequested;index++){
            var pipe=new NamedPipeClientStream(".","discord-ipc-"+index,PipeDirection.InOut,PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            try{pipe.Connect(75);return pipe;}
            catch(TimeoutException){pipe.Dispose();}
            catch(IOException){pipe.Dispose();}
            catch(UnauthorizedAccessException){pipe.Dispose();}
        }
        return null;
    }
    async Task SetActivity(NamedPipeClientStream pipe,DiscordActivity activity){
        string nonce=Guid.NewGuid().ToString("N");
        object value=activity==null?null:activity.Payload();
        await Write(pipe,1,new{cmd="SET_ACTIVITY",args=new{pid=pid,activity=value},nonce=nonce});
        await AwaitResponse(pipe,frame=>Json.S(frame,"nonce")==nonce);
    }
    async Task AwaitResponse(NamedPipeClientStream pipe,Func<object,bool> matches){
        using(var deadline=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){
            deadline.CancelAfter(2500);
            while(true){
                int opcode,length;
                var header=await Read(pipe,8,deadline.Token);
                opcode=BitConverter.ToInt32(header,0);length=BitConverter.ToInt32(header,4);
                if(length<0||length>65536||opcode==2)throw new IOException("Discord closed its connection.");
                var bytes=await Read(pipe,length,deadline.Token);
                if(opcode==3){await WriteBytes(pipe,4,bytes);continue;}
                if(opcode!=1)continue;
                object frame;
                try{frame=Json.Codec.DeserializeObject(Encoding.UTF8.GetString(bytes));}
                catch(ArgumentException){throw new IOException("Invalid Discord response.");}
                catch(InvalidOperationException){throw new IOException("Invalid Discord response.");}
                if(Json.S(frame,"evt")=="ERROR")throw new DiscordReplyException();
                if(matches(frame))return;
            }
        }
    }
    Task Write(NamedPipeClientStream pipe,int opcode,object value){
        return WriteBytes(pipe,opcode,Encoding.UTF8.GetBytes(Json.Codec.Serialize(value)));
    }
    async Task WriteBytes(NamedPipeClientStream pipe,int opcode,byte[] bytes){
        var frame=new byte[8+bytes.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(opcode),0,frame,0,4);
        Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length),0,frame,4,4);
        Buffer.BlockCopy(bytes,0,frame,8,bytes.Length);
        await WaitForIo(pipe,pipe.WriteAsync(frame,0,frame.Length,cancel.Token),cancel.Token);
    }
    static async Task<byte[]> Read(NamedPipeClientStream pipe,int count,System.Threading.CancellationToken token){
        var data=new byte[count];
        int offset=0;
        while(offset<count){
            var pending=pipe.ReadAsync(data,offset,count-offset,token);
            await WaitForIo(pipe,pending,token);
            int received=await pending;
            if(received==0)throw new IOException("Discord disconnected.");
            offset+=received;
        }
        return data;
    }
    static async Task WaitForIo(NamedPipeClientStream pipe,Task io,System.Threading.CancellationToken token){
        using(var timeout=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token)){
            var delay=Task.Delay(2500,timeout.Token);
            if(await Task.WhenAny(io,delay)!=io){
                pipe.Dispose();
                try{await io;}catch(IOException){}catch(ObjectDisposedException){}catch(OperationCanceledException){}
                throw new TimeoutException();
            }
            timeout.Cancel();
            await io;
        }
    }
    sealed class DiscordReplyException:Exception {}
    public void Dispose(){
        cancel.Cancel();
        worker.ContinueWith(delegate{cancel.Dispose();changed.Dispose();},TaskScheduler.Default);
    }
}
}
