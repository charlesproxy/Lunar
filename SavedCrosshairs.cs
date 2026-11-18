using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Lunar {
sealed class SavedCrosshairs {
    readonly string path;
    public SavedCrosshairs():this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lunar","crosshairs.json")){}
    internal SavedCrosshairs(string path){this.path=path;}
    public CrosshairPreset[] Read(){
        if(!File.Exists(path))return new CrosshairPreset[0];
        var rows=Json.Codec.DeserializeObject(File.ReadAllText(path)) as object[];
        if(rows==null)throw new InvalidDataException("Invalid saved crosshair library.");
        return rows.Select(row=>CrosshairView.ReadPreset(row)).Where(item=>item!=null).ToArray();
    }
    public CrosshairPreset[] Update(CrosshairPreset preset,bool keep){
        using(var gate=new Mutex(false,@"Local\LunarCrosshairLibrary")){
            bool held;
            try{held=gate.WaitOne(650);}catch(AbandonedMutexException){held=true;}
            if(!held)throw new IOException("Crosshair library is busy.");
            try{
                var items=Read().ToList();
                int existing=items.FindIndex(item=>String.Equals(item.Code,preset.Code,StringComparison.Ordinal));
                if(keep){
                    if(existing>=0)items[existing]=preset;
                    else items.Insert(0,preset);
                }else if(existing>=0)items.RemoveAt(existing);
                Write(items);
                return items.ToArray();
            }finally{gate.ReleaseMutex();}
        }
    }
    void Write(List<CrosshairPreset> items){
        string directory=Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,"crosshair-"+Guid.NewGuid().ToString("N")+".tmp");
        try{
            File.WriteAllText(temporary,Json.Codec.Serialize(items.Select(item=>
                new{name=item.Name,style=item.Style,code=item.Code,source=item.Source}).ToArray()));
            if(File.Exists(path))File.Replace(temporary,path,null);
            else File.Move(temporary,path);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

sealed class CrosshairEditor:Form {
    readonly TextBox name=new TextBox(),code=new TextBox();
    readonly SoftButton save=new SoftButton{Text="Save crosshair",BackColor=Palette.Accent};
    readonly SoftButton cancel=new SoftButton{Text="Cancel",BackColor=Palette.Raised};
    CrosshairShape preview;
    string error="";
    public CrosshairPreset Preset{get;private set;}

    public CrosshairEditor(){
        Text="Add crosshair";
        BackColor=CrosshairView.Surface;
        ClientSize=new Size(570,302);
        FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.CenterParent;
        ShowInTaskbar=false;
        DoubleBuffered=true;
        KeyPreview=true;
        using(var outline=Skin.Round(ClientRectangle,14))Region=new Region(outline);
        InitInput(name,"Crosshair name",48,new Rectangle(27,95,356,29));
        InitInput(code,"VALORANT import code",4096,new Rectangle(27,167,356,29));
        code.TextChanged+=delegate{preview=CrosshairShape.Parse(code.Text);error="";Invalidate();};
        save.SetBounds(398,247,151,36);
        cancel.SetBounds(281,247,105,36);
        foreach(var button in new[]{save,cancel}){
            button.SurfaceColor=BackColor;
            button.ForeColor=Palette.Text;
            button.FlatStyle=FlatStyle.Flat;
            button.FlatAppearance.BorderSize=0;
            button.TextAlign=ContentAlignment.MiddleCenter;
            button.Cursor=Cursors.Hand;
        }
        save.Click+=delegate{
            string title=name.Text.Trim(),value=code.Text.Trim();
            if(title==""){error="Enter a name for this crosshair.";name.Focus();Invalidate();return;}
            if(preview==null){error="Paste a VALORANT profile code with a primary crosshair.";code.Focus();Invalidate();return;}
            Preset=new CrosshairPreset{Name=title,Code=value,Shape=preview,Style="Custom",Source=""};
            DialogResult=DialogResult.OK;
        };
        cancel.DialogResult=DialogResult.Cancel;
        AcceptButton=save;
        CancelButton=cancel;
        Controls.AddRange(new Control[]{name,code,save,cancel});
    }
    void InitInput(TextBox input,string label,int length,Rectangle bounds){
        input.AccessibleName=label;
    input.BackColor=Palette.Raised;
        input.ForeColor=Palette.Text;
        input.BorderStyle=BorderStyle.None;
        input.Font=new Font("Segoe UI",10);
        input.MaxLength=length;
        input.SetBounds(bounds.X+8,bounds.Y+6,bounds.Width-16,input.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        CrosshairView.Card(g,ClientRectangle,BackColor,CrosshairView.Stroke,14);
        ProfileCanvas.T(g,"Add crosshair",17,Palette.Text,new Rectangle(22,17,420,40),true);
        ProfileCanvas.T(g,"Name",9,Palette.Muted,new Rectangle(26,69,356,23),true);
        ProfileCanvas.T(g,"VALORANT import code",9,Palette.Muted,new Rectangle(26,141,356,23),true);
        var fill=Palette.Raised;
        CrosshairView.Card(g,new Rectangle(27,95,356,33),fill,CrosshairView.Stroke,7);
        CrosshairView.Card(g,new Rectangle(27,167,356,33),fill,CrosshairView.Stroke,7);
        var sample=new Rectangle(403,68,142,142);
        CrosshairView.DrawPreview(g,sample,preview);
        ProfileCanvas.T(g,preview==null?"Paste a code to preview":"Primary · actual size",8,Palette.Muted,
            new Rectangle(sample.X,sample.Bottom+2,sample.Width,22));
        ProfileCanvas.T(g,error,9,Palette.Red,new Rectangle(26,210,518,27));
    }
}
}
