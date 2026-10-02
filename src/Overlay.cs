using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Frame { public int seq, width, height, total, civilians, evidence, reports; public string status; public Box[] boxes; public Marker[] items; }
public class Marker { public float x,y,d; public string kind,name; public Joint[] bones; }
public class Joint { public float x,y; public bool valid; }
public class Box { public float x,y,w,h,d,hp,maxhp; public string kind,state; public Joint[] bones; }
public class Options { public bool enabled=true, distance=true, civilians=true, health=true, healthbar=true, status=true, skeleton=true, boxes=true, inactive=false, evidence=true, reports=true; public int range=150, color=0; }
public class Overlay : Form {
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    struct RECT { public int l,t,r,b; }
    struct POINT { public int x,y; }
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly Font text=new Font("Segoe UI",12), title=new Font("Segoe UI",16,FontStyle.Bold), hint=new Font("Segoe UI",10);
    readonly Color[] colors={Color.FromArgb(255,75,90),Color.FromArgb(70,230,255),Color.FromArgb(255,214,70)};
    Options options=new Options(); Frame frame; DateTime lastFrame=DateTime.MinValue;
    bool menu=true, active, hotkeys; int selected=0; IntPtr game; int ticks;
    readonly int[] hotkeyCodes={0x2D,0x75,0x23,0x26,0x28,0x25,0x27,0x0D};
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x80000|0x20|0x08000000|0x80; return p; } }
    public Overlay() {
        Text="Ready Or Not ESP"; FormBorderStyle=FormBorderStyle.None;
        BackColor=Color.Magenta; TransparencyKey=Color.Magenta; TopMost=true;
        ShowInTaskbar=false; DoubleBuffered=true; AutoScaleMode=AutoScaleMode.None;
        try {
            string saved=File.ReadAllText(Path.Combine(root,"settings.json"));
            options=json.Deserialize<Options>(saved);
            if(options!=null&&!json.Deserialize<Dictionary<string,object>>(saved).ContainsKey("healthbar"))options.healthbar=options.health;
        } catch { }
        if(options==null)options=new Options();
        options.range=Math.Max(25,Math.Min(500,options.range)); options.color=Math.Max(0,options.color)%3;
        var timer=new Timer { Interval=16 }; timer.Tick+=(s,e)=>TickOverlay(); timer.Start();
    }
    void BindKeys(bool bind) {
        if(bind==hotkeys)return;
        for(int i=0;i<hotkeyCodes.Length;i++) {
            if(bind && (i<3||menu))RegisterHotKey(Handle,100+i,0x4000,(uint)hotkeyCodes[i]);
            else UnregisterHotKey(Handle,100+i);
        }
        hotkeys=bind;
    }
    void RebindMenu() { BindKeys(false); BindKeys(active); }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x0312 && active) {
            int id=m.WParam.ToInt32()-100;
            if(id==0){menu=!menu;RebindMenu();}
            if(id==1){options.enabled=!options.enabled;Save();}
            if(id==2){Close();return;}
            if(menu) {
                if(id==3)selected=(selected+13)%14;
                if(id==4)selected=(selected+1)%14;
                if(id>=5&&id<=7) {
                    bool left=id==5;
                    if(selected==0)options.enabled=!options.enabled;
                    if(selected==1)options.range=Math.Max(25,Math.Min(500,options.range+(left?-25:25)));
                    if(selected==2)options.distance=!options.distance;
                    if(selected==3)options.color=(options.color+(left?2:1))%3;
                    if(selected==4)options.civilians=!options.civilians;
                    if(selected==5)options.health=!options.health;
                    if(selected==6)options.healthbar=!options.healthbar;
                    if(selected==7)options.status=!options.status;
                    if(selected==8)options.skeleton=!options.skeleton;
                    if(selected==9)options.boxes=!options.boxes;
                    if(selected==10)options.inactive=!options.inactive;
                    if(selected==11)options.evidence=!options.evidence;
                    if(selected==12)options.reports=!options.reports;
                    if(selected==13){Close();return;}
                    Save();
                }
            }
            Invalidate(); return;
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { BindKeys(false); text.Dispose();title.Dispose();hint.Dispose();base.OnFormClosed(e); }
    void Save() { try { File.WriteAllText(Path.Combine(root,"settings.json"),json.Serialize(options)); } catch { } }
    void TickOverlay() {
        if (++ticks%60==1 || game==IntPtr.Zero) {
            var p=Process.GetProcessesByName("ReadyOrNotSteam-Win64-Shipping").FirstOrDefault();
            game=p==null ? IntPtr.Zero : p.MainWindowHandle;
            if(p!=null)p.Dispose();
        }
        active=game!=IntPtr.Zero && GetForegroundWindow()==game && !IsIconic(game);
        BindKeys(active);
        if(!active) { if(Visible)Hide(); return; }
        RECT r; POINT pt=new POINT();
        if(!GetClientRect(game,out r)||!ClientToScreen(game,ref pt))return;
        var bounds=new Rectangle(pt.x,pt.y,r.r-r.l,r.b-r.t);
        if(bounds.Width<20||bounds.Height<20)return;
        if(Bounds!=bounds)Bounds=bounds;
        if(!Visible)Show();
        try {
            string path=Path.Combine(root,"telemetry.json");
            using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(fs)) {
                var next=json.Deserialize<Frame>(reader.ReadToEnd());
                if(next!=null&&(frame==null||next.seq!=frame.seq)) {frame=next;lastFrame=DateTime.UtcNow;}
            }
        } catch { }
        Invalidate();
    }
    void Label(Graphics g,string value,float x,float y,Color color,Font font) {
        using(var shadow=new SolidBrush(Color.Black))g.DrawString(value,font,shadow,x+1,y+1);
        using(var brush=new SolidBrush(color))g.DrawString(value,font,brush,x,y);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); if(!active)return; var g=e.Graphics;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        bool fresh=frame!=null&&(DateTime.UtcNow-lastFrame).TotalMilliseconds<600;
        if(fresh&&frame.status=="ready"&&options.enabled&&frame.width>0&&frame.height>0&&frame.boxes!=null) {
            float sx=(float)ClientSize.Width/frame.width,sy=(float)ClientSize.Height/frame.height;
            var occupied=new List<RectangleF>();
            foreach(var b in frame.boxes) {
                if(b.d>options.range||b.w<=0||b.h<=0)continue;
                if(b.kind=="civilian"&&!options.civilians)continue;
                if(!options.inactive&&(b.state=="dead"||b.state=="unconscious"||b.state=="arrested"))continue;
                Color color=b.kind=="civilian"?Color.FromArgb(70,240,125):colors[options.color%3];
                if(b.state=="dead"||b.state=="unconscious"||b.state=="arrested")color=Color.Silver;
                var rect=new RectangleF(b.x*sx,b.y*sy,b.w*sx,b.h*sy);
                if(rect.Right<0||rect.Bottom<0||rect.Left>Width||rect.Top>Height)continue;
                if(options.boxes) {
                    using(var black=new Pen(Color.Black,4))g.DrawRectangle(black,rect.X,rect.Y,rect.Width,rect.Height);
                    using(var pen=new Pen(color,2))g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
                }
                if(options.skeleton&&b.bones!=null) {
                    int[,] links={{0,1},{1,2},{2,3},{2,4},{4,5},{5,6},{2,7},{7,8},{8,9},{3,10},{10,11},{11,12},{3,13},{13,14},{14,15}};
                    for(int i=0;i<links.GetLength(0);i++) {
                        int a=links[i,0],z=links[i,1]; if(a>=b.bones.Length||z>=b.bones.Length)continue;
                        var p=b.bones[a];var q=b.bones[z];if(p==null||q==null||!p.valid||!q.valid)continue;
                        float dx=(p.x-q.x)*sx,dy=(p.y-q.y)*sy;
                        if(dx*dx+dy*dy>rect.Height*rect.Height*4)continue;
                        using(var pen=new Pen(Color.Black,4))g.DrawLine(pen,p.x*sx,p.y*sy,q.x*sx,q.y*sy);
                        using(var pen=new Pen(color,2))g.DrawLine(pen,p.x*sx,p.y*sy,q.x*sx,q.y*sy);
                    }
                }
                string label=b.kind=="civilian"?"Гражд.":"Подозр.";
                if(options.distance)label+=" · "+Math.Round(b.d)+" м";
                var labels=new List<string>(); labels.Add(label);
                if(options.status) {
                    string state=b.state=="surrendered"?"Сдался":b.state=="arrested"?"Арестован":b.state=="unconscious"?"Без сознания":b.state=="dead"?"Мёртв":"Активен";
                    labels.Add(state);
                }
                if(options.healthbar&&b.maxhp>0) {
                    float ratio=Math.Max(0,Math.Min(1,b.hp/b.maxhp));
                    using(var brush=new SolidBrush(Color.Black))g.FillRectangle(brush,rect.X-9,rect.Y-1,6,rect.Height+2);
                    using(var brush=new SolidBrush(Color.FromArgb((int)(255*(1-ratio)),(int)(230*ratio),60)))g.FillRectangle(brush,rect.X-8,rect.Bottom-rect.Height*ratio,4,rect.Height*ratio);
                }
                if(options.health&&b.maxhp>0)labels.Add(Math.Round(b.hp)+" / "+Math.Round(b.maxhp)+" HP");
                float lw=labels.Max(value=>g.MeasureString(value,hint).Width)+8,lh=labels.Count*19+4;
                var panel=new RectangleF(Math.Max(2,Math.Min(Width-lw-2,rect.X+rect.Width/2-lw/2)),rect.Y-lh-5,lw,lh);
                for(int attempt=0;attempt<30&&occupied.Any(area=>area.IntersectsWith(panel));attempt++)panel.Y-=lh+5;
                if(panel.Y<2)panel.Y=rect.Bottom+8;
                occupied.Add(panel);
                using(var line=new Pen(color,1))g.DrawLine(line,rect.X+rect.Width/2,rect.Y,panel.X+panel.Width/2,panel.Bottom);
                using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,panel);
                for(int i=0;i<labels.Count;i++)Label(g,labels[i],panel.X+4,panel.Y+2+i*19,i==labels.Count-1&&options.health?Color.White:color,hint);
            }
            foreach(var item in frame.items??new Marker[0]) {
                if(item.d>options.range||(item.kind=="evidence"?!options.evidence:!options.reports))continue;
                float px=item.x*sx,py=item.y*sy;
                if(px<0||py<0||px>Width||py>Height)continue;
                Color color=item.kind=="evidence"?Color.Gold:Color.FromArgb(80,220,255);
                if(options.skeleton&&item.bones!=null) {
                    int[,] links={{0,1},{1,2},{2,3},{2,4},{4,5},{5,6},{2,7},{7,8},{8,9},{3,10},{10,11},{11,12},{3,13},{13,14},{14,15}};
                    for(int i=0;i<links.GetLength(0);i++) {
                        int a=links[i,0],z=links[i,1];if(a>=item.bones.Length||z>=item.bones.Length)continue;
                        var p=item.bones[a];var q=item.bones[z];if(p==null||q==null||!p.valid||!q.valid)continue;
                        float dx=(p.x-q.x)*sx,dy=(p.y-q.y)*sy;
                        if(dx*dx+dy*dy>Width*Width+Height*Height)continue;
                        using(var pen=new Pen(Color.Black,4))g.DrawLine(pen,p.x*sx,p.y*sy,q.x*sx,q.y*sy);
                        using(var pen=new Pen(color,2))g.DrawLine(pen,p.x*sx,p.y*sy,q.x*sx,q.y*sy);
                    }
                }
                var diamond=new[]{new PointF(px,py-7),new PointF(px+7,py),new PointF(px,py+7),new PointF(px-7,py),new PointF(px,py-7)};
                using(var pen=new Pen(Color.Black,4))g.DrawLines(pen,diamond);
                using(var pen=new Pen(color,2))g.DrawLines(pen,diamond);
                string value=(item.kind=="evidence"?"Улика: ":"Сообщить: ")+(item.name??"Объект");
                if(options.distance)value+=" · "+Math.Round(item.d)+" м";
                float lw=g.MeasureString(value,hint).Width+8;
                var panel=new RectangleF(Math.Max(2,Math.Min(Width-lw-2,px-lw/2)),py-32,lw,23);
                for(int attempt=0;attempt<40&&occupied.Any(area=>area.IntersectsWith(panel));attempt++)panel.Y-=28;
                if(panel.Y<2)panel.Y=py+10;
                occupied.Add(panel);
                using(var pen=new Pen(color,1))g.DrawLine(pen,px,py,panel.X+panel.Width/2,panel.Bottom);
                using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,panel);
                Label(g,value,panel.X+4,panel.Y+2,color,hint);
            }
        }
        if(!menu)return;
        float x=40,y=70;
        using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,x,y,510,690);
        using(var pen=new Pen(colors[options.color%3],2))g.DrawRectangle(pen,x,y,510,690);
        Label(g,"READY OR NOT  /  ESP",x+20,y+15,Color.White,title);
        string[] rows={"ESP: "+OnOff(options.enabled),"Дальность: "+options.range+" м","Расстояние: "+OnOff(options.distance),"Цвет подозреваемых: "+new[]{"красный","голубой","жёлтый"}[options.color%3],"Гражданские: "+OnOff(options.civilians),"ХП числом: "+OnOff(options.health),"Полоска ХП: "+OnOff(options.healthbar),"Статусы: "+OnOff(options.status),"Скелет: "+OnOff(options.skeleton),"Рамки: "+OnOff(options.boxes),"Мёртвые / без сознания / арестованные: "+OnOff(options.inactive),"Улики / брошенное оружие: "+OnOff(options.evidence),"Пострадавшие / объекты для доклада: "+OnOff(options.reports),"Закрыть оверлей"};
        for(int i=0;i<rows.Length;i++)Label(g,(i==selected?"›  ":"   ")+rows[i],x+20,y+65+i*35,i==selected?colors[options.color%3]:Color.White,text);
        string status=!fresh?"Ожидание данных игры":frame.status=="ready"?"Активных: подозреваемые "+frame.total+" · гражданские "+frame.civilians:frame.status=="solo_only"?"ESP доступен в одиночном режиме":frame.status=="no_pawn"?"Загрузите одиночную миссию":frame.status;
        Label(g,status,x+20,y+565,Color.LightGray,hint);
        if(fresh)Label(g,"Улик: "+frame.evidence+" · Для доклада: "+frame.reports,x+20,y+590,Color.LightGray,hint);
        Label(g,"Insert — меню  ·  F6 — ESP  ·  End — выход",x+20,y+625,Color.LightGray,hint);
        Label(g,"↑↓ — выбор  ·  ←→ / Enter — изменить",x+20,y+653,Color.LightGray,hint);
    }
    static string OnOff(bool value) { return value?"ВКЛ":"ВЫКЛ"; }
    [STAThread] public static void Main() {
        bool created; using(var mutex=new System.Threading.Mutex(true,"Local\\RoNSoloESP",out created)) {
            if(!created)return; SetProcessDPIAware(); Application.EnableVisualStyles(); Application.Run(new Overlay());
        }
    }
}
