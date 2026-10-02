using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Frame { public int updateMs, seq, width, height, total, civilians, evidence, reports; public string status; public Box[] boxes; public Marker[] items; }
public class Marker { public float x,y,d; public string kind,name; public Joint[] bones; }
public class Joint { public float x,y; public bool valid; }
public class Box { public float x,y,w,h,d,hp,maxhp; public string kind,state; public Joint[] bones; }
public class Options { public bool enabled=true, distance=true, civilians=true, health=true, healthbar=true, status=true, skeleton=true, boxes=true, inactive=false, evidence=true, reports=true, arrested=false; public int range=150, color=0, updateMs=16; }
public class Overlay : Form {
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
    bool mouseInteractive;
    struct RECT { public int l,t,r,b; }
    struct POINT { public int x,y; }
    static readonly int[] updateIntervals={100,50,33,16,8};
    readonly Timer refreshTimer=new Timer();
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly Font text=new Font("Segoe UI",12), title=new Font("Segoe UI",16,FontStyle.Bold), hint=new Font("Segoe UI",10);
    readonly Color[] colors={Color.FromArgb(255,75,90),Color.FromArgb(70,230,255),Color.FromArgb(255,214,70)};
    readonly string[] groups={"Общее","Персонажи","Здоровье","Объекты"};
    static readonly int[,] links={{0,1},{1,2},{2,3},{2,4},{4,5},{5,6},{2,7},{7,8},{8,9},{3,10},{10,11},{11,12},{3,13},{13,14},{14,15}};
    long telemetryStamp; double dataHz; DateTime rateStart=DateTime.UtcNow; int rateFrames;
    Options options=new Options(); Frame frame; DateTime lastFrame=DateTime.MinValue;
    bool menu=true, active, hotkeys; int selected=0, group=0; IntPtr game; int ticks;
    readonly int[] hotkeyCodes={0x2D,0x75,0x23,0x26,0x28,0x25,0x27,0x0D,0x09};
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
        if(Array.IndexOf(updateIntervals,options.updateMs)<0)options.updateMs=16;
        refreshTimer.Interval=options.updateMs;refreshTimer.Tick+=(s,e)=>TickOverlay();refreshTimer.Start();
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
        if(m.Msg==0x0084) {
            long point=m.LParam.ToInt64();
            var client=PointToClient(new Point((short)(point&65535),(short)((point>>16)&65535)));
            m.Result=(IntPtr)(menu&&active&&MenuBounds().Contains(client)?1:-1);return;
        }
        if(m.Msg==0x0021){m.Result=(IntPtr)3;return;}
        if(m.Msg==0x0312 && active) {
            int id=m.WParam.ToInt32()-100;
            if(id==0){menu=!menu;RebindMenu();}
            if(id==1){options.enabled=!options.enabled;Save();}
            if(id==2){Close();return;}
            if(menu) {
                if(id==3)selected=(selected+RowCount()-1)%RowCount();
                if(id==4)selected=(selected+1)%RowCount();
                if(id==8){group=(group+1)%groups.Length;selected=0;}
                if(id>=5&&id<=7)ChangeOption(id==5?-1:1);

            }
            Invalidate(); return;
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { BindKeys(false); refreshTimer.Dispose();text.Dispose();title.Dispose();hint.Dispose();base.OnFormClosed(e); }
    Rectangle MenuBounds() { return new Rectangle(24,Math.Max(12,Math.Min(50,Height-490)),Math.Min(620,Math.Max(300,Width-48)),460); }
    int RowCount() { return group==0?6:group==1?6:2; }
    string[] Rows() {
        if(group==0)return new[]{"ESP: "+OnOff(options.enabled),"Дальность: "+options.range+" м","Расстояние: "+OnOff(options.distance),"Цвет врагов: "+new[]{"красный","голубой","жёлтый"}[options.color],"Обновление: "+options.updateMs+" мс (~"+Math.Round(1000.0/options.updateMs)+" Гц)","Закрыть оверлей"};
        if(group==1)return new[]{"Гражданские: "+OnOff(options.civilians),"Арестованные: "+OnOff(options.arrested),"Мёртвые / без сознания: "+OnOff(options.inactive),"Рамки: "+OnOff(options.boxes),"Скелет: "+OnOff(options.skeleton),"Статусы: "+OnOff(options.status)};
        if(group==2)return new[]{"ХП числом: "+OnOff(options.health),"Полоска ХП: "+OnOff(options.healthbar)};
        return new[]{"Улики / брошенное оружие: "+OnOff(options.evidence),"Пострадавшие / для доклада: "+OnOff(options.reports)};
    }
    void ChangeOption(int direction) {
        if(group==0) {
            if(selected==0)options.enabled=!options.enabled;
            if(selected==1)options.range=Math.Max(25,Math.Min(500,options.range+direction*25));
            if(selected==2)options.distance=!options.distance;
            if(selected==3)options.color=(options.color+(direction<0?2:1))%3;
            if(selected==4) {
                int index=Array.IndexOf(updateIntervals,options.updateMs);
                options.updateMs=updateIntervals[(index+direction+updateIntervals.Length)%updateIntervals.Length];
                refreshTimer.Interval=options.updateMs;
            }
            if(selected==5){Close();return;}
        } else if(group==1) {
            if(selected==0)options.civilians=!options.civilians;
            if(selected==1)options.arrested=!options.arrested;
            if(selected==2)options.inactive=!options.inactive;
            if(selected==3)options.boxes=!options.boxes;
            if(selected==4)options.skeleton=!options.skeleton;
            if(selected==5)options.status=!options.status;
        } else if(group==2) {
            if(selected==0)options.health=!options.health;
            if(selected==1)options.healthbar=!options.healthbar;
        } else {
            if(selected==0)options.evidence=!options.evidence;
            if(selected==1)options.reports=!options.reports;
        }
        Save();Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);if(!menu||!active)return;
        var bounds=MenuBounds();int x=e.X-bounds.X,y=e.Y-bounds.Y;
        if(y>=60&&y<100){group=Math.Max(0,Math.Min(3,x/(bounds.Width/4)));selected=0;Invalidate();return;}
        int row=(y-115)/37;
        if(y>=115&&row>=0&&row<RowCount()){selected=row;ChangeOption(e.Button==MouseButtons.Right?-1:1);}
    }
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
        POINT cursor; bool interactive=menu&&GetCursorPos(out cursor)&&MenuBounds().Contains(PointToClient(new Point(cursor.x,cursor.y)));
        if(interactive!=mouseInteractive) {
            int style=GetWindowLong(Handle,-20);
            SetWindowLong(Handle,-20,interactive?style&~0x20:style|0x20);
            mouseInteractive=interactive;
        }
        try {
            string path=Path.Combine(root,"telemetry.json");
            long stamp=File.GetLastWriteTimeUtc(path).Ticks;
            if(stamp!=telemetryStamp) {
            using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(fs)) {
                var next=json.Deserialize<Frame>(reader.ReadToEnd());
                if(next!=null&&(frame==null||next.seq!=frame.seq)) {frame=next;lastFrame=DateTime.UtcNow;telemetryStamp=stamp;rateFrames++;
                    double elapsed=(lastFrame-rateStart).TotalSeconds;if(elapsed>=1){dataHz=rateFrames/elapsed;rateFrames=0;rateStart=lastFrame;}}
            }
            }
        } catch { }
        Invalidate();
    }
    void DrawSkeleton(Graphics g,Joint[] bones,float sx,float sy,Color color,float maxLengthSquared) {
        if(bones==null)return;
        using(var outline=new Pen(Color.Black,4))
        using(var pen=new Pen(color,2)) {
            for(int i=0;i<links.GetLength(0);i++) {
                int a=links[i,0],z=links[i,1];if(a>=bones.Length||z>=bones.Length)continue;
                var p=bones[a];var q=bones[z];if(p==null||q==null||!p.valid||!q.valid)continue;
                float dx=(p.x-q.x)*sx,dy=(p.y-q.y)*sy;if(dx*dx+dy*dy>maxLengthSquared)continue;
                g.DrawLine(outline,p.x*sx,p.y*sy,q.x*sx,q.y*sy);
                g.DrawLine(pen,p.x*sx,p.y*sy,q.x*sx,q.y*sy);
            }
        }
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
                if(b.state=="arrested"&&!options.arrested)continue;
                if(!options.inactive&&(b.state=="dead"||b.state=="unconscious"))continue;
                Color color=b.kind=="civilian"?Color.FromArgb(70,240,125):colors[options.color%3];
                if(b.state=="dead"||b.state=="unconscious"||b.state=="arrested")color=Color.Silver;
                var rect=new RectangleF(b.x*sx,b.y*sy,b.w*sx,b.h*sy);
                if(rect.Right<0||rect.Bottom<0||rect.Left>Width||rect.Top>Height)continue;
                if(options.boxes) {
                    using(var black=new Pen(Color.Black,4))g.DrawRectangle(black,rect.X,rect.Y,rect.Width,rect.Height);
                    using(var pen=new Pen(color,2))g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
                }
                if(options.skeleton)DrawSkeleton(g,b.bones,sx,sy,color,rect.Height*rect.Height*4);
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
                if(options.skeleton)DrawSkeleton(g,item.bones,sx,sy,color,Width*Width+Height*Height);
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
        var boundsMenu=MenuBounds();float x=boundsMenu.X,y=boundsMenu.Y,w=boundsMenu.Width;
        using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,boundsMenu);
        using(var pen=new Pen(colors[options.color],2))g.DrawRectangle(pen,boundsMenu);
        Label(g,"READY OR NOT  /  SOLO ESP",x+18,y+12,Color.White,title);
        for(int i=0;i<groups.Length;i++) {
            var tab=new RectangleF(x+i*w/4,y+60,w/4,38);
            using(var brush=new SolidBrush(i==group?Color.FromArgb(52,62,80):Color.FromArgb(30,34,44)))g.FillRectangle(brush,tab);
            Label(g,groups[i],tab.X+10,tab.Y+8,i==group?colors[options.color]:Color.LightGray,text);
        }
        var rows=Rows();
        for(int i=0;i<rows.Length;i++) {
            if(i==selected)using(var brush=new SolidBrush(Color.FromArgb(38,44,57)))g.FillRectangle(brush,x+10,y+112+i*37,w-20,34);
            Label(g,(i==selected?"›  ":"   ")+rows[i],x+18,y+115+i*37,i==selected?colors[options.color]:Color.White,text);
        }
        string status=!fresh?"Ожидание данных игры":frame.status=="ready"?"Подозреваемые: "+frame.total+" · гражданские: "+frame.civilians:frame.status=="solo_only"?"ESP доступен в одиночном режиме":frame.status=="no_pawn"?"Загрузите одиночную миссию":frame.status;
        Label(g,status,x+18,y+350,Color.LightGray,hint);
        Label(g,"Данные: "+Math.Round(dataHz)+" Гц · Улик: "+(fresh?frame.evidence:0)+" · Для доклада: "+(fresh?frame.reports:0),x+18,y+375,Color.LightGray,hint);
        Label(g,"Insert — меню · F6 — ESP · End — выход",x+18,y+406,Color.LightGray,hint);
        Label(g,"Мышь / Tab — раздел · ↑↓ — выбор · ←→ / Enter — изменить",x+18,y+430,Color.LightGray,hint);
    }
    static string OnOff(bool value) { return value?"ВКЛ":"ВЫКЛ"; }
    [STAThread] public static void Main() {
        bool created; using(var mutex=new System.Threading.Mutex(true,"Local\\RoNSoloESP",out created)) {
            if(!created){MessageBox.Show("ESP уже запущен. Insert — меню, End — выход.","Ready Or Not ESP");return;}
            SetProcessDPIAware(); Application.EnableVisualStyles();
            try { Launcher.Run(); } catch(Exception ex) { MessageBox.Show(ex.Message,"Ready Or Not ESP",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        }
    }
}
